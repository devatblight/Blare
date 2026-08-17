namespace Blight.Blare.Audio.Sessions;

/// <summary>Plain snapshot of one enumerated session — no COM types, so this is easy to fake in tests.</summary>
public sealed record SessionSnapshot(string SessionKey, Guid GroupingParam, uint ProcessId);

public sealed record SessionGroupRow(
    string GroupKey,
    uint ProcessId,
    IReadOnlyList<string> SessionKeys,
    bool PendingRemoval);

/// <summary>
/// Reconciles raw per-poll session snapshots into stable mixer rows (see
/// plan §2): sessions sharing a grouping GUID collapse into one row, and a
/// row that stops appearing isn't removed instantly — it's held for a
/// debounce window in case the app briefly recreates its session (some
/// browsers do this), then dropped if it never comes back.
/// </summary>
public sealed class SessionGroupTracker
{
    private readonly TimeSpan _removalDebounce;
    private readonly Dictionary<string, RowState> _rows = new();

    public SessionGroupTracker(TimeSpan? removalDebounce = null)
    {
        _removalDebounce = removalDebounce ?? TimeSpan.FromSeconds(2.5);
    }

    public static string ComputeGroupKey(Guid groupingParam, string sessionKey, uint processId) =>
        groupingParam == Guid.Empty ? $"session:{sessionKey}" : $"group:{groupingParam}:{processId}";

    /// <summary>Feeds one poll cycle's worth of currently-enumerated sessions and returns the current row set.</summary>
    public IReadOnlyList<SessionGroupRow> Reconcile(IReadOnlyList<SessionSnapshot> currentSessions, DateTimeOffset now)
    {
        var seenKeys = new HashSet<string>();

        foreach (var snapshot in currentSessions)
        {
            var groupKey = ComputeGroupKey(snapshot.GroupingParam, snapshot.SessionKey, snapshot.ProcessId);
            seenKeys.Add(groupKey);

            if (!_rows.TryGetValue(groupKey, out var row))
            {
                row = new RowState(snapshot.ProcessId);
                _rows[groupKey] = row;
            }

            row.SessionKeys.Add(snapshot.SessionKey);
            row.PendingRemovalSince = null;
        }

        foreach (var key in _rows.Keys.ToList())
        {
            if (seenKeys.Contains(key))
            {
                continue;
            }

            var row = _rows[key];
            row.PendingRemovalSince ??= now;

            if (now - row.PendingRemovalSince >= _removalDebounce)
            {
                _rows.Remove(key);
            }
        }

        return _rows
            .Select(kvp => new SessionGroupRow(
                kvp.Key,
                kvp.Value.ProcessId,
                kvp.Value.SessionKeys.ToList(),
                kvp.Value.PendingRemovalSince is not null))
            .ToList();
    }

    private sealed class RowState(uint processId)
    {
        public uint ProcessId { get; } = processId;

        public HashSet<string> SessionKeys { get; } = new();

        public DateTimeOffset? PendingRemovalSince { get; set; }
    }
}
