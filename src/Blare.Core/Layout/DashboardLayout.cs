namespace Blight.Blare.Core.Layout;

/// <summary>
/// The kinds of card a user can place on the dashboard.
///
/// Deliberately a closed set: Blare supplies the cards, the user decides which
/// ones exist and where they sit. That keeps every card something we can hold
/// to a standard, while the arrangement stays entirely theirs.
/// </summary>
public enum CardKind
{
    /// <summary>Channel strips for everything currently playing.</summary>
    AppMixer,

    /// <summary>The default output device's volume.</summary>
    MasterOutput,

    /// <summary>Every other output device, each with its own fader.</summary>
    OtherOutputs,

    /// <summary>Speaker volume built into displays, over DDC/CI.</summary>
    DisplaySpeakers,

    /// <summary>Warnings raised and time spent loud.</summary>
    HearingStatus,

    /// <summary>Mute all, unmute all, clear focus.</summary>
    QuickActions,

    /// <summary>The loudest app right now, at a glance.</summary>
    NowPlaying,
}

/// <summary>One placed card. <paramref name="Id"/> is unique per instance so the same kind can appear more than once.</summary>
public sealed record DashboardCard(string Id, CardKind Kind, int Column, int Row, int ColumnSpan, int RowSpan)
{
    public int Right => Column + ColumnSpan;

    public int Bottom => Row + RowSpan;

    public bool Overlaps(DashboardCard other) =>
        Column < other.Right && other.Column < Right &&
        Row < other.Bottom && other.Row < Bottom;
}

/// <summary>
/// The dashboard: a grid of cards the user arranges.
///
/// Geometry lives here rather than in the UI because clamping, overlap and
/// finding a free slot are easy to get subtly wrong and easy to test. The
/// shipped default still has to be good on its own — being rearrangeable is no
/// excuse for a poor starting point.
/// </summary>
public sealed class DashboardLayout
{
    public const int Columns = 12;
    public const int Rows = 12;
    public const int MinimumSpan = 2;

    private readonly List<DashboardCard> _cards = new();

    public IReadOnlyList<DashboardCard> Cards => _cards;

    public DashboardCard? Get(string id) => _cards.FirstOrDefault(c => c.Id == id);

    /// <summary>The arrangement shipped out of the box, and what "Reset layout" restores.</summary>
    public static DashboardLayout CreateDefault()
    {
        var layout = new DashboardLayout();

        layout.Add(new DashboardCard("master", CardKind.MasterOutput, 0, 0, 5, 3));
        layout.Add(new DashboardCard("status", CardKind.HearingStatus, 5, 0, 4, 3));
        layout.Add(new DashboardCard("actions", CardKind.QuickActions, 9, 0, 3, 3));
        layout.Add(new DashboardCard("mixer", CardKind.AppMixer, 0, 3, 12, 9));

        return layout;
    }

    public void Add(DashboardCard card) => _cards.Add(Clamp(card));

    public void Remove(string id) => _cards.RemoveAll(c => c.Id == id);

    public void Move(string id, int column, int row) =>
        Replace(id, card => card with { Column = column, Row = row });

    public void Resize(string id, int columnSpan, int rowSpan) =>
        Replace(id, card => card with { ColumnSpan = columnSpan, RowSpan = rowSpan });

    /// <summary>
    /// Finds somewhere a new card of the given size will fit without covering
    /// an existing one, scanning left to right, top to bottom. Returns null when
    /// the grid is full, so the UI can say so rather than stacking cards.
    /// </summary>
    public (int Column, int Row)? FindFreeSlot(int columnSpan, int rowSpan)
    {
        var width = Math.Clamp(columnSpan, MinimumSpan, Columns);
        var height = Math.Clamp(rowSpan, MinimumSpan, Rows);

        for (var row = 0; row <= Rows - height; row++)
        {
            for (var column = 0; column <= Columns - width; column++)
            {
                var candidate = new DashboardCard("probe", CardKind.AppMixer, column, row, width, height);

                if (!_cards.Any(existing => existing.Overlaps(candidate)))
                {
                    return (column, row);
                }
            }
        }

        return null;
    }

    public static DashboardLayout FromCards(IEnumerable<DashboardCard> cards)
    {
        var layout = new DashboardLayout();

        foreach (var card in cards)
        {
            layout.Add(card);
        }

        // An empty or unreadable saved layout would leave a blank screen with no
        // obvious way back, so fall back to the default instead.
        return layout._cards.Count == 0 ? CreateDefault() : layout;
    }

    private void Replace(string id, Func<DashboardCard, DashboardCard> update)
    {
        var index = _cards.FindIndex(c => c.Id == id);

        if (index >= 0)
        {
            _cards[index] = Clamp(update(_cards[index]));
        }
    }

    private static DashboardCard Clamp(DashboardCard card)
    {
        var columnSpan = Math.Clamp(card.ColumnSpan, MinimumSpan, Columns);
        var rowSpan = Math.Clamp(card.RowSpan, MinimumSpan, Rows);

        // Clamp the origin after the span so a card dragged toward an edge
        // slides back inside rather than being silently shrunk.
        return card with
        {
            Column = Math.Clamp(card.Column, 0, Columns - columnSpan),
            Row = Math.Clamp(card.Row, 0, Rows - rowSpan),
            ColumnSpan = columnSpan,
            RowSpan = rowSpan,
        };
    }
}
