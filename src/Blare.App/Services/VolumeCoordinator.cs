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
}
