using Blight.Blare.App.Controls;
using Blight.Blare.App.Services;
using Blight.Blare.Core.Layout;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Blight.Blare.App.Views;

/// <summary>
/// Builds the content for each kind of dashboard card.
///
/// Split from the page's own logic so adding a card type is one method here and
/// one entry in the catalogue, rather than another branch threaded through the
/// page.
/// </summary>
public sealed partial class MixerPage
{
    public static string TitleFor(CardKind kind) => kind switch
    {
        CardKind.AppMixer => "Apps",
        CardKind.MasterOutput => "Master output",
        CardKind.OtherOutputs => "Other outputs",
        CardKind.DisplaySpeakers => "Display speakers",
        CardKind.HearingStatus => "Hearing",
        CardKind.QuickActions => "Quick actions",
        CardKind.NowPlaying => "Loudest right now",
        _ => kind.ToString(),
    };

    private UIElement BuildCardContent(CardKind kind, CardDensity density) => kind switch
    {
        CardKind.AppMixer => BuildAppMixerCard(),
        CardKind.MasterOutput => BuildMasterCard(density),
        CardKind.OtherOutputs => BuildOtherOutputsCard(),
        CardKind.DisplaySpeakers => BuildDisplaySpeakersCard(),
        CardKind.HearingStatus => BuildHearingCard(),
        CardKind.QuickActions => BuildQuickActionsCard(density),
        CardKind.NowPlaying => BuildNowPlayingCard(density),
        _ => new TextBlock { Text = "Unknown card" },
    };

    private UIElement BuildAppMixerCard()
    {
        _stripsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,

            // Strips arrive and leave as apps start and stop playing. The stock
            // transitions slide the neighbours along instead of having the row
            // jump, which is the whole reason a session appearing doesn't startle.
            ChildrenTransitions =
            [
                new AddDeleteThemeTransition(),
                new RepositionThemeTransition(),
            ],
        };

        _emptyState = BuildEmptyState();

