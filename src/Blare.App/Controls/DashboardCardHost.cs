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

        _editOverlay = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Resource("BlareAccent"),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Visibility = Visibility.Collapsed,
        };

        _resizeGrip = new Rectangle
        {
            Width = 16,
            Height = 16,
            RadiusX = 3,
            RadiusY = 3,
            Fill = Resource("BlareAccent"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 3, 3),
            Visibility = Visibility.Collapsed,
            ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY,
        };

        RemoveButton = new Button
        {
            Content = new FontIcon { Glyph = char.ConvertFromUtf32(0xE711), FontSize = 11 },
            Padding = new Thickness(5, 1, 5, 1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Visibility = Visibility.Collapsed,
        };

        var root = new Grid();
        root.Children.Add(_chrome);
        root.Children.Add(_editOverlay);
        root.Children.Add(_resizeGrip);
        root.Children.Add(RemoveButton);

        Content = root;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        RenderTransform = _dragOffset;

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
        Card = card;
        ClearDragOffset();
    }

    public void SetCellSize(double width, double height)
    {
        _cellWidth = Math.Max(1, width);
        _cellHeight = Math.Max(1, height);
    }

    public void SetEditing(bool editing)
    {
        _editing = editing;

        var visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _editOverlay.Visibility = visibility;
        _resizeGrip.Visibility = visibility;
        RemoveButton.Visibility = visibility;

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

        // Lift it visually so it reads as picked up.
        Opacity = 0.75;
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

        var target = Card with
        {
            Column = Card.Column + _pendingColumns,
            Row = Card.Row + _pendingRows,
        };

        _pendingColumns = 0;
        _pendingRows = 0;
        ClearDragOffset();

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
