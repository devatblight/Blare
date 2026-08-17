using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Media.Audio;

namespace Blight.Blare.Audio.Boost;

/// <summary>
/// Renders gained+limited float samples to the default output device via a
/// normal (non-loopback) shared-mode IAudioClient.
///
/// Shared-mode WASAPI does NOT accept an arbitrary caller-chosen format —
/// it must match the device's current mix format, or Initialize fails with
/// AUDCLNT_E_UNSUPPORTED_FORMAT (confirmed the hard way: hardcoding
/// 48kHz/float32/stereo threw exactly that against real hardware). So this
/// queries GetMixFormat and initializes with the device's own format,
/// exposing the resulting sample rate/channel count so the capture side of
/// the pipeline can be configured to match — avoiding a resampler for v1.
///
/// Still NOT handled: the capture (virtual process-loopback) and render
/// (real device) clients are on independent clocks with no explicit drift
/// compensation, so long sessions may accumulate small timing drift.
/// Acceptable for v1 per the plan; a real fix is a resampler or adaptive
/// buffer, not attempted here.
/// </summary>
public sealed class BoostRenderer
{
    private IAudioClient? _audioClient;
    private IAudioRenderClient? _renderClient;

    public int Channels { get; private set; }

    public int SampleRateHz { get; private set; }

    public unsafe void Start()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);

        var audioClientIid = typeof(IAudioClient).GUID;
        device.Activate(&audioClientIid, Windows.Win32.System.Com.CLSCTX.CLSCTX_ALL, null, out var audioClientObj);
        var audioClient = (IAudioClient)audioClientObj;

        audioClient.GetMixFormat(out var mixFormat);
        Channels = mixFormat->nChannels;
        SampleRateHz = (int)mixFormat->nSamplesPerSec;

        try
        {
            audioClient.Initialize(
                AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
                0,
                2_000_000, // 200ms buffer, in 100-ns units
                0,
                mixFormat,
                null);
        }
        finally
        {
            Marshal.FreeCoTaskMem((IntPtr)mixFormat);
        }

        var renderClientIid = typeof(IAudioRenderClient).GUID;
        audioClient.GetService(&renderClientIid, out var renderClientObj);

        _audioClient = audioClient;
        _renderClient = (IAudioRenderClient)renderClientObj;
        _audioClient.Start();
    }

    /// <summary>Writes interleaved float samples (in the format described by <see cref="Channels"/>/<see cref="SampleRateHz"/>), padding/truncating to fit currently-available buffer space so the render clock is never starved or overrun.</summary>
    public unsafe void Write(ReadOnlySpan<float> samples)
    {
        if (_audioClient is null || _renderClient is null)
        {
            throw new InvalidOperationException($"{nameof(BoostRenderer)} has not been started.");
        }

        _audioClient.GetBufferSize(out var bufferFrames);
        _audioClient.GetCurrentPadding(out var paddingFrames);
        var availableFrames = bufferFrames - paddingFrames;

        var framesToWrite = (uint)Math.Min(availableFrames, samples.Length / Channels);
        if (framesToWrite == 0)
        {
            return;
        }

        byte* dataPtr;
        _renderClient.GetBuffer(framesToWrite, &dataPtr);

        var destination = new Span<float>(dataPtr, (int)(framesToWrite * Channels));
        samples[..destination.Length].CopyTo(destination);

        _renderClient.ReleaseBuffer(framesToWrite, 0);
    }

    public void Stop()
    {
        _audioClient?.Stop();
        _audioClient = null;
        _renderClient = null;
    }
}