        var scroller = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _stripsPanel,
        };

        var host = new Grid();
        host.Children.Add(scroller);
        host.Children.Add(_emptyState);
        return host;
    }

    /// <summary>
    /// Shown when no app is playing. A bare line of grey text reads like the
    /// panel failed to load; an icon and a second line make it clear this is a
    /// normal, quiet state rather than something being broken.
    /// </summary>
    private static UIElement BuildEmptyState()
    {
        var stack = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Opacity = 0,
        };

        stack.Children.Add(new FontIcon
        {
            Glyph = char.ConvertFromUtf32(0xE767),
            FontSize = 22,
            Opacity = 0.35,
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Nothing is playing",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.65,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Apps appear here as soon as they make a sound.",
            FontSize = 11.5,
            Opacity = 0.45,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        return stack;
    }

    private UIElement BuildMasterCard(CardDensity density)
    {
        _masterDeviceNameText = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        _masterVolumeText = new TextBlock
        {
            Text = "0%",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            MinWidth = 44,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _masterVolumeSlider = new Slider { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center };
        _masterVolumeSlider.ValueChanged += OnMasterVolumeChanged;

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = char.ConvertFromUtf32(0xE7F5),
            FontSize = 16,
            Foreground = CardBrush("BlareAccent"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(icon, 0);
        Grid.SetColumn(_masterVolumeSlider, 1);
        Grid.SetColumn(_masterVolumeText, 2);
        row.Children.Add(icon);
        row.Children.Add(_masterVolumeSlider);
        row.Children.Add(_masterVolumeText);

        var stack = new StackPanel { Spacing = 6 };

        // With one row to spare, the fader matters more than knowing which box
        // it drives — and the device name is on the Diagnostics page regardless.
        if (density != CardDensity.Compact)
        {
            stack.Children.Add(_masterDeviceNameText);
        }

        stack.Children.Add(row);

        RefreshMasterDevice();
        return stack;
    }

    private UIElement BuildOtherOutputsCard()
    {
        var stack = new StackPanel { Spacing = 6 };

        try
        {
            var devices = _deviceManager.GetRenderDevices().Where(d => !d.IsDefault).ToList();

            if (devices.Count == 0)
            {
                return new TextBlock { Text = "No other outputs.", Opacity = 0.5, FontSize = 12 };
            }

            foreach (var device in devices)
            {
                stack.Children.Add(BuildDeviceRow(
                    device.DisplayName,
                    _deviceManager.GetMasterVolume(device.DeviceId) * 100,
                    percent => _deviceManager.SetMasterVolume(device.DeviceId, (float)(percent / 100.0))));
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return new TextBlock { Text = "Could not read output devices.", Opacity = 0.5, FontSize = 12 };
        }

        return stack;
    }

    private UIElement BuildDisplaySpeakersCard()
    {
        try
        {
            // Read once rather than polled: DDC/CI is a slow serial channel and
            // hammering it makes monitors unresponsive.
            var controls = _monitorVolume.GetControls().Where(c => c.SupportsVolume).ToList();

            if (controls.Count == 0)
            {
                return new TextBlock
                {
                    Text = "No display reports a speaker volume.",
                    Opacity = 0.5,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                };
            }

            var stack = new StackPanel { Spacing = 6 };

            foreach (var control in controls)
            {
                stack.Children.Add(BuildDeviceRow(
                    control.DisplayName,
                    control.VolumePercent,
                    percent => _monitorVolume.TrySetVolumePercent(control.Index, percent)));
            }

            return stack;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return new TextBlock { Text = "Display speakers unavailable.", Opacity = 0.5, FontSize = 12 };
        }
    }

    private UIElement BuildHearingCard()
    {
        _warningCountText = new TextBlock { Text = "no warnings", FontSize = 12 };
        _exposureText = new TextBlock { Text = "0m loud today", FontSize = 12 };
        _warningDot = NewDot();
        _exposureDot = NewDot();

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(DotRow(_warningDot, _warningCountText));
        stack.Children.Add(DotRow(_exposureDot, _exposureText));

        UpdateStatusChips();
        return stack;
    }

    private UIElement BuildQuickActionsCard(CardDensity density)
    {
        var actions = new (string Label, int Glyph, Action Run)[]
        {
            ("Mute everything", 0xE74F, () => SetAllMuted(true)),
            ("Unmute everything", 0xE767, () => SetAllMuted(false)),
            ("Level everything to 100%", 0xE9E9, () => SetAllVolume(100)),
            ("Clear focus", 0xE711, () => CrashLog.FireAndForget(ReleaseFocusAsync())),
        };

        // Stacked labelled buttons need a row each, and the fourth was the one
        // that got cut. Short and wide, they become a row of icons instead —
        // every action still reachable, each with its label as a tooltip.
        if (density == CardDensity.Compact)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            foreach (var (label, glyph, run) in actions)
            {
                row.Children.Add(IconActionButton(label, glyph, run));
            }

            return row;
        }

        var stack = new StackPanel { Spacing = 4 };

        foreach (var (label, glyph, run) in actions)
        {
            stack.Children.Add(ActionButton(label, glyph, run));
        }

        return stack;
    }

    private UIElement BuildNowPlayingCard(CardDensity density)
    {
        _nowPlayingText = new TextBlock
        {
            Text = "Nothing playing",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        _nowPlayingDetail = new TextBlock { FontSize = 11.5, Opacity = 0.6 };

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(_nowPlayingText);

        if (density != CardDensity.Compact)
        {
            stack.Children.Add(_nowPlayingDetail);
        }

        return stack;
    }

    // ---- shared pieces -------------------------------------------------------

    private UIElement BuildDeviceRow(string name, double percent, Func<double, bool> apply) =>
        BuildDeviceRow(name, percent, p => { apply(p); });

    private UIElement BuildDeviceRow(string name, double percent, Action<double> apply)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var readout = new TextBlock
        {
            Text = $"{percent:F0}%",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            MinWidth = 42,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var slider = new Slider { Minimum = 0, Maximum = 100, Value = percent, Margin = new Thickness(0, -4, 0, -4) };
        slider.ValueChanged += (_, e) =>
        {
            readout.Text = $"{e.NewValue:F0}%";
            apply(e.NewValue);
        };

        var label = new TextBlock
        {
            Text = name,
            FontSize = 11,
            Opacity = 0.7,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var stack = new StackPanel();
        stack.Children.Add(label);
        stack.Children.Add(slider);

        Grid.SetColumn(stack, 0);
        Grid.SetColumn(readout, 1);
        grid.Children.Add(stack);
        grid.Children.Add(readout);
        return grid;
    }

    private static Ellipse NewDot() => new()
    {
        Width = 7,
        Height = 7,
        Fill = CardBrush("BlareMeterUnlit"),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static UIElement DotRow(UIElement dot, UIElement text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(dot);
        row.Children.Add(text);
        return row;
    }

    /// <summary>The same action as a square icon button, for cards too short to stack labelled rows.</summary>
    private static Button IconActionButton(string label, int glyph, Action action)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = char.ConvertFromUtf32(glyph), FontSize = 14 },
            Padding = new Thickness(9, 6, 9, 6),
        };

        ToolTipService.SetToolTip(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    private static Button ActionButton(string text, int glyph, Action action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        content.Children.Add(new FontIcon { Glyph = char.ConvertFromUtf32(glyph), FontSize = 12 });
        content.Children.Add(new TextBlock { Text = text, FontSize = 12 });

        var button = new Button
        {
            Content = content,
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };

        button.Click += (_, _) => action();
        return button;
    }

    private static Brush? CardBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;
}
