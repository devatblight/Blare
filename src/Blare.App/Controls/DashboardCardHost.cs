using Blight.Blare.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Blight.Blare.App.Controls;

/// <summary>
/// How much room a card has to work with. Cards render a different layout in
/// each band rather than shrinking one layout until it stops being readable.
/// </summary>
public enum CardDensity
{
    /// <summary>Barely more than a row: essentials only, labels dropped.</summary>
    Compact,

    /// <summary>Enough for the controls that matter, without the extras.</summary>
    Normal,

    /// <summary>Room for the full treatment, meters and all.</summary>
    Expanded,
}

/// <summary>
/// Wraps one card on the dashboard and, in edit mode, lets it be dragged,
/// resized and removed.
///
/// The card follows the pointer continuously while dragging and only commits to
/// a grid cell when released. An earlier version snapped and ended the
/// manipulation as soon as the drag crossed a single cell, which felt like the
/// card twitched a few pixels and then dropped wherever it liked.
/// </summary>
public sealed class DashboardCardHost : ContentControl
{
    private readonly Border _chrome;
    private readonly Border _editOverlay;
    private readonly Rectangle _resizeGrip;
    private readonly TranslateTransform _dragOffset = new();
    private readonly ScaleTransform _liftScale = new() { ScaleX = 1, ScaleY = 1 };
    private readonly StackPanel _body;

    private double _cellWidth = 1;
    private double _cellHeight = 1;
    private bool _editing;

    // Live gesture state, before it is committed to the model.
    private int _pendingColumns;
    private int _pendingRows;
    private double _sizeDeltaX;
    private double _sizeDeltaY;

