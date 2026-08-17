using Blight.Blare.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Blight.Blare.App.Controls;

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

        _chrome = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = Resource("BlareStripBackground"),
            BorderBrush = Resource("BlareStripBorder"),
            Padding = new Thickness(14, 12, 14, 12),
            Child = BuildBody(title, content),
        };

        // The edit affordances start transparent as well as collapsed: they are
        // faded in and out, and an opacity of 1 would make the first fade a no-op.
        _editOverlay = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Resource("BlareAccent"),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            IsHitTestVisible = false,
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

    /// <summary>Fades the card in on its way to its slot, staggered behind the ones before it.</summary>
    public void PlayEntrance(int index) => Motion.EnterStaggered(this, _dragOffset, index);

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

        // Grow live under the pointer rather than only on release.
        Width = Math.Max(_cellWidth, (Card.ColumnSpan * _cellWidth) + _sizeDeltaX);
        Height = Math.Max(_cellHeight, (Card.RowSpan * _cellHeight) + _sizeDeltaY);

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

    private static UIElement BuildBody(string title, UIElement content)
    {
        var stack = new StackPanel { Spacing = 8 };

        stack.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 9,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Opacity = 0.5,
        });

        stack.Children.Add(content);
        return stack;
    }

    private static Brush? Resource(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;
}
