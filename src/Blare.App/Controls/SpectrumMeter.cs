using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Blight.Blare.App.Controls;

/// <summary>
/// Draws live FFT band levels as a column of vertical bars, each bar built
/// from discrete LED-style segments so it reads like hardware metering
/// rather than a progress bar. Segment colour ramps green → amber → red up
/// the column, so a loud app is obvious at a glance without reading numbers.
///
/// Two things make it read like a real meter rather than flickering noise:
/// levels rise instantly but fall gradually, and each bar keeps a peak marker
/// that hangs at the loudest recent value and then drops. Raw FFT output
/// jitters far too fast for an eye to follow without both.
///
/// Only segments whose state actually changed are touched each frame, so this
/// stays cheap enough to run for several apps at animation rate.
/// </summary>
public sealed class SpectrumMeter : UserControl
{
    private const int SegmentsPerBar = 14;

    /// <summary>Fraction of the remaining distance a falling bar covers per frame.</summary>
    private const double Release = 0.28;

    /// <summary>How far the peak marker slides per frame once it starts falling.</summary>
    private const double PeakFall = 0.012;

    /// <summary>Frames a peak marker hangs at a new high before it begins to fall.</summary>
    private const int PeakHoldFrames = 14;

    private Rectangle[,]? _segments;
    private Brush[]? _segmentBrushes;
    private double[]? _levels;
    private double[]? _peaks;
    private int[]? _peakHold;
    private int[]? _drawnLit;
    private int[]? _drawnPeak;
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
        if (!TryPrepare())
        {
            return;
        }

        var bars = Math.Min(_barCount, levels.Length);

        for (var bar = 0; bar < bars; bar++)
        {
            Advance(bar, Math.Clamp(levels[bar], 0, 1));
        }

        Draw();
    }

    /// <summary>
    /// Advances one frame with no new data. Without this a meter freezes at its
    /// last value the moment an app stops feeding it, which looks like audio is
    /// still playing.
    /// </summary>
    public void Decay()
    {
        if (!TryPrepare())
        {
            return;
        }

        for (var bar = 0; bar < _barCount; bar++)
        {
            Advance(bar, 0);
        }

        Draw();
    }

    private void Advance(int bar, double target)
    {
        var levels = _levels!;
        var peaks = _peaks!;
        var hold = _peakHold!;

        // Attack is instant, release is gradual — the standard meter ballistics.
        // A level that fell as fast as it rose reads as flicker, not loudness.
        levels[bar] = target >= levels[bar]
            ? target
            : levels[bar] + ((target - levels[bar]) * Release);

        if (levels[bar] >= peaks[bar])
        {
            peaks[bar] = levels[bar];
            hold[bar] = PeakHoldFrames;
            return;
        }

        if (hold[bar] > 0)
        {
            hold[bar]--;
            return;
        }

        peaks[bar] = Math.Max(levels[bar], peaks[bar] - PeakFall);
    }

    private void Draw()
    {
        var segments = _segments!;
        var levels = _levels!;
        var peaks = _peaks!;
        var drawnLit = _drawnLit!;
        var drawnPeak = _drawnPeak!;

        for (var bar = 0; bar < _barCount; bar++)
        {
            var lit = (int)Math.Round(levels[bar] * SegmentsPerBar);
            var peak = peaks[bar] <= 0.001 ? -1 : Math.Clamp((int)(peaks[bar] * SegmentsPerBar), 0, SegmentsPerBar - 1);

            if (lit == drawnLit[bar] && peak == drawnPeak[bar])
            {
                continue;
            }

            for (var segment = 0; segment < SegmentsPerBar; segment++)
            {
                var wasLit = segment < drawnLit[bar] || segment == drawnPeak[bar];
                var isLit = segment < lit || segment == peak;

                if (wasLit == isLit)
                {
                    continue;
                }

                var rectangle = segments[bar, segment];

                // Unlit segments switch to a neutral fill rather than a faded
                // version of their lit colour — otherwise an idle app shows a
                // distracting ghost of the full green/amber/red ladder.
                rectangle.Fill = isLit ? _segmentBrushes![segment] : UnlitBrush;
                rectangle.Opacity = isLit ? 1.0 : 0.3;
            }

            drawnLit[bar] = lit;
            drawnPeak[bar] = peak;
        }
    }

    private bool TryPrepare()
    {
        if (_segments is null)
        {
            EnsureBuilt(BarCount);
        }

        return _segments is not null;
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
        _levels = new double[barCount];
        _peaks = new double[barCount];
        _peakHold = new int[barCount];

        // -1 rather than 0: 0 is a real state, and starting there would skip the
        // first draw of a bar that is genuinely silent.
        _drawnLit = new int[barCount];
        _drawnPeak = new int[barCount];
        Array.Fill(_drawnLit, -1);
        Array.Fill(_drawnPeak, -1);

        // Resolved once per build rather than per segment per frame.
        _segmentBrushes = new Brush[SegmentsPerBar];
        for (var segment = 0; segment < SegmentsPerBar; segment++)
        {
            _segmentBrushes[segment] = BrushForSegment(segment);
        }

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
                    Fill = UnlitBrush,
                    Opacity = 0.3,
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

    private static Brush UnlitBrush =>
        Application.Current.Resources["BlareMeterUnlit"] as Brush
        ?? new SolidColorBrush(Microsoft.UI.Colors.DimGray);

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
