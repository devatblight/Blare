using Blight.Blare.Core.Safety;

namespace Blare.Core.Tests.Safety;

public class ExposureTimelineTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LoudSamplesLandInTheHourTheyHappened()
    {
        var timeline = new ExposureTimeline();
        timeline.Record(Noon.AddMinutes(5), loud: true, TimeSpan.FromMinutes(1));
        timeline.Record(Noon.AddMinutes(50), loud: true, TimeSpan.FromMinutes(1));

        var bucket = Assert.Single(timeline.Buckets);

        Assert.Equal(Noon, bucket.Hour);
        Assert.Equal(TimeSpan.FromMinutes(2), bucket.TimeLoud);
    }

    [Fact]
    public void QuietSamplesRecordNothing()
    {
        var timeline = new ExposureTimeline();
        timeline.Record(Noon, loud: false, TimeSpan.FromMinutes(30));

        Assert.Empty(timeline.Buckets);
        Assert.Equal(TimeSpan.Zero, timeline.TotalInWindow);
    }

    [Fact]
    public void SeparateHoursStaySeparate()
    {
        // The whole point: two hours in one sitting reads differently from two
        // spread across a day.
        var timeline = new ExposureTimeline();
        timeline.Record(Noon, loud: true, TimeSpan.FromMinutes(30));
        timeline.Record(Noon.AddHours(3), loud: true, TimeSpan.FromMinutes(30));

        Assert.Equal(2, timeline.Buckets.Count);
        Assert.Equal(TimeSpan.FromHours(1), timeline.TotalInWindow);
    }

    [Fact]
    public void BucketsComeBackOldestFirst()
    {
        var timeline = new ExposureTimeline();
        timeline.Record(Noon.AddHours(2), loud: true, TimeSpan.FromMinutes(5));
        timeline.Record(Noon, loud: true, TimeSpan.FromMinutes(5));

        Assert.Equal(Noon, timeline.Buckets[0].Hour);
    }

    [Fact]
    public void AnythingOlderThanTheWindowIsDropped()
    {
        var timeline = new ExposureTimeline(TimeSpan.FromHours(6));
        timeline.Record(Noon, loud: true, TimeSpan.FromMinutes(20));

        timeline.Record(Noon.AddHours(10), loud: true, TimeSpan.FromMinutes(5));

        Assert.Single(timeline.Buckets);
        Assert.Equal(TimeSpan.FromMinutes(5), timeline.TotalInWindow);
    }

    [Fact]
    public void PruningHappensEvenOnAQuietSample()
    {
        // Otherwise a machine that goes quiet keeps yesterday's totals forever.
        var timeline = new ExposureTimeline(TimeSpan.FromHours(2));
        timeline.Record(Noon, loud: true, TimeSpan.FromMinutes(20));

        timeline.Record(Noon.AddHours(8), loud: false, TimeSpan.FromMinutes(1));

        Assert.Empty(timeline.Buckets);
    }

    [Fact]
    public void ConsecutiveHoursCountAsOneStretch()
    {
        var timeline = new ExposureTimeline();

        foreach (var hour in new[] { 0, 1, 2, 5 })
        {
            timeline.Record(Noon.AddHours(hour), loud: true, TimeSpan.FromMinutes(10));
        }

        Assert.Equal(3, timeline.LongestStretchHours());
    }

    [Fact]
    public void WithNothingRecordedThereIsNoStretchAndNoBusiestHour()
    {
        var timeline = new ExposureTimeline();

        Assert.Equal(0, timeline.LongestStretchHours());
        Assert.Null(timeline.Busiest());
    }

    [Fact]
    public void TheBusiestHourIsTheOneWithTheMostTime()
    {
        var timeline = new ExposureTimeline();
        timeline.Record(Noon, loud: true, TimeSpan.FromMinutes(10));
        timeline.Record(Noon.AddHours(1), loud: true, TimeSpan.FromMinutes(45));

        Assert.Equal(Noon.AddHours(1), timeline.Busiest()!.Value.Hour);
    }

    [Fact]
    public void ATimelineSurvivesARoundTrip()
    {
        var timeline = new ExposureTimeline();
        timeline.Record(Noon, loud: true, TimeSpan.FromMinutes(12));

        var restored = new ExposureTimeline();
        restored.Restore(timeline.Snapshot());

        Assert.Equal(TimeSpan.FromMinutes(12), restored.TotalInWindow);
    }
}

public class ProtectionAuditTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EntriesComeBackNewestFirst()
    {
        var audit = new ProtectionAudit();
        audit.Record(Noon, ProtectionEvent.WarningsDisabled);
        audit.Record(Noon.AddHours(1), ProtectionEvent.WarningsReenabled);

        Assert.Equal(ProtectionEvent.WarningsReenabled, audit.Entries[0].Event);
    }

    [Fact]
    public void TheLogIsCappedSoABackgroundTimerCannotLeak()
    {
        var audit = new ProtectionAudit();

        for (var i = 0; i < ProtectionAudit.MaximumEntries + 50; i++)
        {
            audit.Record(Noon.AddMinutes(i), ProtectionEvent.CapSet);
        }

        Assert.Equal(ProtectionAudit.MaximumEntries, audit.Entries.Count);
    }

    [Fact]
    public void TheCapDropsTheOldestRatherThanTheNewest()
    {
        var audit = new ProtectionAudit();

        for (var i = 0; i < ProtectionAudit.MaximumEntries; i++)
        {
            audit.Record(Noon.AddMinutes(i), ProtectionEvent.CapSet);
        }

        audit.Record(Noon.AddDays(1), ProtectionEvent.WarningsDisabled);

        Assert.Equal(ProtectionEvent.WarningsDisabled, audit.Entries[0].Event);
        Assert.Equal(ProtectionAudit.MaximumEntries, audit.Entries.Count);
    }

    [Fact]
    public void ItCanAnswerWhetherProtectionWasEverOff()
    {
        var audit = new ProtectionAudit();
        audit.Record(Noon, ProtectionEvent.WarningsDisabled);

        Assert.True(audit.WasEverDisabled(Noon.AddHours(-1)));
        Assert.False(audit.WasEverDisabled(Noon.AddHours(1)));
    }

    [Fact]
    public void OtherEventsDoNotCountAsProtectionBeingOff()
    {
        var audit = new ProtectionAudit();
        audit.Record(Noon, ProtectionEvent.QuietHoursChanged);

        Assert.False(audit.WasEverDisabled(Noon.AddHours(-1)));
    }

    [Fact]
    public void ARestoredLogIsOrderedAndCapped()
    {
        var audit = new ProtectionAudit();
        audit.Restore(
        [
            new ProtectionEntry(Noon, ProtectionEvent.CapSet, ""),
            new ProtectionEntry(Noon.AddHours(2), ProtectionEvent.WarningsDisabled, ""),
        ]);

        Assert.Equal(ProtectionEvent.WarningsDisabled, audit.Entries[0].Event);
    }
}
