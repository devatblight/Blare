using BLight.Blare.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace BLight.Blare.App.Controls;

/// <summary>
/// One app's channel strip: icon, spectrum meter, vertical fader with a
/// visible danger zone above 100%, numeric readout and mute.
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

        StripBorder.BorderBrush = isFocused
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BlareAccent"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BlareStripBorder"];
    }

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

        UpdateVolumeText();
        UpdateDangerZone();
        UpdateBoostBadge();
    }

    public void SetLevels(ReadOnlySpan<double> levels) => Meter.SetLevels(levels);

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
            case nameof(SessionRowViewModel.IsBoosted):
                UpdateBoostBadge();
                break;
            case nameof(SessionRowViewModel.MaxVolumePercent):
                VolumeSlider.Maximum = _viewModel.MaxVolumePercent;
                UpdateDangerZone();
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
    }

    private void UpdateVolumeText()
    {
        if (_viewModel is null)
        {
            return;
        }

        VolumeText.Text = $"{_viewModel.VolumePercent:F0}%";
        VolumeText.Foreground = _viewModel.VolumePercent > 100
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BlareMeterHigh"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    }

    private void UpdateBoostBadge()
    {
        BoostBadge.Visibility = _viewModel?.IsBoosted == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDangerZone()
    {
        if (_viewModel is null)
        {
            return;
        }

        // The danger strip covers the top slice of fader travel — the part
        // that sits above 100% — so how far "too far" is, is visible before
        // you drag rather than only after.
        var max = Math.Max(1, _viewModel.MaxVolumePercent);
        var dangerFraction = Math.Clamp((max - 100) / max, 0, 1);

        DangerZone.Visibility = dangerFraction > 0 ? Visibility.Visible : Visibility.Collapsed;
        DangerZone.Height = dangerFraction * 150;
    }
}
