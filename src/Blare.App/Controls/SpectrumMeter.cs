using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace BLight.Blare.App.Controls;

/// <summary>
/// Draws live FFT band levels as a column of vertical bars, each bar built
/// from discrete LED-style segments so it reads like hardware metering
/// rather than a progress bar. Segment colour ramps green → amber → red up
/// the column, so a loud app is obvious at a glance without reading numbers.
///
/// Rebuilds only segment opacity per frame (not the visual tree), so this
/// stays cheap enough to run at animation rate for several apps at once.
/// </summary>
public sealed class SpectrumMeter : UserControl
{
    private const int SegmentsPerBar = 14;

    private Rectangle[,]? _segments;
    private int _barCount;

    public SpectrumMeter()
    {
        EnsureBuilt(BarCount);
    }

    public static readonly DependencyProperty BarCountProperty = DependencyProperty.Register(
        nameof(BarCount),
        typeof(int),
        typeof(SpectrumMeter),
        new PropertyMetadata(14, (d, e) => ((SpectrumMeter)d).EnsureBuilt((int)e.NewValue)));

    public int BarCount
    {
        get => (int)GetValue(BarCountProperty);
        set => SetValue(BarCountProperty, value);
    }

    /// <summary>Pushes one frame of band levels (each 0..1). Length may differ from <see cref="BarCount"/>; extra values are ignored.</summary>
    public void SetLevels(ReadOnlySpan<double> levels)
    {
        if (_segments is null)
        {
            EnsureBuilt(BarCount);
        }

        if (_segments is null)
        {
            return;
        }

        var bars = Math.Min(_barCount, levels.Length);

        for (var bar = 0; bar < bars; bar++)
        {
            var lit = (int)Math.Round(Math.Clamp(levels[bar], 0, 1) * SegmentsPerBar);

            for (var segment = 0; segment < SegmentsPerBar; segment++)
            {
                // Segment 0 is the bottom of the bar.
                _segments[bar, segment].Opacity = segment < lit ? 1.0 : 0.12;
            }
        }
    }

    private void EnsureBuilt(int barCount)
    {
        if (barCount <= 0 || (_segments is not null && _barCount == barCount))
        {
            return;
        }

        _barCount = barCount;
        var root = new Grid { ColumnSpacing = 2 };

        for (var i = 0; i < barCount; i++)
        {
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        _segments = new Rectangle[barCount, SegmentsPerBar];

        for (var bar = 0; bar < barCount; bar++)
        {
            var column = new Grid { RowSpacing = 2 };
            for (var i = 0; i < SegmentsPerBar; i++)
            {
                column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }

            for (var segment = 0; segment < SegmentsPerBar; segment++)
            {
                var rectangle = new Rectangle
                {
                    RadiusX = 1,
                    RadiusY = 1,
                    Fill = BrushForSegment(segment),
                    Opacity = 0.12,
                };

                // Row 0 is the top of a Grid, but segment 0 is the bottom of the bar.
                Grid.SetRow(rectangle, SegmentsPerBar - 1 - segment);
                column.Children.Add(rectangle);
                _segments[bar, segment] = rectangle;
            }

            Grid.SetColumn(column, bar);
            root.Children.Add(column);
        }

        Content = root;
    }

    private static Brush BrushForSegment(int segment)
    {
        var fraction = (double)segment / SegmentsPerBar;
        var key = fraction switch
        {
            > 0.85 => "BlareMeterHigh",
            > 0.65 => "BlareMeterMid",
            _ => "BlareMeterLow",
        };

        return Application.Current.Resources[key] as Brush
               ?? new SolidColorBrush(Microsoft.UI.Colors.LimeGreen);
    }
}
