using Blight.Blare.Core.Safety;

namespace Blight.Blare.App.Services;

/// <summary>
/// Decides when listening counts as "loud" and for how long.
///
/// Deliberately does NOT key off an app's volume slider. Windows sets every
/// session to 100% by default, so treating a high slider as loud flags every
/// untouched app on the system — 100% means "not attenuated", not "loud".
///
/// The honest signal is how much sound is actually leaving the machine, so
/// this uses the measured peak level of each app's stream scaled by the output
/// device's volume. A hot stream through a device at 20% isn't loud; a hot
/// stream through a device at 100% is. Silence never counts, whatever the
/// sliders say.
///
/// Still relative, not absolute: Windows cannot see your speaker or headphone
/// gain, so this can't be a sound pressure measurement and is never presented
/// as one.
/// </summary>
public sealed class SafetyMonitor
{
    /// <summary>Below this the stream is effectively silent and can't be loud regardless of volume.</summary>
    private const double SilenceFloor = 0.02;

    private readonly LoudnessTracker _tracker;
    private readonly ConsentState _consent;
    private readonly TimeSpan _warnAfter;
    private readonly double _loudLevel;
    private readonly HashSet<string> _currentlyWarned = new();

    /// <param name="loudLevel">
    /// Effective output level counted as loud, 0..1. The default of 0.5 is
    /// roughly -6 dBFS — genuinely energetic content at a high device volume,
    /// not merely an unattenuated app.
    /// </param>
    public SafetyMonitor(LoudnessTracker tracker, ConsentState consent, TimeSpan? warnAfter = null, double loudLevel = 0.5)
    {
        _tracker = tracker;
        _consent = consent;
        _warnAfter = warnAfter ?? TimeSpan.FromMinutes(20);
        _loudLevel = loudLevel;
    }

    public double LoudLevel => _loudLevel;

    public TimeSpan WarnAfter => _warnAfter;

    /// <summary>How many times an app has newly crossed into a warned state this run — a transition count, not a per-sample tally, so an ongoing warning doesn't keep inflating it.</summary>
    public int WarningCount { get; private set; }

    /// <summary>Total time any app has spent loud, summed across apps. A rough exposure figure for the UI, not a clinical dose measurement.</summary>
    public TimeSpan TotalTimeAboveThreshold =>
        _tracker.Windows.Values.Aggregate(TimeSpan.Zero, (total, window) => total + window.TimeAboveThreshold);

    /// <summary>Effective output level for a stream — what actually reaches the output, before the hardware gain Windows can't see.</summary>
    public static double EffectiveLevel(double peakLevel, double masterVolume) =>
        Math.Clamp(peakLevel, 0, 1) * Math.Clamp(masterVolume, 0, 1);

    public bool IsLoud(double peakLevel, double masterVolume) =>
        peakLevel > SilenceFloor && EffectiveLevel(peakLevel, masterVolume) >= _loudLevel;

    /// <summary>
    /// Feeds one sampling tick and returns app keys that have now been loud for
    /// longer than the warn-after duration. Empty while warnings are opted out
    /// (and that opt-out hasn't expired).
    /// </summary>
    public IReadOnlyList<string> Sample(
        IEnumerable<(string AppKey, double PeakLevel)> sessions,
        double masterVolume,
        DateTimeOffset now)
    {
        var warned = new List<string>();

        foreach (var (appKey, peakLevel) in sessions)
        {
            if (string.IsNullOrEmpty(appKey))
            {
                continue;
            }

            var loud = IsLoud(peakLevel, masterVolume);
            var window = _tracker.RecordSample(new LoudnessSample(now, appKey, (float)peakLevel, loud));

            if (window.TimeAboveThreshold >= _warnAfter)
            {
                warned.Add(appKey);

                if (_currentlyWarned.Add(appKey))
                {
                    WarningCount++;
                }
            }
            else
            {
                _currentlyWarned.Remove(appKey);
            }
        }

        return _consent.IsActive(ConsentKind.SafetyWarningsDisabled, now) ? [] : warned;
    }

    public bool WarningsDisabled(DateTimeOffset now) => _consent.IsActive(ConsentKind.SafetyWarningsDisabled, now);

    public void DisableWarnings(DateTimeOffset now) => _consent.Grant(ConsentKind.SafetyWarningsDisabled, now);

    public void ReenableWarnings() => _consent.Revoke(ConsentKind.SafetyWarningsDisabled);
}
