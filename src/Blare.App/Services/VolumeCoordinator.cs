using Blight.Blare.Audio.Sessions;

namespace Blight.Blare.App.Services;

/// <summary>
/// Applies per-app volume. Nothing more.
///
/// Blare previously tried to amplify above 100% by capturing an app's audio,
/// gaining it up and rendering it back out. That was removed after it produced
/// a loud burst of noise on a user's speakers: the app's own process ended up
/// on the desk, so it captured its own boosted output and re-rendered it in a
/// feedback loop. A limiter caps amplitude but cannot help here — a feedback
/// loop pinned at the ceiling is exactly the screech that reached the speakers.
///
/// The lesson is not "add another guard". It is that an app whose purpose is
/// protecting hearing has no business rendering audio it synthesised, so it
/// no longer does. Volume only goes down from unity, through the same Windows
/// APIs the built-in mixer uses, and Blare never becomes an audio source.
/// </summary>
public sealed class VolumeCoordinator
{
    /// <summary>Windows' own ceiling for a session. There is no path above this.</summary>
    public const double MaximumPercent = 100;

    private readonly AudioSessionManager _sessionManager;

    public VolumeCoordinator(AudioSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public void SetVolumePercent(uint processId, double volumePercent) =>
        _sessionManager.SetVolume(processId, (float)(Math.Clamp(volumePercent, 0, MaximumPercent) / 100.0));

    public void SetMute(uint processId, bool isMuted) => _sessionManager.SetMute(processId, isMuted);

    /// <summary>
    /// Slides a session to a level instead of jumping to it.
    ///
    /// For discrete changes only — a hotkey, a scene recall, "everything to
    /// 100%". Dragging a fader is already continuous and ramping it would fight
    /// the pointer. The reason to ramp at all is that a sudden jump upward is
    /// both an audible click and a small startle, which is the exact thing this
    /// app exists to avoid.
    /// </summary>
    public async Task RampToAsync(uint processId, double targetPercent, CancellationToken cancellationToken = default)
    {
        const int steps = 8;
        var stepDelay = TimeSpan.FromMilliseconds(RampDuration.TotalMilliseconds / steps);

        var target = Math.Clamp(targetPercent, 0, MaximumPercent);
        var start = _sessionManager.GetVolume(processId) * 100;

        if (double.IsNaN(start) || Math.Abs(target - start) < 1)
        {
            SetVolumePercent(processId, target);
            return;
        }

        for (var step = 1; step <= steps; step++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Ease out, so it arrives gently rather than stopping dead.
            var progress = step / (double)steps;
            var eased = 1 - Math.Pow(1 - progress, 3);

            SetVolumePercent(processId, start + ((target - start) * eased));

            if (step < steps)
            {
                await Task.Delay(stepDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        SetVolumePercent(processId, target);
    }

    /// <summary>Short enough to feel immediate, long enough to remove the click.</summary>
    public static readonly TimeSpan RampDuration = TimeSpan.FromMilliseconds(160);
}
