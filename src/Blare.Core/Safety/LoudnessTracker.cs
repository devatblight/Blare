namespace Blight.Blare.Core.Safety;

public sealed record LoudnessSample(
    DateTimeOffset Timestamp,
    string AppKey,
    float PeakLevel,
    bool AboveThreshold);

public sealed record LoudnessWindow(
    string AppKey,
    DateTimeOffset WindowStart,
    TimeSpan TotalDuration,
    TimeSpan TimeAboveThreshold)
{
    public double FractionAboveThreshold =>
        TotalDuration == TimeSpan.Zero ? 0.0 : TimeAboveThreshold / TotalDuration;
}

/// <summary>
/// Accumulates "time spent above threshold" per app from a stream of
/// low-frequency samples (~1-5s apart in real use). Each sample attributes
/// the elapsed time since the previous sample for that app to either bucket
/// based on the new sample's AboveThreshold flag — a standard
/// hold-last-value accumulator, not a claim of continuous measurement.
///
/// Takes timestamps as input rather than reading the clock itself, so it's
/// fully deterministic and testable without waiting on real time.
/// </summary>
public sealed class LoudnessTracker
{
    private readonly Dictionary<string, DateTimeOffset> _lastSampleTimestamps = new();
    private readonly Dictionary<string, LoudnessWindow> _windows = new();

    public IReadOnlyDictionary<string, LoudnessWindow> Windows => _windows;

    public LoudnessWindow RecordSample(LoudnessSample sample)
    {
        if (!_lastSampleTimestamps.TryGetValue(sample.AppKey, out var lastTimestamp))
        {
            var freshWindow = new LoudnessWindow(sample.AppKey, sample.Timestamp, TimeSpan.Zero, TimeSpan.Zero);
            _windows[sample.AppKey] = freshWindow;
            _lastSampleTimestamps[sample.AppKey] = sample.Timestamp;
            return freshWindow;
        }

        var elapsed = sample.Timestamp - lastTimestamp;
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Sample timestamp is earlier than the last recorded sample for this app.",
                nameof(sample));
        }

        var existing = _windows[sample.AppKey];
        var updated = existing with
        {
            TotalDuration = existing.TotalDuration + elapsed,
            TimeAboveThreshold = existing.TimeAboveThreshold + (sample.AboveThreshold ? elapsed : TimeSpan.Zero),
        };

        _windows[sample.AppKey] = updated;
        _lastSampleTimestamps[sample.AppKey] = sample.Timestamp;
        return updated;
    }

    /// <summary>Starts a fresh rolling window for an app (e.g. called once every 24h by the host).</summary>
    public void ResetWindow(string appKey, DateTimeOffset windowStart)
    {
        _windows[appKey] = new LoudnessWindow(appKey, windowStart, TimeSpan.Zero, TimeSpan.Zero);
        _lastSampleTimestamps[appKey] = windowStart;
    }

    public LoudnessWindow? GetWindow(string appKey) =>
        _windows.TryGetValue(appKey, out var window) ? window : null;
}
