namespace BLight.Blare.Core.Safety;

/// <summary>
/// Independent things a user can explicitly opt out of. Kept separate so
/// disabling warnings and overriding the boost ceiling don't share one
/// on/off switch.
/// </summary>
public enum ConsentKind
{
    SafetyWarningsDisabled,
    SafeBoostCeilingOverride,
}

public sealed record ConsentRecord(
    ConsentKind Kind,
    bool IsActive,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? LastReconfirmedAt)
{
    public static ConsentRecord NotGranted(ConsentKind kind) => new(kind, false, null, null);
}

/// <summary>
/// Tracks explicit user opt-outs (see the two-gate confirmation dialog
/// design in the plan) and their expiry. An opt-out is only "in effect"
/// for a bounded interval — once it expires, the safe default (warnings
/// on / ceiling enforced) resumes automatically rather than staying
/// disabled forever. The app should show a one-time notice when a consent
/// expires and reverts, rather than silently re-enabling.
/// </summary>
public sealed class ConsentState
{
    private readonly TimeSpan _reconfirmationInterval;
    private readonly Dictionary<ConsentKind, ConsentRecord> _records = new();

    public ConsentState(TimeSpan? reconfirmationInterval = null)
    {
        _reconfirmationInterval = reconfirmationInterval ?? TimeSpan.FromDays(30);
    }

    public ConsentRecord Grant(ConsentKind kind, DateTimeOffset now)
    {
        var record = new ConsentRecord(kind, IsActive: true, ConfirmedAt: now, LastReconfirmedAt: now);
        _records[kind] = record;
        return record;
    }

    public ConsentRecord Revoke(ConsentKind kind)
    {
        var record = ConsentRecord.NotGranted(kind);
        _records[kind] = record;
        return record;
    }

    /// <summary>Whether this opt-out is still in effect right now — false once it has expired.</summary>
    public bool IsActive(ConsentKind kind, DateTimeOffset now)
    {
        if (!_records.TryGetValue(kind, out var record) || !record.IsActive)
        {
            return false;
        }

        if (record.LastReconfirmedAt is not { } lastConfirmed)
        {
            return false;
        }

        return now - lastConfirmed < _reconfirmationInterval;
    }

    /// <summary>True the instant an opt-out crosses its reconfirmation interval — the host uses this to fire the one-time "warnings re-enabled" notice.</summary>
    public bool HasExpired(ConsentKind kind, DateTimeOffset now)
    {
        if (!_records.TryGetValue(kind, out var record) || !record.IsActive)
        {
            return false;
        }

        return !IsActive(kind, now);
    }

    public ConsentRecord? GetRecord(ConsentKind kind) =>
        _records.TryGetValue(kind, out var record) ? record : null;
}
