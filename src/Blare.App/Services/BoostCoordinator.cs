using BLight.Blare.Audio.Boost;
using BLight.Blare.Audio.Sessions;
using BLight.Blare.Core.Safety;

namespace BLight.Blare.App.Services;

/// <summary>
/// Owns one <see cref="BoostEngine"/> per boosted process and translates a
/// single volume-percent value (0-300, where >100 means boosted) into
/// either a plain Phase 1 volume set or a Phase 2 boost start/adjust/stop —
/// so the UI only ever deals with "set this app to X%" and doesn't need to
/// know which mechanism is behind it.
/// </summary>
public sealed class BoostCoordinator
{
    /// <summary>
    /// Whether above-unity boost can work at all on this build. False because
    /// per-process loopback capture is applied after session volume and mute,
    /// so the original can't be silenced and re-amplified — see
    /// <see cref="SetVolumePercentAsync"/>. Surfaced in the UI so the boost
    /// controls state the truth rather than offering a setting that does nothing.
    /// </summary>
    public const bool BoostAvailable = false;

    // Held at unity while the boost pipeline is out of action so faders can't
    // be dragged into a range that does nothing.
    public const double SafeCeilingPercent = 100;
    public const double OverriddenCeilingPercent = 100;

    private readonly AudioSessionManager _sessionManager;
    private readonly ConsentState _consent;
    private readonly Dictionary<uint, BoostEngine> _engines = new();

    public BoostCoordinator(AudioSessionManager sessionManager, ConsentState consent)
    {
        _sessionManager = sessionManager;
        _consent = consent;
    }

    public bool IsBoosted(uint processId) => _engines.TryGetValue(processId, out var engine) && engine.IsRunning;

    public bool AnyBoosted => _engines.Values.Any(e => e.IsRunning);

    public int BoostedCount => _engines.Values.Count(e => e.IsRunning);

    public double CurrentCeilingPercent(DateTimeOffset now) =>
        _consent.IsActive(ConsentKind.SafeBoostCeilingOverride, now) ? OverriddenCeilingPercent : SafeCeilingPercent;

    public void GrantCeilingOverride(DateTimeOffset now) => _consent.Grant(ConsentKind.SafeBoostCeilingOverride, now);

    /// <summary>Drops back to the safe ceiling. Unlike granting, this needs no confirmation — moving toward safety is never gated.</summary>
    public void RevokeCeilingOverride() => _consent.Revoke(ConsentKind.SafeBoostCeilingOverride);

    public async Task SetVolumePercentAsync(uint processId, double volumePercent)
    {
        // The boost pipeline is disabled: BoostEngine silences the original
        // session so the boosted re-render can replace it, but measurement
        // showed per-process loopback capture is applied AFTER session
        // volume/mute — so silencing the original also silences what we
        // capture, and boost renders pure silence. Engaging it here would
        // kill the app's audio outright, which is worse than not boosting.
        // Volume is clamped to unity until boost has a working mechanism.
        if (_engines.TryGetValue(processId, out var runningEngine) && runningEngine.IsRunning)
        {
            await runningEngine.StopAsync();
        }

        var clamped = Math.Clamp(volumePercent, 0, 100);
        _sessionManager.SetVolume(processId, (float)(clamped / 100.0));
    }

    public async Task StopAllAsync()
    {
        foreach (var engine in _engines.Values.Where(e => e.IsRunning))
        {
            await engine.StopAsync();
        }
    }
}
