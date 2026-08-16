using BLight.Blare.Core.Safety;

namespace BLight.Blare.App.Services;

/// <summary>
/// Ties LoudnessTracker + ConsentState to a live sampling loop (see plan's
/// Health/safety tracking section). Phase 1 signal is user-set volume vs a
/// threshold — not peak-meter content loudness — because that distinguishes
/// "quiet app turned up" (not dangerous) from "inherently loud content"
/// better than a raw peak reading would.
/// </summary>
public sealed class SafetyMonitor
{
    private readonly LoudnessTracker _tracker;
    private readonly ConsentState _consent;
    private readonly TimeSpan _warnAfter;
    private readonly double _thresholdPercent;

    public SafetyMonitor(LoudnessTracker tracker, ConsentState consent, TimeSpan? warnAfter = null, double thresholdPercent = 90)
    {
        _tracker = tracker;
        _consent = consent;
        _warnAfter = warnAfter ?? TimeSpan.FromMinutes(20);
        _thresholdPercent = thresholdPercent;
    }

    /// <summary>Feeds one sampling tick and returns app keys currently past the warn-after duration, empty if warnings are opted out (and that opt-out hasn't expired).</summary>
    public IReadOnlyList<string> Sample(IEnumerable<(string AppKey, double VolumePercent)> sessions, DateTimeOffset now)
    {
        var warned = new List<string>();

        foreach (var (appKey, volumePercent) in sessions)
        {
            if (string.IsNullOrEmpty(appKey))
            {
                continue;
            }

            var aboveThreshold = volumePercent >= _thresholdPercent;
            var window = _tracker.RecordSample(new LoudnessSample(now, appKey, (float)volumePercent, aboveThreshold));

            if (window.TimeAboveThreshold >= _warnAfter)
            {
                warned.Add(appKey);
            }
        }

        return _consent.IsActive(ConsentKind.SafetyWarningsDisabled, now) ? [] : warned;
    }

    public bool WarningsDisabled(DateTimeOffset now) => _consent.IsActive(ConsentKind.SafetyWarningsDisabled, now);

    public void DisableWarnings(DateTimeOffset now) => _consent.Grant(ConsentKind.SafetyWarningsDisabled, now);

    public void ReenableWarnings() => _consent.Revoke(ConsentKind.SafetyWarningsDisabled);
}
