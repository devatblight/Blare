namespace Blight.Blare.Core.Safety;

/// <summary>
/// A ceiling that applies during a window of the day.
///
/// The point is late-night listening: the same level that is fine at midday is
/// not fine at 2am with headphones on, and the person best placed to decide that
/// is the one setting it up while awake.
/// </summary>
public sealed record QuietHours(bool Enabled, TimeOnly Start, TimeOnly End, double CeilingPercent)
{
    public static QuietHours Off => new(false, new TimeOnly(23, 0), new TimeOnly(7, 0), 40);

    /// <summary>
    /// Whether a moment falls inside the window, handling one that runs past
    /// midnight — which is the normal case for quiet hours and the easy thing to
    /// get wrong.
    /// </summary>
    public bool Contains(TimeOnly time)
    {
        if (!Enabled)
        {
            return false;
        }

        if (Start == End)
        {
            // A zero-length window is off, not "always on".
            return false;
        }

        return Start < End
            ? time >= Start && time < End
            : time >= Start || time < End;
    }

    /// <summary>The ceiling in force at a given time — 100 when the window doesn't apply.</summary>
    public double CeilingAt(TimeOnly time) =>
        Contains(time) ? Math.Clamp(CeilingPercent, 0, 100) : 100;
}

/// <summary>
/// Per-app ceilings the user has set, plus whatever quiet hours are imposing.
///
/// Kept apart from the volume store: a saved level is where you last left an app,
/// while a cap is a rule about where it is allowed to go. Conflating them means
/// "restore my level" can quietly undo a limit the user set deliberately.
/// </summary>
public sealed class ListeningLimits
{
    private readonly Dictionary<string, double> _caps = new(StringComparer.OrdinalIgnoreCase);

    public QuietHours QuietHours { get; set; } = QuietHours.Off;

    public IReadOnlyDictionary<string, double> Caps => _caps;

    /// <summary>Raised whenever a limit changes, so it can be persisted and re-applied.</summary>
    public event EventHandler? Changed;

    public void SetCap(string appKey, double percent)
    {
        if (string.IsNullOrWhiteSpace(appKey))
        {
            return;
        }

        _caps[appKey] = Math.Clamp(percent, 0, 100);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ClearCap(string appKey)
    {
        if (_caps.Remove(appKey))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public double? CapFor(string appKey) =>
        !string.IsNullOrWhiteSpace(appKey) && _caps.TryGetValue(appKey, out var cap) ? cap : null;

    public void SetQuietHours(QuietHours quietHours)
    {
        QuietHours = quietHours;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The highest level an app may be set to right now. The lowest limit wins:
    /// a cap the user set is not overridden by quiet hours being generous, and
    /// quiet hours are not overridden by a permissive per-app cap.
    /// </summary>
    public double CeilingFor(string appKey, TimeOnly time) =>
        Math.Min(CapFor(appKey) ?? 100, QuietHours.CeilingAt(time));

    /// <summary>Clamps a requested level to whatever is currently allowed.</summary>
    public double Apply(string appKey, double requestedPercent, TimeOnly time) =>
        Math.Clamp(requestedPercent, 0, CeilingFor(appKey, time));

    public IReadOnlyDictionary<string, double> Snapshot() => new Dictionary<string, double>(_caps);

    public void Restore(IReadOnlyDictionary<string, double>? caps, QuietHours? quietHours)
    {
        _caps.Clear();

        if (caps is not null)
        {
            foreach (var (key, value) in caps)
            {
                _caps[key] = Math.Clamp(value, 0, 100);
            }
        }

        if (quietHours is not null)
        {
            QuietHours = quietHours;
        }
    }
}
