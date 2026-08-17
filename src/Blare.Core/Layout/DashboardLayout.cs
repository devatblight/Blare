namespace Blight.Blare.Core.Layout;

/// <summary>Where one panel sits on the dashboard grid.</summary>
public sealed record PanelLayout(string PanelId, int Column, int Row, int ColumnSpan, int RowSpan)
{
    public int Right => Column + ColumnSpan;

    public int Bottom => Row + RowSpan;

    public bool Overlaps(PanelLayout other) =>
        Column < other.Right && other.Column < Right &&
        Row < other.Bottom && other.Row < Bottom;
}

/// <summary>
/// The arrangement of the mixer's panels.
///
/// Everything on the main screen is a panel the user can move and resize, so
/// the layout is data rather than markup. The shipped default still has to be
/// good on its own — being rearrangeable is not an excuse for a poor starting
/// point — so <see cref="CreateDefault"/> is the considered layout, not an
/// empty canvas.
///
/// Geometry lives here, away from the UI, because clamping and overlap are
/// exactly the sort of thing that is easy to get subtly wrong and easy to test.
/// </summary>
public sealed class DashboardLayout
{
    public const int Columns = 12;
    public const int Rows = 12;
    public const int MinimumSpan = 2;

    public const string MasterPanel = "master";
    public const string DisplaysPanel = "displays";
    public const string StatusPanel = "status";
    public const string DeskPanel = "desk";

    private readonly Dictionary<string, PanelLayout> _panels = new();

    public IReadOnlyCollection<PanelLayout> Panels => _panels.Values;

    public PanelLayout? Get(string panelId) => _panels.GetValueOrDefault(panelId);

    public void Set(PanelLayout panel) => _panels[panel.PanelId] = Clamp(panel);

    /// <summary>
    /// The default arrangement: master and displays share the top row rather
    /// than each spanning the full width, and the desk takes the whole lower
    /// two-thirds so channel strips get the room they actually need.
    /// </summary>
    public static DashboardLayout CreateDefault()
    {
        var layout = new DashboardLayout();

        layout.Set(new PanelLayout(MasterPanel, Column: 0, Row: 0, ColumnSpan: 5, RowSpan: 3));
        layout.Set(new PanelLayout(DisplaysPanel, Column: 5, Row: 0, ColumnSpan: 4, RowSpan: 3));
        layout.Set(new PanelLayout(StatusPanel, Column: 9, Row: 0, ColumnSpan: 3, RowSpan: 3));
        layout.Set(new PanelLayout(DeskPanel, Column: 0, Row: 3, ColumnSpan: 12, RowSpan: 9));

        return layout;
    }

    /// <summary>Moves a panel, keeping it wholly on the grid.</summary>
    public void Move(string panelId, int column, int row)
    {
        if (_panels.TryGetValue(panelId, out var panel))
        {
            _panels[panelId] = Clamp(panel with { Column = column, Row = row });
        }
    }

    /// <summary>Resizes a panel, keeping it at least the minimum span and wholly on the grid.</summary>
    public void Resize(string panelId, int columnSpan, int rowSpan)
    {
        if (_panels.TryGetValue(panelId, out var panel))
        {
            _panels[panelId] = Clamp(panel with { ColumnSpan = columnSpan, RowSpan = rowSpan });
        }
    }

    public IReadOnlyList<PanelLayout> ToList() => _panels.Values.ToList();

    public static DashboardLayout FromPanels(IEnumerable<PanelLayout> panels)
    {
        var layout = new DashboardLayout();

        foreach (var panel in panels)
        {
            layout.Set(panel);
        }

        // A saved layout from an older version may be missing panels added
        // since; fill them from the default rather than dropping them.
        foreach (var fallback in CreateDefault().Panels)
        {
            if (!layout._panels.ContainsKey(fallback.PanelId))
            {
                layout.Set(fallback);
            }
        }

        return layout;
    }

    private static PanelLayout Clamp(PanelLayout panel)
    {
        var columnSpan = Math.Clamp(panel.ColumnSpan, MinimumSpan, Columns);
        var rowSpan = Math.Clamp(panel.RowSpan, MinimumSpan, Rows);

        // Clamp the origin after the span, so a panel dragged toward the edge
        // slides back inside rather than being silently shrunk.
        var column = Math.Clamp(panel.Column, 0, Columns - columnSpan);
        var row = Math.Clamp(panel.Row, 0, Rows - rowSpan);

        return panel with
        {
            Column = column,
            Row = row,
            ColumnSpan = columnSpan,
            RowSpan = rowSpan,
        };
    }
}
