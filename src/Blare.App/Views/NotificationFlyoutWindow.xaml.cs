using Blight.Blare.App.Services;
using Blight.Blare.Core.Settings;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Blight.Blare.App.Views;

public enum FlyoutTone
{
    Neutral,
    Caution,
    Danger,
}

/// <summary>
/// The window every Blare message appears in.
///
/// Behaves like a system flyout rather than an app window: it never takes
/// focus, sits above everything, stays out of Alt-Tab and the taskbar, and
/// slides in with a cubic ease-out. Hovering it keeps it on screen so a
/// message with an action can't time out while being read.
/// </summary>
public sealed partial class NotificationFlyoutWindow : Window
{
    private const int FlyoutWidth = 400;
    private const int MinimumFlyoutHeight = 76;

    /// <summary>The message is capped at three lines, so anything taller than this means the measure went wrong.</summary>
    private const int MaximumFlyoutHeight = 220;

    private const double SlideDistance = 14;

    /// <summary>Set from the measured content each time a message is shown — a fixed height clips longer messages.</summary>
    private int _flyoutHeight = MinimumFlyoutHeight;

    private readonly DispatcherQueueTimer _dismissTimer;
    private DesktopAcrylicController? _backdropController;
    private SystemBackdropConfiguration? _backdropConfiguration;

    private Action? _action;
    private bool _pointerInside;
    private TimeSpan _duration = TimeSpan.FromSeconds(6);

    public NotificationFlyoutWindow()
    {
        InitializeComponent();

        Title = "Blare";
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
        FlyoutWindowNative.MakePassive(handle);
        FlyoutWindowNative.RemoveSystemFrame(handle);

        RootHost.PointerEntered += OnPointerEntered;
        RootHost.PointerExited += OnPointerExited;

        AppWindow.Resize(new Windows.Graphics.SizeInt32(FlyoutWidth, MinimumFlyoutHeight));

        // Park it off-screen so it is never seen at the origin before being placed.
        AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
        AppWindow.Hide();

        _dismissTimer = DispatcherQueue.CreateTimer();
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();

            // Don't snatch a message away from someone reading it.
            if (_pointerInside)
            {
                _dismissTimer.Interval = TimeSpan.FromSeconds(1.5);
                _dismissTimer.Start();
                return;
            }

            BeginDismiss();
        };
    }

    public void ShowMessage(
        string title,
        string message,
        FlyoutPosition position,
        FlyoutTone tone,
        TimeSpan duration,
        string? actionLabel = null,
        Action? action = null)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        AccentStripe.Background = ToneBrush(tone);

        _action = action;
        _duration = duration;
        ActionButton.Content = actionLabel ?? string.Empty;
        ActionButton.Visibility = actionLabel is null ? Visibility.Collapsed : Visibility.Visible;

        ResizeToContent();
        MoveTo(position);

        AppWindow.Show(activateWindow: false);
        FlyoutWindowNative.BringToTop(WindowNative.GetWindowHandle(this));

        // Slide in from whichever edge the flyout is anchored to.
        CardTransform.Y = position.Row() == 2 ? SlideDistance : -SlideDistance;
        EnterStoryboard.Begin();

        // DWM resets frame attributes when a window is shown, so the border has
        // to be suppressed again on every appearance, not just at construction.
        FlyoutWindowNative.RemoveSystemFrame(WindowNative.GetWindowHandle(this));

        _dismissTimer.Stop();
        _dismissTimer.Interval = duration;
        _dismissTimer.Start();
    }

    /// <summary>
    /// Grows the window to fit the message.
    ///
    /// XAML measures in device-independent units while AppWindow sizes in
    /// physical pixels, so the measured height has to be scaled — skipping that
    /// step is why text clips on a display at anything other than 100%.
    /// </summary>
    private void ResizeToContent()
    {
        var scale = Content?.XamlRoot?.RasterizationScale ?? 1.0;
        var availableWidth = FlyoutWidth / scale;

        // Measure against a fresh layout pass. Measuring straight after setting
        // the text returns the previous message's size, which is what was
        // clipping longer messages.
        RootHost.InvalidateMeasure();
        RootHost.Measure(new Windows.Foundation.Size(availableWidth, double.PositiveInfinity));
        RootHost.UpdateLayout();
        RootHost.Measure(new Windows.Foundation.Size(availableWidth, double.PositiveInfinity));

        var measured = (int)Math.Ceiling(RootHost.DesiredSize.Height * scale);
        _flyoutHeight = Math.Clamp(measured, MinimumFlyoutHeight, MaximumFlyoutHeight);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(FlyoutWidth, _flyoutHeight));
    }

    public void MoveTo(FlyoutPosition position)
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var work = area.WorkArea;

        var (x, y) = position.Locate(
            work.X, work.Y, work.Width, work.Height, FlyoutWidth, _flyoutHeight);

        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    public void Dismiss()
    {
        _dismissTimer.Stop();
        BeginDismiss();
    }

    private void BeginDismiss()
    {
        ExitStoryboard.Completed += OnExitCompleted;
        ExitStoryboard.Begin();
    }

    private void OnExitCompleted(object? sender, object e)
    {
        ExitStoryboard.Completed -= OnExitCompleted;
        AppWindow.Hide();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => _pointerInside = true;

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = false;

        // Restart a short countdown once the pointer leaves.
        _dismissTimer.Stop();
        _dismissTimer.Interval = TimeSpan.FromSeconds(Math.Min(2, _duration.TotalSeconds));
        _dismissTimer.Start();
    }

    private void OnActionClicked(object sender, RoutedEventArgs e)
    {
        var action = _action;
        Dismiss();
        action?.Invoke();
    }

    private void TrySetBackdrop()
    {
        // Acrylic rather than Mica: a small transient surface reads better with
        // the stronger blur, and it works on Windows 10 too.
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

    private static Brush? ToneBrush(FlyoutTone tone)
    {
        var key = tone switch
        {
            FlyoutTone.Danger => "BlareMeterHigh",
            FlyoutTone.Caution => "BlareMeterMid",
            _ => "BlareAccent",
        };

        return Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;
    }
}
