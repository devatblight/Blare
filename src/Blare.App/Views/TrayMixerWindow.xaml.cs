using System.Diagnostics;
using Blight.Blare.App.Services;
using Blight.Blare.Audio.Devices;
using Blight.Blare.Audio.Sessions;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WinRT.Interop;

namespace Blight.Blare.App.Views;

/// <summary>
/// The compact mixer that opens from the tray.
///
/// This is the surface Blare is actually used from. Opening a full window to
/// turn one app down is more work than opening the Windows mixer, so the
/// per-app faders have to be reachable in one click from the tray or the rest
/// of the product goes unused.
///
/// Unlike the notification flyout this one takes focus, because it is something
/// you interact with rather than read — and taking focus is what lets clicking
/// away dismiss it.
/// </summary>
public sealed partial class TrayMixerWindow : Window
{
    private const double CardWidth = 320;
    private const int EdgeMargin = 12;

    /// <summary>Enough for the master plus a handful of apps before it scrolls.</summary>
    private const double MaximumCardHeight = 460;

    private readonly AudioSessionManager _sessions;
    private readonly AudioDeviceManager _devices;
    private readonly VolumeCoordinator _volume;
    private readonly IconResolver _icons = new();

    private DesktopAcrylicController? _backdropController;
    private SystemBackdropConfiguration? _backdropConfiguration;

    private string? _defaultDeviceId;
    private bool _suppressMasterPush;
    private bool _isOpen;

    public TrayMixerWindow(AudioSessionManager sessions, AudioDeviceManager devices, VolumeCoordinator volume)
    {
        _sessions = sessions;
        _devices = devices;
        _volume = volume;

        InitializeComponent();

        Title = "Blare mixer";
        ExtendsContentIntoTitleBar = true;

        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        AppWindow.IsShownInSwitchers = false;

        TrySetBackdrop();

        var handle = WindowNative.GetWindowHandle(this);
        FlyoutWindowNative.RemoveSystemFrame(handle);

        AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
        AppWindow.Hide();

        // Clicking anywhere else puts it away, the way the system volume flyout
        // behaves. Without this it would sit on top of everything forever.
        Activated += (_, args) =>
        {
            if (_isOpen && args.WindowActivationState == WindowActivationState.Deactivated)
            {
                Hide();
            }
        };
    }

    public void Toggle()
    {
        if (_isOpen)
        {
            Hide();
            return;
        }

        Show();
    }