    public DashboardCardHost(DashboardCard card, string title, UIElement content)
    {
        Card = card;

        _body = new StackPanel { Spacing = 8 };
        _body.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 9,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Opacity = 0.5,
        });

        _body.Children.Add(content);

        // Last line of defence against clipping. Card content adapts to the
        // space it has, but a list of eight output devices can still outgrow any
        // sensible minimum, and scrolling beats silently cutting content off.
        _chrome = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = Resource("BlareStripBackground"),
            BorderBrush = Resource("BlareStripBorder"),
            Padding = new Thickness(14, 12, 14, 12),
            Child = new ScrollViewer
            {
                Content = _body,
                VerticalScrollMode = ScrollMode.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };

        // The edit affordances start transparent as well as collapsed: they are
        // faded in and out, and an opacity of 1 would make the first fade a no-op.
        //
        // This overlay must stay hit-testable. In edit mode the chrome below is
        // switched off so the card's own sliders cannot swallow the gesture,
        // which leaves this transparent layer as the only thing the pointer can
        // land on — and therefore the entire drag surface. Marking it
        // IsHitTestVisible="False" silently kills drag and drop.
        _editOverlay = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Resource("BlareAccent"),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Visibility = Visibility.Collapsed,
            Opacity = 0,
        };

        _resizeGrip = new Rectangle
        {
            Width = 14,
            Height = 14,
            RadiusX = 3,
            RadiusY = 3,
            Fill = Resource("BlareAccent"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 3, 3),
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY,
        };

        ToolTipService.SetToolTip(_resizeGrip, "Drag to resize");

        RemoveButton = new Button
        {
            Content = new FontIcon { Glyph = char.ConvertFromUtf32(0xE711), FontSize = 11 },
            Padding = new Thickness(5, 1, 5, 1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Visibility = Visibility.Collapsed,
            Opacity = 0,
        };

        ToolTipService.SetToolTip(RemoveButton, "Remove this card");

        var root = new Grid();
        root.Children.Add(_chrome);
        root.Children.Add(_editOverlay);
        root.Children.Add(_resizeGrip);
        root.Children.Add(RemoveButton);

        Content = root;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        RenderTransform = _dragOffset;

        // Scaling the chrome rather than the host keeps the drag translation and
        // the pick-up lift on separate transforms, so neither clobbers the other.
        _chrome.RenderTransform = _liftScale;
        _chrome.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

        ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY;
        ManipulationStarted += OnDragStarted;
        ManipulationDelta += OnDragDelta;
        ManipulationCompleted += OnDragCompleted;

        _resizeGrip.ManipulationStarted += OnResizeStarted;
        _resizeGrip.ManipulationDelta += OnResizeDelta;
        _resizeGrip.ManipulationCompleted += OnResizeCompleted;

        SizeChanged += (_, e) => UpdateDensity(e.NewSize.Width, e.NewSize.Height);
    }

    public DashboardCard Card { get; private set; }

    public Button RemoveButton { get; }

    /// <summary>Raised while dragging with the cell the card would land on, so the page can preview the drop.</summary>
    public event EventHandler<DashboardCard>? Previewing;

    /// <summary>Raised on release with the final requested geometry.</summary>
    public event EventHandler<DashboardCard>? Committed;

    /// <summary>Adopts the model's geometry after it has clamped, displaced or refused a change.</summary>
    public void ApplyCard(DashboardCard card)
    {
        // The card is about to jump to its new cell in layout. Pre-load the
        // inverse of that jump onto the drag transform and let it glide back to
        // zero, so the move reads as travel rather than a teleport. This covers
        // three cases with one mechanism: the dragged card settling the last few
        // pixels into its cell, a refused drop springing back to where it came
        // from, and a displaced card sliding out of the way.
        var jumpX = (card.Column - Card.Column) * _cellWidth;
        var jumpY = (card.Row - Card.Row) * _cellHeight;

        Card = card;
        Opacity = 1;

        Motion.SettleFrom(_dragOffset, _dragOffset.X - jumpX, _dragOffset.Y - jumpY);
    }

    /// <summary>How much room this card currently has. Changes as it is resized or the window is.</summary>
    public CardDensity Density { get; private set; } = CardDensity.Normal;

    /// <summary>Raised when the card crosses into a different size band and its content should be rebuilt.</summary>
    public event EventHandler<CardDensity>? DensityChanged;

    /// <summary>Swaps the card's content, keeping its title and chrome.</summary>
    public void SetBody(UIElement content)
    {
        // Index 0 is the title.
        if (_body.Children.Count > 1)
        {
            _body.Children.RemoveAt(1);
        }

        _body.Children.Add(content);
    }

    /// <summary>Fades the card in on its way to its slot, staggered behind the ones before it.</summary>
    public void PlayEntrance(int index) => Motion.EnterStaggered(this, _dragOffset, index);

    private void UpdateDensity(double width, double height)
    {
        // Measured against the space left after the chrome's padding and title
        // row, which is what the content actually gets.
        var usableHeight = height - 46;

        var density = width < 240 || usableHeight < 150
            ? CardDensity.Compact
            : usableHeight < 280
                ? CardDensity.Normal
                : CardDensity.Expanded;

        if (density == Density)
        {
            return;
        }

        Density = density;
        DensityChanged?.Invoke(this, density);
    }

    public void SetCellSize(double width, double height)
    {
        _cellWidth = Math.Max(1, width);
        _cellHeight = Math.Max(1, height);
    }

    public void SetEditing(bool editing)
    {
        _editing = editing;

        Motion.ToggleLayer(_editOverlay, editing, Motion.Normal);
        Motion.ToggleLayer(_resizeGrip, editing, Motion.Normal);
        Motion.ToggleLayer(RemoveButton, editing, Motion.Normal);

        // The card's own controls stay live outside edit mode; inside it they
        // must not swallow the drag.
        _chrome.IsHitTestVisible = !editing;

        if (!editing)
        {
            ClearDragOffset();
        }
    }

    // ---- move ----------------------------------------------------------------

    private void OnDragStarted(object sender, ManipulationStartedRoutedEventArgs e)
    {
        if (!_editing)
        {
            return;
        }

        // Lift it visually so it reads as picked up rather than as the pointer
        // happening to be over it.
        Motion.FadeTo(this, 0.9);
        Motion.ScaleTo(_liftScale, 1.03);
        Canvas.SetZIndex(this, 10);
    }

    private void OnDragDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (!_editing)
        {
            return;
        }

        // Follow the pointer exactly; snapping happens on release.
        _dragOffset.X = e.Cumulative.Translation.X;
        _dragOffset.Y = e.Cumulative.Translation.Y;

        var columns = (int)Math.Round(e.Cumulative.Translation.X / _cellWidth);
        var rows = (int)Math.Round(e.Cumulative.Translation.Y / _cellHeight);

        if (columns == _pendingColumns && rows == _pendingRows)
        {
            return;
        }

        _pendingColumns = columns;
        _pendingRows = rows;

        Previewing?.Invoke(this, Card with
        {
            Column = Card.Column + columns,
            Row = Card.Row + rows,
        });
    }

    private void OnDragCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        if (!_editing)
        {
            return;
        }

        Canvas.SetZIndex(this, 0);
        Motion.ScaleTo(_liftScale, 1);

        var target = Card with
        {
            Column = Card.Column + _pendingColumns,
            Row = Card.Row + _pendingRows,
        };

        _pendingColumns = 0;
        _pendingRows = 0;

        // The drag offset is deliberately left in place: ApplyCard turns it into
        // the settle animation once the model has had its say.
        Committed?.Invoke(this, target);
    }

    // ---- resize --------------------------------------------------------------

    private void OnResizeStarted(object sender, ManipulationStartedRoutedEventArgs e)
    {
        _sizeDeltaX = 0;
        _sizeDeltaY = 0;
        e.Handled = true;
    }

    private void OnResizeDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (!_editing)
        {
            return;
        }

        _sizeDeltaX = e.Cumulative.Translation.X;
        _sizeDeltaY = e.Cumulative.Translation.Y;

        // Grow live under the pointer, but stop at the card's own limits rather
        // than letting it be dragged to a size the model will only snap back.
        var bounds = CardSizing.For(Card.Kind);

        Width = Math.Clamp(
            (Card.ColumnSpan * _cellWidth) + _sizeDeltaX,
            bounds.MinColumns * _cellWidth,
            bounds.MaxColumns * _cellWidth);

        Height = Math.Clamp(
            (Card.RowSpan * _cellHeight) + _sizeDeltaY,
            bounds.MinRows * _cellHeight,
            bounds.MaxRows * _cellHeight);

        e.Handled = true;
    }

    private void OnResizeCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        if (!_editing)
        {
            return;
        }

        var target = Card with
        {
            ColumnSpan = Card.ColumnSpan + (int)Math.Round(_sizeDeltaX / _cellWidth),
            RowSpan = Card.RowSpan + (int)Math.Round(_sizeDeltaY / _cellHeight),
        };

        _sizeDeltaX = 0;
        _sizeDeltaY = 0;
        e.Handled = true;

        Committed?.Invoke(this, target);
    }

    private void ClearDragOffset()
    {
        _dragOffset.X = 0;
        _dragOffset.Y = 0;
        Opacity = 1;
    }

    private static Brush? Resource(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;
}
