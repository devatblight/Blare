using BLight.Blare.Audio.Sessions;

namespace BLight.Blare.Audio.Boost;

/// <summary>
/// Ties capture → gain → limiter → re-render into the actual Phase 2 boost
/// pipeline (see plan §3), and coordinates the mute-original step: the
/// target app's own session volume is driven to 0 via
/// <see cref="AudioSessionManager"/> while boost is active, since the
/// process-loopback capture is a *copy* of what the app renders — without
/// muting the original, the user would hear both it and the boosted
/// re-render simultaneously.
///
/// Known simplification for v1 (see plan's flagged Phase 2 risks): no
/// watchdog re-verifying the original stays muted, and no explicit
/// clock-drift compensation between the independent capture/render
/// clocks — acceptable for a first working pipeline, not for shipping.
/// </summary>
public sealed class BoostEngine
{
    private readonly AudioSessionManager _sessionManager;
    private readonly ProcessLoopbackCapture _capture = new();

    private CancellationTokenSource? _cts;
    private Task? _pipelineTask;
    private uint _boostedProcessId;

    public BoostEngine(AudioSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public bool IsRunning => _pipelineTask is { IsCompleted: false };

    /// <summary>Read by the pipeline loop on every block, so a caller (e.g. dragging a slider) can adjust gain live without tearing down and restarting the whole capture/render pipeline.</summary>
    public float GainLinear { get; set; } = 1f;

    public void Start(uint processId, float gainLinear)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Boost is already running — stop it before starting a new target.");
        }

        _boostedProcessId = processId;
        GainLinear = gainLinear;
        _sessionManager.SetMute(processId, true);

        _cts = new CancellationTokenSource();
        _pipelineTask = RunPipelineAsync(processId, _cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();

        try
        {
            if (_pipelineTask is not null)
            {
                await _pipelineTask;
            }
        }
        catch (OperationCanceledException)
        {
            // expected on cancellation
        }
        finally
        {
            _sessionManager.SetMute(_boostedProcessId, false);
            _cts.Dispose();
            _cts = null;
            _pipelineTask = null;
        }
    }

    private async Task RunPipelineAsync(uint processId, CancellationToken cancellationToken)
    {
        var renderer = new BoostRenderer();
        var limiter = new Limiter();
        renderer.Start();

        try
        {
            await _capture.RunAsync(
                processId,
                block =>
                {
                    var mutable = block.ToArray().AsSpan();
                    GainProcessor.ApplyGain(mutable, GainLinear);
                    limiter.Process(mutable);
                    renderer.Write(mutable);
                },
                cancellationToken,
                renderer.SampleRateHz,
                renderer.Channels);
        }
        finally
        {
            renderer.Stop();
        }
    }
}
