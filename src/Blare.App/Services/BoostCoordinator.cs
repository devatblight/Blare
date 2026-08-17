using Blight.Blare.App.Views;
using Blight.Blare.Audio.Boost;
using Blight.Blare.Audio.Sessions;
using Blight.Blare.Core.Safety;

namespace Blight.Blare.App.Services;

/// <summary>
/// Owns one <see cref="BoostEngine"/> per boosted app and turns a single
/// volume-percent value into either a plain session volume (at or below 100%)
/// or a real boost pipeline (above it), so the UI only ever says "set this app
/// to X%".
///
/// Boost is time-limited on purpose. Amplified audio is exactly the situation
/// the health features exist for, and the risk comes from listening at that
/// level for a long stretch, so every boost expires on its own rather than
/// running until someone remembers to turn it off.
/// </summary>
public sealed class BoostCoordinator
{
    public const double SafeCeilingPercent = 150;
    public const double OverriddenCeilingPercent = 300;

    /// <summary>How long a boost runs before it lapses back to normal.</summary>
    public static readonly TimeSpan AutoDisableAfter = TimeSpan.FromMinutes(30);

    private readonly AudioSessionManager _sessionManager;
    private readonly ConsentState _consent;
    private readonly FlyoutService _flyout;
    private readonly Dictionary<uint, BoostEngine> _engines = new();
    private readonly Dictionary<uint, string> _namesByProcess = new();
    private readonly System.Timers.Timer _expiryTimer;

    public BoostCoordinator(AudioSessionManager sessionManager, ConsentState consent, FlyoutService flyout)
    {
        _sessionManager = sessionManager;
        _consent = consent;
        _flyout = flyout;

        _expiryTimer = new System.Timers.Timer(TimeSpan.FromSeconds(20).TotalMilliseconds) { AutoReset = true };
        _expiryTimer.Elapsed += (_, _) => _ = ExpireStaleBoostsAsync();
        _expiryTimer.Start();
    }

    /// <summary>Raised when a boost ends by itself, so the UI can put the fader back.</summary>
    public event EventHandler<uint>? BoostEnded;

    public bool IsBoosted(uint processId) => _engines.TryGetValue(processId, out var engine) && engine.IsRunning;

    public bool AnyBoosted => _engines.Values.Any(e => e.IsRunning);

    public int BoostedCount => _engines.Values.Count(e => e.IsRunning);

    /// <summary>How long until a boost lapses, or null when that app isn't boosted.</summary>
    public TimeSpan? TimeRemaining(uint processId) =>
        _engines.TryGetValue(processId, out var engine) && engine is { IsRunning: true, StartedAt: { } started }
            ? AutoDisableAfter - (DateTimeOffset.UtcNow - started)
            : null;

    public double CurrentCeilingPercent(DateTimeOffset now) =>
        _consent.IsActive(ConsentKind.SafeBoostCeilingOverride, now) ? OverriddenCeilingPercent : SafeCeilingPercent;

    public void GrantCeilingOverride(DateTimeOffset now) => _consent.Grant(ConsentKind.SafeBoostCeilingOverride, now);

    /// <summary>Drops back to the safe ceiling. Needs no confirmation — moving toward safety is never gated.</summary>
    public void RevokeCeilingOverride() => _consent.Revoke(ConsentKind.SafeBoostCeilingOverride);

    public void RememberName(uint processId, string displayName) => _namesByProcess[processId] = displayName;

    public async Task SetVolumePercentAsync(uint processId, double volumePercent, double currentVolumePercent)
    {
        if (volumePercent > 100)
        {
            var gain = (float)(volumePercent / 100.0);

            if (_engines.TryGetValue(processId, out var existing) && existing.IsRunning)
            {
                existing.GainLinear = gain;
                return;
            }

            var engine = new BoostEngine(_sessionManager);
            engine.Stopped += (_, reason) => OnEngineStopped(processId, reason);
            _engines[processId] = engine;

            // Start from the level the app was at, so releasing boost restores it.
            engine.Start(processId, gain, (float)(Math.Clamp(currentVolumePercent, 0, 100) / 100.0));

            _flyout.Show(
                $"{NameFor(processId)} boosted",
                $"Now at {volumePercent:F0}%. Boost turns itself off after {AutoDisableAfter.TotalMinutes:F0} minutes.",
                FlyoutTone.Caution,
                TimeSpan.FromSeconds(5));

            return;
        }

        if (_engines.TryGetValue(processId, out var running) && running.IsRunning)
        {
            await running.StopAsync();
            _engines.Remove(processId);
        }

        _sessionManager.SetVolume(processId, (float)(Math.Clamp(volumePercent, 0, 100) / 100.0));
    }

    public async Task StopAsync(uint processId)
    {
        if (_engines.TryGetValue(processId, out var engine) && engine.IsRunning)
        {
            await engine.StopAsync();
        }

        _engines.Remove(processId);
    }

    public async Task StopAllAsync()
    {
        foreach (var processId in _engines.Keys.ToList())
        {
            await StopAsync(processId);
        }
    }

    private async Task ExpireStaleBoostsAsync()
    {
        foreach (var (processId, engine) in _engines.ToList())
        {
            if (!engine.IsRunning || engine.StartedAt is not { } started)
            {
                continue;
            }

            if (DateTimeOffset.UtcNow - started < AutoDisableAfter)
            {
                continue;
            }

            await StopAsync(processId);

            _flyout.Show(
                "Boost turned off",
                $"{NameFor(processId)} had been boosted for {AutoDisableAfter.TotalMinutes:F0} minutes. Volume is back to normal.",
                FlyoutTone.Caution,
                TimeSpan.FromSeconds(8));

            BoostEnded?.Invoke(this, processId);
        }
    }

    private void OnEngineStopped(uint processId, string reason)
    {
        _engines.Remove(processId);

        _flyout.Show(
            "Boost stopped",
            $"{NameFor(processId)} could not keep boosting — {reason}",
            FlyoutTone.Danger,
            TimeSpan.FromSeconds(8));

        BoostEnded?.Invoke(this, processId);
    }

    private string NameFor(uint processId) =>
        _namesByProcess.TryGetValue(processId, out var name) ? name : $"pid {processId}";
}
