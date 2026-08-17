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
/// Movement snaps to grid cells rather than following the pointer freely, so a
/// user cannot produce a layout the model would have to silently correct — what
/// they drag is exactly what gets saved.
/// </summary>
public sealed class DashboardCardHost : ContentControl
{
    private readonly Border _chrome;
    private readonly Grid _root;
    private readonly Border _editOverlay;
    private readonly Rectangle _resizeGrip;

    private double _cellWidth = 1;
    private double _cellHeight = 1;
    private bool _editing;

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
            // Sits above the card so its own controls don't steal the drag.
            IsHitTestVisible = true,
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
            Margin = new Thickness(0, 0, 4, 4),
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

        _root = new Grid();
        _root.Children.Add(_chrome);
        _root.Children.Add(_editOverlay);
        _root.Children.Add(_resizeGrip);
        _root.Children.Add(RemoveButton);

        Content = _root;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;

        ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY;
        ManipulationDelta += OnCardDrag;
        _resizeGrip.ManipulationDelta += OnGripDrag;
    }

    public DashboardCard Card { get; private set; }

    public Button RemoveButton { get; }

    /// <summary>Raised when a drag or resize finishes with a new grid position.</summary>
    public event EventHandler<DashboardCard>? Changed;

    /// <summary>Adopts the model's corrected geometry after a drag was clamped.</summary>
    public void ApplyCard(DashboardCard card) => Card = card;

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

        // The card's own controls stay live outside edit mode; inside it, they
        // must not swallow the drag.
        _chrome.IsHitTestVisible = !editing;
    }

    private void OnCardDrag(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (!_editing)
        {
            return;
        }

        var columns = (int)Math.Round(e.Cumulative.Translation.X / _cellWidth);
        var rows = (int)Math.Round(e.Cumulative.Translation.Y / _cellHeight);

        if (columns == 0 && rows == 0)
        {
            return;
        }

        Card = Card with { Column = Card.Column + columns, Row = Card.Row + rows };
        Changed?.Invoke(this, Card);
        e.Complete();
    }

    private void OnGripDrag(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (!_editing)
        {
            return;
        }

        var columns = (int)Math.Round(e.Cumulative.Translation.X / _cellWidth);
        var rows = (int)Math.Round(e.Cumulative.Translation.Y / _cellHeight);

        if (columns == 0 && rows == 0)
        {
            return;
        }

        Card = Card with { ColumnSpan = Card.ColumnSpan + columns, RowSpan = Card.RowSpan + rows };
        Changed?.Invoke(this, Card);
        e.Complete();
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
