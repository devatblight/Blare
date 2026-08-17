using Blight.Blare.Core.Safety;

namespace Blare.Core.Tests.Safety;

public class LoudnessTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstSample_StartsWindowWithZeroDuration()
    {
        var tracker = new LoudnessTracker();

        var window = tracker.RecordSample(new LoudnessSample(Start, "app.exe", PeakLevel: 0.5f, AboveThreshold: true));

        Assert.Equal(TimeSpan.Zero, window.TotalDuration);
        Assert.Equal(TimeSpan.Zero, window.TimeAboveThreshold);
        Assert.Equal(Start, window.WindowStart);
    }

    [Fact]
    public void SubsequentSample_AttributesElapsedTimeToAboveThresholdWhenFlagged()
    {
        var tracker = new LoudnessTracker();
        tracker.RecordSample(new LoudnessSample(Start, "app.exe", 0.5f, AboveThreshold: true));

        var window = tracker.RecordSample(
            new LoudnessSample(Start + TimeSpan.FromSeconds(5), "app.exe", 0.9f, AboveThreshold: true));

        Assert.Equal(TimeSpan.FromSeconds(5), window.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), window.TimeAboveThreshold);
    }

    [Fact]
    public void SubsequentSample_DoesNotCountElapsedTimeWhenBelowThreshold()
    {
        var tracker = new LoudnessTracker();
        tracker.RecordSample(new LoudnessSample(Start, "app.exe", 0.5f, AboveThreshold: true));

        var window = tracker.RecordSample(
            new LoudnessSample(Start + TimeSpan.FromSeconds(5), "app.exe", 0.1f, AboveThreshold: false));

        Assert.Equal(TimeSpan.FromSeconds(5), window.TotalDuration);
        Assert.Equal(TimeSpan.Zero, window.TimeAboveThreshold);
    }

    [Fact]
    public void MixedSamples_AccumulateFractionAboveThresholdCorrectly()
    {
        // Each elapsed gap is attributed to the flag on the sample that ends it
        // (hold-value-backward): 0-10s -> above (10s), 10-20s -> below (10s), 20-30s -> above (10s).
        var tracker = new LoudnessTracker();
        tracker.RecordSample(new LoudnessSample(Start, "app.exe", 0.5f, AboveThreshold: false));
        tracker.RecordSample(new LoudnessSample(Start + TimeSpan.FromSeconds(10), "app.exe", 0.9f, AboveThreshold: true));
        tracker.RecordSample(new LoudnessSample(Start + TimeSpan.FromSeconds(20), "app.exe", 0.2f, AboveThreshold: false));
        var window = tracker.RecordSample(
            new LoudnessSample(Start + TimeSpan.FromSeconds(30), "app.exe", 0.9f, AboveThreshold: true));

        Assert.Equal(TimeSpan.FromSeconds(30), window.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(20), window.TimeAboveThreshold);
        Assert.Equal(20.0 / 30.0, window.FractionAboveThreshold, precision: 6);
    }

    [Fact]
    public void DifferentApps_TrackIndependentWindows()
    {
        var tracker = new LoudnessTracker();
        tracker.RecordSample(new LoudnessSample(Start, "a.exe", 0.5f, true));
        tracker.RecordSample(new LoudnessSample(Start, "b.exe", 0.5f, false));

        tracker.RecordSample(new LoudnessSample(Start + TimeSpan.FromSeconds(10), "a.exe", 0.9f, true));
        tracker.RecordSample(new LoudnessSample(Start + TimeSpan.FromSeconds(10), "b.exe", 0.9f, false));

        Assert.Equal(TimeSpan.FromSeconds(10), tracker.GetWindow("a.exe")!.TimeAboveThreshold);
        Assert.Equal(TimeSpan.Zero, tracker.GetWindow("b.exe")!.TimeAboveThreshold);
    }

    [Fact]
    public void OutOfOrderTimestamp_Throws()
    {
        var tracker = new LoudnessTracker();
        tracker.RecordSample(new LoudnessSample(Start, "app.exe", 0.5f, true));

        Assert.Throws<ArgumentException>(() =>
            tracker.RecordSample(new LoudnessSample(Start - TimeSpan.FromSeconds(1), "app.exe", 0.5f, true)));
    }

    [Fact]
    public void ResetWindow_ClearsAccumulatedDurationAndRestartsFromZero()
    {
        var tracker = new LoudnessTracker();
        tracker.RecordSample(new LoudnessSample(Start, "app.exe", 0.5f, true));
        tracker.RecordSample(new LoudnessSample(Start + TimeSpan.FromSeconds(10), "app.exe", 0.9f, true));

        var resetAt = Start + TimeSpan.FromDays(1);
        tracker.ResetWindow("app.exe", resetAt);
        var freshWindow = tracker.GetWindow("app.exe");

        Assert.Equal(TimeSpan.Zero, freshWindow!.TotalDuration);
        Assert.Equal(resetAt, freshWindow.WindowStart);

        // next sample after reset starts elapsed-time counting fresh, not from the pre-reset timestamp
        var window = tracker.RecordSample(
            new LoudnessSample(resetAt + TimeSpan.FromSeconds(5), "app.exe", 0.9f, true));
        Assert.Equal(TimeSpan.FromSeconds(5), window.TotalDuration);
    }
}
