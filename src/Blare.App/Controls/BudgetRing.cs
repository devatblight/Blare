using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

// Both Shapes.Path and System.IO.Path are in scope through the implicit usings.
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Blight.Blare.App.Controls;

/// <summary>
/// A ring that fills as the day's loud-listening budget is used.
///
/// Chosen over a bar because the thing being shown is a proportion of something
/// finite, and a ring reads as "how much of it is gone" at a glance without a
/// number being read. The sweep animates rather than jumping, so a glance at it
/// twenty minutes apart shows movement rather than a different static picture.
///
/// The arc is rebuilt from a dependency property, so the animation is a
/// dependent one — fine here, since it runs for a few hundred milliseconds every
/// few seconds at most, not per frame like the meters.
/// </summary>
public sealed class BudgetRing : UserControl
{
    private const double Thickness = 7;

    private readonly Path _track = new();
    private readonly Path _fill = new();
    private readonly TextBlock _caption = new();

    public BudgetRing()
    {
        _track.StrokeThickness = Thickness;
        _track.StrokeStartLineCap = PenLineCap.Round;
        _track.StrokeEndLineCap = PenLineCap.Round;
        _track.Opacity = 0.25;

        _fill.StrokeThickness = Thickness;
        _fill.StrokeStartLineCap = PenLineCap.Round;
        _fill.StrokeEndLineCap = PenLineCap.Round;

        _caption.HorizontalAlignment = HorizontalAlignment.Center;
        _caption.VerticalAlignment = VerticalAlignment.Center;
        _caption.FontSize = 15;
        _caption.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;

        var root = new Grid();
        root.Children.Add(_track);
        root.Children.Add(_fill);
        root.Children.Add(_caption);

        Content = root;
        SizeChanged += (_, _) => Redraw();
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(BudgetRing),
        new PropertyMetadata(0.0, (d, _) => ((BudgetRing)d).Redraw()));

    /// <summary>How much of the ring is filled, 0..1.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Caption
    {
        get => _caption.Text;
        set => _caption.Text = value;
    }

    public void SetBrushes(Brush? track, Brush? fill)
    {
        _track.Stroke = track;
        _fill.Stroke = fill;
        _caption.Foreground = fill;
    }

    /// <summary>Eases to a new value instead of snapping, so the ring reads as something that moves.</summary>
    public void AnimateTo(double value)
    {
        var target = Math.Clamp(value, 0, 1);

        if (Motion.Reduced)
        {
            Value = target;
            return;
        }

        var animation = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(600),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        Storyboard.SetTarget(animation, this);
        Storyboard.SetTargetProperty(animation, "Value");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            storyboard.Stop();
            Value = target;
        };

        storyboard.Begin();
    }

    private void Redraw()
    {
        var size = Math.Min(ActualWidth, ActualHeight);

        if (size <= Thickness * 2)
        {
            return;
        }

        var radius = (size - Thickness) / 2;
        var centre = new Point(ActualWidth / 2, ActualHeight / 2);

        _track.Data = Arc(centre, radius, 1);
        _fill.Data = Arc(centre, radius, Math.Clamp(Value, 0, 1));
    }

    /// <summary>
    /// An arc starting at twelve o'clock and sweeping clockwise.
    ///
    /// A full circle can't be drawn as one arc segment — start and end land on
    /// the same point and it collapses to nothing — so it's drawn as two halves.
    /// </summary>
    private static Geometry Arc(Point centre, double radius, double fraction)
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(centre.X, centre.Y - radius),
            IsClosed = false,
            IsFilled = false,
        };

        if (fraction >= 1)
        {
            figure.Segments.Add(Segment(new Point(centre.X, centre.Y + radius), radius, false));
            figure.Segments.Add(Segment(new Point(centre.X, centre.Y - radius), radius, false));
        }
        else if (fraction > 0)
        {
            var angle = fraction * 2 * Math.PI;
            var end = new Point(
                centre.X + (radius * Math.Sin(angle)),
                centre.Y - (radius * Math.Cos(angle)));

            figure.Segments.Add(Segment(end, radius, fraction > 0.5));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static ArcSegment Segment(Point point, double radius, bool isLarge) => new()
    {
        Point = point,
        Size = new Size(radius, radius),
        SweepDirection = SweepDirection.Clockwise,
        IsLargeArc = isLarge,
    };
}
