using Blight.Blare.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Blight.Blare.App.Controls;

/// <summary>
/// One app's channel strip: icon, spectrum meter, vertical fader, numeric
/// readout, mute and focus.
///
/// Wired to its view model by hand rather than by x:Bind because the strip
/// is created per session at runtime and its fader has to push changes back
/// without echoing them straight back into the control.
/// </summary>
public sealed partial class ChannelStrip : UserControl
{
    // Segoe MDL2 Assets.
    private const string SpeakerGlyph = "";
    private const string MutedGlyph = "";

    /// <summary>Barely-there lift on hover — enough to show the strip is live, not enough to strobe while the pointer crosses the desk.</summary>
    private const double HoverOpacity = 0.06;

    private const double MutedOpacity = 0.4;

    /// <summary>Percentage points per wheel notch.</summary>
    private const double WheelStep = 2;

    private SessionRowViewModel? _viewModel;
    private bool _suppressPush;

    public ChannelStrip()
    {
        InitializeComponent();
        BuildContextMenu();
    }

    public SessionRowViewModel? ViewModel => _viewModel;

    /// <summary>Raised when the user asks for this app to be the focused one.</summary>
    public event EventHandler<string>? FocusRequested;

    /// <summary>Raised when the user asks for everything except this app to be muted.</summary>
    public event EventHandler<string>? SoloRequested;

    /// <summary>Raised with a ceiling this app should never be set above, or null to remove one.</summary>
    public event EventHandler<double?>? LimitRequested;

