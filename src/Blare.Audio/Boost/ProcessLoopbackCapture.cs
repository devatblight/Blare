using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.System.Com.StructuredStorage;

namespace BLight.Blare.Audio.Boost;

/// <summary>
/// Milestone-0 Spike B result (see plan §3): captures one process's
/// rendered audio via per-process WASAPI loopback
/// (ActivateAudioInterfaceAsync + AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK),
/// confirmed working against CsWin32's projection with no hand-rolled
/// interop fallback needed. This is capture only — gain/limiter/re-render
/// and the mute-original coordination are still open Phase 2 work; this
/// class exists to prove and exercise the capture mechanism itself.
///
/// C# doesn't allow unsafe/pointer code and `await` in the same method
/// body, so the async coordination (waiting on the activation callback)
/// and the unsafe COM/pointer calls are deliberately split into separate
/// methods rather than interleaved.
/// </summary>
public sealed class ProcessLoopbackCapture
{
    // Not projected as named enums by CsWin32 for these particular
    // parameters (they're plain UInt32 in the generated signatures) — raw
    // values from audioclient.h / mmreg.h.
    private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x1;
    private const ushort WAVE_FORMAT_IEEE_FLOAT = 3;

    private const int Channels = 2;
    private const int SampleRateHz = 48000;
    private const int BitsPerSample = 32;

    private sealed class CompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        public TaskCompletionSource<IActivateAudioInterfaceAsyncOperation> Completion { get; } = new();

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation) =>
            Completion.SetResult(activateOperation);
    }

    public async Task<LoopbackCaptureResult> CaptureAsync(uint targetProcessId, TimeSpan duration)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var handler = new CompletionHandler();
        StartActivation(targetProcessId, handler);

        var operation = await handler.Completion.Task;
        var audioClient = GetActivationResult(operation);
        var activationLatency = stopwatch.Elapsed;

        var captureClient = InitializeCapture(audioClient);
        audioClient.Start();

        long framesRead = 0;
        long packetsRead = 0;
        float peak = 0;
        var deadline = DateTime.UtcNow + duration;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
            DrainAvailablePackets(captureClient, ref framesRead, ref packetsRead, ref peak);
        }

        audioClient.Stop();

        return new LoopbackCaptureResult(activationLatency, framesRead, packetsRead, peak);
    }

    private static unsafe void StartActivation(uint targetProcessId, CompletionHandler handler)
    {
        var activationParams = new AUDIOCLIENT_ACTIVATION_PARAMS
        {
            ActivationType = AUDIOCLIENT_ACTIVATION_TYPE.AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK,
            Anonymous = new AUDIOCLIENT_ACTIVATION_PARAMS._Anonymous_e__Union
            {
                ProcessLoopbackParams = new AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
                {
                    TargetProcessId = targetProcessId,
                    ProcessLoopbackMode = PROCESS_LOOPBACK_MODE.PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE,
                },
            },
        };

        var propVariant = BuildActivationPropVariant(activationParams);
        var audioClientIid = typeof(IAudioClient).GUID;

        PInvoke.ActivateAudioInterfaceAsync(
            PInvoke.VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
            in audioClientIid,
            propVariant,
            handler,
            out _);
    }

    private static unsafe IAudioClient GetActivationResult(IActivateAudioInterfaceAsyncOperation operation)
    {
        HRESULT activateResult;
        operation.GetActivateResult(&activateResult, out var activatedInterface);
        activateResult.ThrowOnFailure();
        return (IAudioClient)activatedInterface;
    }

    private static unsafe IAudioCaptureClient InitializeCapture(IAudioClient audioClient)
    {
        var format = new WAVEFORMATEX
        {
            wFormatTag = WAVE_FORMAT_IEEE_FLOAT,
            nChannels = Channels,
            nSamplesPerSec = SampleRateHz,
            wBitsPerSample = BitsPerSample,
            nBlockAlign = (ushort)(Channels * BitsPerSample / 8),
            nAvgBytesPerSec = (uint)(SampleRateHz * Channels * BitsPerSample / 8),
            cbSize = 0,
        };

        audioClient.Initialize(
            AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
            AUDCLNT_STREAMFLAGS_LOOPBACK,
            2_000_000, // 200ms buffer, in 100-ns units
            0,
            &format,
            null);

        var captureClientIid = typeof(IAudioCaptureClient).GUID;
        audioClient.GetService(&captureClientIid, out var captureClientObj);
        return (IAudioCaptureClient)captureClientObj;
    }

    private static unsafe void DrainAvailablePackets(
        IAudioCaptureClient captureClient,
        ref long framesRead,
        ref long packetsRead,
        ref float peak)
    {
        captureClient.GetNextPacketSize(out var framesAvailable);

        while (framesAvailable > 0)
        {
            byte* dataPtr;
            captureClient.GetBuffer(&dataPtr, out var numFrames, out var flags, null, null);

            if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) == 0 && numFrames > 0)
            {
                var samples = new ReadOnlySpan<float>(dataPtr, (int)(numFrames * Channels));
                foreach (var sample in samples)
                {
                    var abs = Math.Abs(sample);
                    if (abs > peak)
                    {
                        peak = abs;
                    }
                }
            }

            captureClient.ReleaseBuffer(numFrames);
            framesRead += numFrames;
            packetsRead++;

            captureClient.GetNextPacketSize(out framesAvailable);
        }
    }

    private static unsafe PROPVARIANT BuildActivationPropVariant(AUDIOCLIENT_ACTIVATION_PARAMS activationParams)
    {
        // AUDIOCLIENT_ACTIVATION_PARAMS is passed to ActivateAudioInterfaceAsync via a
        // PROPVARIANT blob (VT_BLOB) per the documented pattern — this is the one place
        // the friendly CsWin32 wrapper can't hide the raw marshaling.
        var size = Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>();
        var blobPtr = Marshal.AllocCoTaskMem(size);
        Marshal.StructureToPtr(activationParams, blobPtr, false);

        return new PROPVARIANT
        {
            Anonymous = new PROPVARIANT._Anonymous_e__Union
            {
                Anonymous = new PROPVARIANT._Anonymous_e__Union._Anonymous_e__Struct_unmanaged
                {
                    vt = Windows.Win32.System.Variant.VARENUM.VT_BLOB,
                    Anonymous = new PROPVARIANT._Anonymous_e__Union._Anonymous_e__Struct_unmanaged._Anonymous_e__Union_unmanaged
                    {
                        blob = new Windows.Win32.System.Com.BLOB
                        {
                            cbSize = (uint)size,
                            pBlobData = (byte*)blobPtr,
                        },
                    },
                },
            },
        };
    }
}

public sealed record LoopbackCaptureResult(
    TimeSpan ActivationLatency,
    long FramesCaptured,
    long PacketsCaptured,
    float PeakAmplitude);
