using Blight.Blare.Audio.Sessions;

namespace Blight.Blare.Audio.Boost;

/// <summary>
/// Phase 2 boost: capture → gain → limiter → re-render.
///
/// The original stream is <em>attenuated</em>, not muted. Muting looks like the
/// obvious way to stop hearing the original alongside the boosted copy, but
/// per-process loopback capture is applied after session volume and mute, so a
/// muted app captures pure silence and boost amplifies nothing. Measured:
/// captured peak 0.000000 muted, 0.567 unmuted.
///
/// Attenuation works because capture scales linearly with volume. Holding the
/// original at <see cref="ResidualLevel"/> leaves a signal that is still fully
/// present in the capture, and multiplying by the reciprocal reconstructs it.
/// Float32 has ample headroom for that, so nothing is lost numerically. What
/// the user hears directly from the original is about -34 dB relative to the
/// boosted render — far enough down to be inaudible under it.
/// </summary>
public sealed class BoostEngine
{
    /// <summary>Where the original session is held while boosting: quiet enough to be inaudible, loud enough to capture cleanly.</summary>
    public const float ResidualLevel = 0.02f;

    /// <summary>Consecutive heavily-clamped blocks tolerated before boost gives up. A few is a loud transient; a run of them is a broken pipeline.</summary>
    private const int RunawayBlockLimit = 10;

    private readonly AudioSessionManager _sessionManager;
    private readonly ProcessLoopbackCapture _capture = new();

    private CancellationTokenSource? _cts;
    private Task? _pipelineTask;
    private uint _boostedProcessId;
    private float _volumeBeforeBoost = 1f;

    public BoostEngine(AudioSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public bool IsRunning => _pipelineTask is { IsCompleted: false };

    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>The boost the user asked for, 1.0 being unity. Read every block so a fader can move it live.</summary>
    public float GainLinear { get; set; } = 1f;

    /// <summary>Raised when the pipeline stops on its own — a failure, or the app going away.</summary>
    public event EventHandler<string>? Stopped;

    public void Start(uint processId, float gainLinear, float currentVolume)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Boost is already running — stop it before starting a new target.");
        }

        _boostedProcessId = processId;
        _volumeBeforeBoost = Math.Clamp(currentVolume, 0f, 1f);
        GainLinear = BoostSafety.SanitizeGain(gainLinear);
        StartedAt = DateTimeOffset.UtcNow;

        _sessionManager.SetMute(processId, false);
        _sessionManager.SetVolume(processId, ResidualLevel);

        // Verify the attenuation actually landed. If the original is still at
        // full volume, boosting on top of it would stack the direct output and
        // the amplified copy — the exact scenario that must never reach anyone.
        var applied = _sessionManager.GetVolume(processId);
        if (applied > ResidualLevel * 4)
        {
            _sessionManager.SetVolume(processId, _volumeBeforeBoost);
            StartedAt = null;
            throw new InvalidOperationException(
                $"Could not attenuate the original stream (still at {applied:P0}); boost refused.");
        }

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
            // Put the app back where the user had it — leaving it at the
            // residual level would look like Blare had broken its audio.
            _sessionManager.SetVolume(_boostedProcessId, _volumeBeforeBoost);
            _cts.Dispose();
            _cts = null;
            _pipelineTask = null;
            StartedAt = null;
        }
    }

    private async Task RunPipelineAsync(uint processId, CancellationToken cancellationToken)
    {
        var renderer = new BoostRenderer();
        var limiter = new Limiter();
        string? failure = null;
        var runawayBlocks = 0;

        try
        {
            renderer.Start();

            await _capture.RunAsync(
                processId,
                block =>
                {
                    var mutable = block.ToArray().AsSpan();

                    // Undo the residual attenuation, then apply the user's boost.
                    // Sanitised rather than trusted: a bad gain would turn every
                    // sample that follows into NaN.
                    GainProcessor.ApplyGain(mutable, BoostSafety.SanitizeGain(GainLinear / ResidualLevel));
                    limiter.Process(mutable);

                    // Nothing reaches the device without passing this.
                    var corrections = BoostSafety.Enforce(mutable);

                    if (BoostSafety.IsRunaway(corrections, mutable.Length))
                    {
                        // Most of a block needed clamping, so something upstream
                        // is broken. Stop rather than keep feeding the speakers.
                        runawayBlocks++;

                        if (runawayBlocks >= RunawayBlockLimit)
                        {
                            throw new InvalidOperationException(
                                "Boost output was clamped continuously — stopped to protect your hearing.");
                        }
                    }
                    else
                    {
                        runawayBlocks = 0;
                    }

                    renderer.Write(mutable);
                },
                cancellationToken,
                renderer.SampleRateHz,
                renderer.Channels);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }
        finally
        {
            renderer.Stop();
        }

        if (failure is not null)
        {
            // Restore immediately rather than leaving the app stuck near-silent.
            _sessionManager.SetVolume(processId, _volumeBeforeBoost);
            Stopped?.Invoke(this, failure);
        }
    }
}