    /// <summary>
    /// Right-click menu for the things that don't earn a permanent button.
    ///
    /// Built here rather than in XAML because every item needs the view model,
    /// which arrives after construction.
    /// </summary>
    private void BuildContextMenu()
    {
        var menu = new MenuFlyout();

        var solo = new MenuFlyoutItem { Text = "Solo — mute everything else" };
        solo.Click += (_, _) =>
        {
            if (_viewModel is not null)
            {
                SoloRequested?.Invoke(this, _viewModel.AppKey);
            }
        };

        var reset = new MenuFlyoutItem { Text = "Reset to 100%" };
        reset.Click += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.VolumePercent = 100;
            }
        };

        menu.Items.Add(solo);
        menu.Items.Add(reset);
        menu.Items.Add(new MenuFlyoutSeparator());

        // A ceiling is a rule, not a level: it survives the fader being dragged
        // and the app being restarted.
        foreach (var ceiling in new double[] { 25, 50, 75 })
        {
            var item = new MenuFlyoutItem { Text = $"Never above {ceiling:F0}%" };
            item.Click += (_, _) => LimitRequested?.Invoke(this, ceiling);
            menu.Items.Add(item);
        }

        var clear = new MenuFlyoutItem { Text = "Remove limit" };
        clear.Click += (_, _) => LimitRequested?.Invoke(this, null);
        menu.Items.Add(clear);

        ContextFlyout = menu;
    }

    public void SetFocused(bool isFocused)
    {
        _suppressPush = true;
        FocusButton.IsChecked = isFocused;
        _suppressPush = false;

        Motion.FadeTo(FocusRing, isFocused ? 1 : 0, Motion.Normal);
    }

    /// <summary>Wheel anywhere over the strip moves its fader, the way a real desk's would.</summary>
    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;

        if (delta == 0)
        {
            return;
        }

        _viewModel.VolumePercent = Math.Clamp(
            _viewModel.VolumePercent + (Math.Sign(delta) * WheelStep), 0, _viewModel.MaxVolumePercent);

        // Otherwise the card's scroll viewer takes the gesture as well and the
        // desk scrolls sideways while the fader moves.
        e.Handled = true;
    }

    private void OnLevelFlyoutOpening(object? sender, object e)
    {
        if (_viewModel is not null)
        {
            LevelInput.Maximum = _viewModel.MaxVolumePercent;
            LevelInput.Value = Math.Round(_viewModel.VolumePercent);
        }
    }

    private void OnLevelTyped(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        // NaN arrives when the box is cleared mid-edit.
        if (_viewModel is null || double.IsNaN(args.NewValue))
        {
            return;
        }

        _viewModel.VolumePercent = Math.Clamp(args.NewValue, 0, _viewModel.MaxVolumePercent);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) =>
        Motion.FadeTo(HoverOverlay, HoverOpacity);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) =>
        Motion.FadeTo(HoverOverlay, 0);

    private void OnFocusClicked(object sender, RoutedEventArgs e)
    {
        if (_suppressPush || _viewModel is null)
        {
            return;
        }

        FocusRequested?.Invoke(this, _viewModel.AppKey);
    }

    public void Bind(SessionRowViewModel viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        AppNameText.Text = viewModel.DisplayName;
        AppIcon.Source = viewModel.Icon;

        _suppressPush = true;
        VolumeSlider.Maximum = viewModel.MaxVolumePercent;
        VolumeSlider.Value = viewModel.VolumePercent;
        _suppressPush = false;

        MuteButton.IsChecked = viewModel.IsMuted;
        MuteIcon.Glyph = viewModel.IsMuted ? MutedGlyph : SpeakerGlyph;

        // Set outright on bind — a strip that fades in from muted on first paint
        // looks like something changed when nothing did.
        var muted = viewModel.IsMuted ? MutedOpacity : 1;
        SignalArea.Opacity = muted;
        VolumeText.Opacity = muted;

        UpdateVolumeText();
    }

    /// <summary>
    /// Lays the strip out for the room it has.
    ///
    /// Each band drops the least important thing rather than scaling everything
    /// down: a fader squeezed to forty pixels is not a smaller fader, it is an
    /// unusable one. The fader and the app's identity survive every band because
    /// without them the strip has no purpose.
    /// </summary>
    public void SetDensity(CardDensity density)
    {
        switch (density)
        {
            case CardDensity.Compact:
                StripRoot.Width = 76;
                MeterHost.Visibility = Visibility.Collapsed;
                ButtonsRow.Visibility = Visibility.Collapsed;
                SignalArea.MinHeight = 64;
                break;

            case CardDensity.Normal:
                StripRoot.Width = 96;
                MeterHost.Visibility = Visibility.Visible;
                ButtonsRow.Visibility = Visibility.Visible;
                SignalArea.MinHeight = 88;
                break;

            default:
                StripRoot.Width = 104;
                MeterHost.Visibility = Visibility.Visible;
                ButtonsRow.Visibility = Visibility.Visible;
                SignalArea.MinHeight = 150;
                break;
        }
    }

    public void SetLevels(ReadOnlySpan<double> levels) => Meter.SetLevels(levels);

    /// <summary>Advances the meter with no new data so it falls away instead of freezing.</summary>
    public void DecayLevels() => Meter.Decay();

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(SessionRowViewModel.Icon):
                AppIcon.Source = _viewModel.Icon;
                break;
            case nameof(SessionRowViewModel.MaxVolumePercent):
                VolumeSlider.Maximum = _viewModel.MaxVolumePercent;
                break;
            case nameof(SessionRowViewModel.VolumePercent):
                _suppressPush = true;
                VolumeSlider.Value = _viewModel.VolumePercent;
                _suppressPush = false;
                UpdateVolumeText();
                break;
            case nameof(SessionRowViewModel.IsMuted):
                _suppressPush = true;
                MuteButton.IsChecked = _viewModel.IsMuted;
                MuteIcon.Glyph = _viewModel.IsMuted ? MutedGlyph : SpeakerGlyph;
                _suppressPush = false;
                UpdateMutedLook();
                break;
        }
    }

    private void OnVolumeSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressPush || _viewModel is null)
        {
            return;
        }

        _viewModel.VolumePercent = e.NewValue;
        UpdateVolumeText();
    }

    private void OnMuteToggled(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _suppressPush)
        {
            return;
        }

        _viewModel.IsMuted = MuteButton.IsChecked == true;
        MuteIcon.Glyph = _viewModel.IsMuted ? MutedGlyph : SpeakerGlyph;
        UpdateMutedLook();
    }

    private void UpdateMutedLook()
    {
        var target = _viewModel?.IsMuted == true ? MutedOpacity : 1;

        Motion.FadeTo(SignalArea, target, Motion.Normal);
        Motion.FadeTo(VolumeText, target, Motion.Normal);
    }

    private void UpdateVolumeText()
    {
        if (_viewModel is null)
        {
            return;
        }

        VolumeText.Text = $"{_viewModel.VolumePercent:F0}%";
    }
}
