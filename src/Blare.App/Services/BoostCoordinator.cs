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
    public const double SafeCeilingPercent = 150;
    public const double OverriddenCeilingPercent = 300;

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
        if (volumePercent > 100)
        {
            var gain = (float)(volumePercent / 100.0);

            if (!_engines.TryGetValue(processId, out var engine))
            {
                engine = new BoostEngine(_sessionManager);
                _engines[processId] = engine;
            }

            if (engine.IsRunning)
            {
                engine.GainLinear = gain;
            }
            else
            {
                engine.Start(processId, gain);
            }

            return;
        }

        if (_engines.TryGetValue(processId, out var runningEngine) && runningEngine.IsRunning)
        {
            await runningEngine.StopAsync();
        }

        _sessionManager.SetVolume(processId, (float)(volumePercent / 100.0));
    }

    public async Task StopAllAsync()
    {
        foreach (var engine in _engines.Values.Where(e => e.IsRunning))
        {
            await engine.StopAsync();
        }
    }
}
