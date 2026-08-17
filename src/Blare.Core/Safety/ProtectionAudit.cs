namespace Blight.Blare.Core.Safety;

public enum ProtectionEvent
{
    WarningsDisabled,
    WarningsReenabled,

    /// <summary>The opt-out reached its expiry and warnings came back on their own.</summary>
    WarningsExpired,

    QuietHoursChanged,
    CapSet,
    CapCleared,
}

public sealed record ProtectionEntry(DateTimeOffset When, ProtectionEvent Event, string Detail);

/// <summary>
/// A record of when Blare's safeguards were turned off, changed, or came back.
///
/// The point is honesty about the app's own behaviour. Warnings can be disabled,
/// and they re-enable themselves after a period — so a user who does not
/// remember doing either has no way to tell whether they were protected last
/// month or whether the app quietly stopped watching. This is the answer, and it
/// stays on the machine like everything else.
///
/// Append-only and capped: it is a log, not a database, and an unbounded one on
/// a background timer is a slow leak.
/// </summary>
public sealed class ProtectionAudit
{
    public const int MaximumEntries = 200;

    private readonly List<ProtectionEntry> _entries = new();

    /// <summary>Newest first, which is the order it is read in.</summary>
    public IReadOnlyList<ProtectionEntry> Entries => _entries;

    public event EventHandler? Changed;

    public void Record(DateTimeOffset when, ProtectionEvent kind, string detail = "")
    {
        _entries.Insert(0, new ProtectionEntry(when, kind, detail));

        if (_entries.Count > MaximumEntries)
        {
            _entries.RemoveRange(MaximumEntries, _entries.Count - MaximumEntries);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether protection was off at any point in the given window — the question worth asking of a log like this.</summary>
    public bool WasEverDisabled(DateTimeOffset since) =>
        _entries.Any(entry => entry.When >= since && entry.Event == ProtectionEvent.WarningsDisabled);

    public void Restore(IEnumerable<ProtectionEntry>? entries)
    {
        _entries.Clear();

        if (entries is null)
        {
            return;
        }

        _entries.AddRange(entries.OrderByDescending(entry => entry.When).Take(MaximumEntries));
    }
}
