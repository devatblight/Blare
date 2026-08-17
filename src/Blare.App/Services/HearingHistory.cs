using Blight.Blare.Core.Safety;
using Blight.Blare.Core.Settings;

namespace Blight.Blare.App.Services;

/// <summary>
/// Keeps the exposure timeline and the protection audit, and persists both.
///
/// Together these are what makes Blare's claim about hearing more than a
/// slogan: one records when listening was loud, the other records when the
/// safeguards were off. Both stay on the machine.
///
/// Saved on a cadence rather than on every sample — the timeline is written to
/// several times a minute, and rewriting a file that often to record that
/// nothing happened is pure wear.
/// </summary>
public sealed class HearingHistory
{
    private const string TimelineKey = "exposure";
    private const string AuditKey = "protection-audit";

    private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(2);

    private sealed record SavedTimeline(Dictionary<DateTimeOffset, TimeSpan> Buckets);

    private readonly ISettingsStore _store;
    private DateTimeOffset _lastSaved = DateTimeOffset.MinValue;

    public HearingHistory(ISettingsStore store)
    {
        _store = store;

        // The audit is rare and important, so it is written as it happens.
        Audit.Changed += (_, _) => CrashLog.FireAndForget(SaveAuditAsync());
    }

    public ExposureTimeline Timeline { get; } = new();

    public ProtectionAudit Audit { get; } = new();

    /// <summary>The user's self-set daily allowance of loud listening.</summary>
    public ListeningBudget Budget { get; set; } = ListeningBudget.Default;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var timeline = await _store.LoadAsync<SavedTimeline>(TimelineKey, cancellationToken);

        if (timeline is not null)
        {
            Timeline.Restore(timeline.Buckets);
            Timeline.Prune(DateTimeOffset.UtcNow);
        }

        var audit = await _store.LoadAsync<List<ProtectionEntry>>(AuditKey, cancellationToken);

        if (audit is not null)
        {
            Audit.Restore(audit);
        }
    }

    /// <summary>Records one sampling tick and saves if enough time has passed since the last write.</summary>
    public void RecordSample(DateTimeOffset now, bool loud, TimeSpan interval)
    {
        Timeline.Record(now, loud, interval);

        if (now - _lastSaved < SaveInterval)
        {
            return;
        }

        _lastSaved = now;
        CrashLog.FireAndForget(SaveTimelineAsync());
    }

    public void Record(ProtectionEvent kind, string detail = "") =>
        Audit.Record(DateTimeOffset.UtcNow, kind, detail);

    public Task SaveTimelineAsync(CancellationToken cancellationToken = default) =>
        _store.SaveAsync(
            TimelineKey,
            new SavedTimeline(new Dictionary<DateTimeOffset, TimeSpan>(Timeline.Snapshot())),
            cancellationToken);

    public Task SaveAuditAsync(CancellationToken cancellationToken = default) =>
        _store.SaveAsync(AuditKey, Audit.Entries.ToList(), cancellationToken);
}