    public void Show()
    {
        RefreshMaster();
        RefreshApps();

        var scale = FlyoutWindowNative.ScaleFor(WindowNative.GetWindowHandle(this));

        RootHost.InvalidateMeasure();
        RootHost.Measure(new Windows.Foundation.Size(CardWidth, double.PositiveInfinity));
        RootHost.UpdateLayout();
        RootHost.Measure(new Windows.Foundation.Size(CardWidth, double.PositiveInfinity));

        var height = Math.Clamp(RootHost.DesiredSize.Height, 120, MaximumCardHeight);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)Math.Ceiling(CardWidth * scale),
            (int)Math.Ceiling(height * scale)));

        MoveNearTray();

        _isOpen = true;
        AppWindow.Show();
        FlyoutWindowNative.RemoveSystemFrame(WindowNative.GetWindowHandle(this));

        CardTransform.Y = 12;
        Animate(1, 0);
    }

    public void Hide()
    {
        _isOpen = false;
        AppWindow.Hide();
    }

    /// <summary>
    /// Anchors the card to the corner the tray lives in.
    ///
    /// Derived from the work area rather than assuming bottom-right: a taskbar
    /// moved to the top or side would otherwise put the mixer across the screen
    /// from the icon that opened it.
    /// </summary>
    private void MoveNearTray()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var work = area.WorkArea;
        var outer = area.OuterBounds;

        var width = AppWindow.Size.Width;
        var height = AppWindow.Size.Height;

        // The taskbar is wherever the work area falls short of the screen.
        var taskbarAtTop = work.Y > outer.Y;

        var x = work.X + work.Width - width - EdgeMargin;
        var y = taskbarAtTop
            ? work.Y + EdgeMargin
            : work.Y + work.Height - height - EdgeMargin;

        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private void RefreshMaster()
    {
        var defaultDevice = _devices.GetRenderDevices().FirstOrDefault(device => device.IsDefault);

        if (defaultDevice is null)
        {
            DeviceNameText.Text = "No output device";
            return;
        }

        _defaultDeviceId = defaultDevice.DeviceId;
        DeviceNameText.Text = defaultDevice.DisplayName;

        _suppressMasterPush = true;
        MasterSlider.Value = Math.Round(_devices.GetMasterVolume(defaultDevice.DeviceId) * 100);
        _suppressMasterPush = false;

        MasterReadout.Text = $"{MasterSlider.Value:F0}%";
    }

    private void OnMasterChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        MasterReadout.Text = $"{e.NewValue:F0}%";

        if (_suppressMasterPush || _defaultDeviceId is null)
        {
            return;
        }

        _devices.SetMasterVolume(_defaultDeviceId, (float)(e.NewValue / 100.0));
    }

    /// <summary>
    /// Rebuilds the app rows from whatever is playing right now.
    ///
    /// Rebuilt on open rather than kept live: the flyout is on screen for a few
    /// seconds at a time, and a rebuild is far simpler than reconciling rows
    /// while the user is dragging one of them.
    /// </summary>
    private void RefreshApps()
    {
        AppRows.Children.Clear();

        var ownProcessId = (uint)Environment.ProcessId;

        // Grouped by executable so a browser is one fader, matching the desk.
        var groups = _sessions.GetSessionsForDefaultDevice()
            .Where(session => !session.IsSystemSoundsSession && session.ProcessId != ownProcessId)
            .Select(session => (Session: session, Path: PathFor(session.ProcessId)))
            .GroupBy(entry => string.IsNullOrEmpty(entry.Path) ? $"pid:{entry.Session.ProcessId}" : entry.Path.ToLowerInvariant())
            .ToList();

        if (groups.Count == 0)
        {
            AppRows.Children.Add(new TextBlock
            {
                Text = "Nothing is playing.",
                FontSize = 12,
                Opacity = 0.5,
            });

            return;
        }

        foreach (var group in groups)
        {
            AppRows.Children.Add(BuildRow(group.Select(entry => entry.Session).ToList(), group.First().Path));
        }
    }

    private UIElement BuildRow(IReadOnlyList<AudioSessionInfo> sessions, string executablePath)
    {
        var first = sessions[0];

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Image { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };

        if (!string.IsNullOrEmpty(executablePath))
        {
            CrashLog.FireAndForget(SetIconAsync(icon, executablePath));
        }

        var readout = new TextBlock
        {
            Text = $"{first.Volume * 100:F0}%",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            MinWidth = 40,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = VolumeCoordinator.MaximumPercent,
            Value = Math.Round(first.Volume * 100),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -6, 0, -6),
        };

        slider.ValueChanged += (_, e) =>
        {
            readout.Text = $"{e.NewValue:F0}%";

            foreach (var session in sessions)
            {
                _volume.SetVolumePercent(session.ProcessId, e.NewValue);
                _volume.SetMute(session.ProcessId, false);
            }
        };

        var name = new TextBlock
        {
            Text = NameFor(first, executablePath),
            FontSize = 11.5,
            Opacity = 0.75,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var stack = new StackPanel();
        stack.Children.Add(name);
        stack.Children.Add(slider);

        Grid.SetColumn(icon, 0);
        Grid.SetColumn(stack, 1);
        Grid.SetColumn(readout, 2);
        grid.Children.Add(icon);
        grid.Children.Add(stack);
        grid.Children.Add(readout);

        return grid;
    }

    private async Task SetIconAsync(Image image, string executablePath)
    {
        if (await _icons.ResolveAsync(executablePath) is { } source)
        {
            image.Source = source;
        }
    }

    private void Animate(double opacity, double offset)
    {
        var storyboard = new Storyboard();

        storyboard.Children.Add(Fade(RootHost, "Opacity", opacity));
        storyboard.Children.Add(Fade(CardTransform, "Y", offset));
        storyboard.Begin();

        static DoubleAnimation Fade(DependencyObject target, string property, double to)
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };

            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            return animation;
        }
    }

    private void TrySetBackdrop()
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            return;
        }

        _backdropConfiguration = new SystemBackdropConfiguration { IsInputActive = true };
        _backdropController = new DesktopAcrylicController();
        _backdropController.AddSystemBackdropTarget(
            WinRT.CastExtensions.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>(this));
        _backdropController.SetSystemBackdropConfiguration(_backdropConfiguration);
    }

    private static string NameFor(AudioSessionInfo session, string executablePath)
    {
        if (!string.IsNullOrWhiteSpace(session.DisplayName))
        {
            return session.DisplayName;
        }

        return string.IsNullOrEmpty(executablePath)
            ? $"pid {session.ProcessId}"
            : Path.GetFileNameWithoutExtension(executablePath);
    }

    private static string PathFor(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }
}
