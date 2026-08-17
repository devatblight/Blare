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

    private SessionRowViewModel? _viewModel;
    private bool _suppressPush;

    public ChannelStrip()
    {
        InitializeComponent();
    }

    public SessionRowViewModel? ViewModel => _viewModel;

    /// <summary>Raised when the user asks for this app to be the focused one.</summary>
    public event EventHandler<string>? FocusRequested;

    public void SetFocused(bool isFocused)
    {
        _suppressPush = true;
        FocusButton.IsChecked = isFocused;
        _suppressPush = false;

        Motion.FadeTo(FocusRing, isFocused ? 1 : 0, Motion.Normal);
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
