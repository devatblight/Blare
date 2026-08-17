using Blight.Blare.Audio.Sessions;

namespace Blare.Audio.Tests.Sessions;

public class SessionGroupTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2.5);

    [Fact]
    public void NewSession_ProducesOneRow()
    {
        var tracker = new SessionGroupTracker(Debounce);

        var rows = tracker.Reconcile(
            [new SessionSnapshot("s1", Guid.Empty, ProcessId: 100)],
            Start);

        Assert.Single(rows);
        Assert.Equal(100u, (uint)rows[0].ProcessId);
        Assert.False(rows[0].PendingRemoval);
    }

    [Fact]
    public void SessionsSharingGroupingParam_CollapseIntoOneRow()
    {
        var tracker = new SessionGroupTracker(Debounce);
        var grouping = Guid.NewGuid();

        var rows = tracker.Reconcile(
            [
                new SessionSnapshot("tab-1", grouping, ProcessId: 200),
                new SessionSnapshot("tab-2", grouping, ProcessId: 200),
            ],
            Start);

        Assert.Single(rows);
        Assert.Equal(2, rows[0].SessionKeys.Count);
        Assert.Contains("tab-1", rows[0].SessionKeys);
        Assert.Contains("tab-2", rows[0].SessionKeys);
    }

    [Fact]
    public void UngroupedSessions_StayAsSeparateRows()
    {
        var tracker = new SessionGroupTracker(Debounce);

        var rows = tracker.Reconcile(
            [
                new SessionSnapshot("s1", Guid.Empty, ProcessId: 100),
                new SessionSnapshot("s2", Guid.Empty, ProcessId: 200),
            ],
            Start);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void DisappearedSession_StaysVisiblePendingRemovalWithinDebounceWindow()
    {
        var tracker = new SessionGroupTracker(Debounce);
        tracker.Reconcile([new SessionSnapshot("s1", Guid.Empty, 100)], Start);

        var rows = tracker.Reconcile([], Start + TimeSpan.FromSeconds(1));

        Assert.Single(rows);
        Assert.True(rows[0].PendingRemoval);
    }

    [Fact]
    public void DisappearedSession_IsRemovedAfterDebounceWindowElapses()
    {
        var tracker = new SessionGroupTracker(Debounce);
        tracker.Reconcile([new SessionSnapshot("s1", Guid.Empty, 100)], Start);

        tracker.Reconcile([], Start + TimeSpan.FromSeconds(1));
        var rows = tracker.Reconcile([], Start + TimeSpan.FromSeconds(1) + Debounce + TimeSpan.FromSeconds(1));

        Assert.Empty(rows);
    }

    [Fact]
    public void SessionThatReappearsWithinDebounceWindow_CancelsPendingRemoval()
    {
        var tracker = new SessionGroupTracker(Debounce);
        tracker.Reconcile([new SessionSnapshot("s1", Guid.Empty, 100)], Start);

        tracker.Reconcile([], Start + TimeSpan.FromSeconds(1));
        var rows = tracker.Reconcile([new SessionSnapshot("s1", Guid.Empty, 100)], Start + TimeSpan.FromSeconds(2));

        Assert.Single(rows);
        Assert.False(rows[0].PendingRemoval);

        // and it should survive well past the original debounce window since removal was cancelled
        var laterRows = tracker.Reconcile(
            [new SessionSnapshot("s1", Guid.Empty, 100)],
            Start + TimeSpan.FromSeconds(10));
        Assert.Single(laterRows);
        Assert.False(laterRows[0].PendingRemoval);
    }

    [Fact]
    public void SameGroupingParam_DifferentProcessId_AreSeparateRows()
    {
        var tracker = new SessionGroupTracker(Debounce);
        var grouping = Guid.NewGuid();

        var rows = tracker.Reconcile(
            [
                new SessionSnapshot("s1", grouping, ProcessId: 100),
                new SessionSnapshot("s2", grouping, ProcessId: 200),
            ],
            Start);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void ComputeGroupKey_IsStableForSameInputs()
    {
        var grouping = Guid.NewGuid();

        var key1 = SessionGroupTracker.ComputeGroupKey(grouping, "s1", 100);
        var key2 = SessionGroupTracker.ComputeGroupKey(grouping, "s1", 100);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void ComputeGroupKey_EmptyGrouping_FallsBackToSessionKey()
    {
        var key1 = SessionGroupTracker.ComputeGroupKey(Guid.Empty, "s1", 100);
        var key2 = SessionGroupTracker.ComputeGroupKey(Guid.Empty, "s2", 100);

        Assert.NotEqual(key1, key2);
    }
}
