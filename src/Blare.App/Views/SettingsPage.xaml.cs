using BLight.Blare.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BLight.Blare.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SafetyMonitor _safetyMonitor;
    private readonly BoostCoordinator _boostCoordinator;
    private readonly ThemeService _themeService;
    private readonly BackdropService _backdropService;
    private bool _initializing = true;

    public SettingsPage()
    {
        _safetyMonitor = App.Services.GetRequiredService<SafetyMonitor>();
        _boostCoordinator = App.Services.GetRequiredService<BoostCoordinator>();
        _themeService = App.Services.GetRequiredService<ThemeService>();
        _backdropService = App.Services.GetRequiredService<BackdropService>();

        InitializeComponent();

        ThemeComboBox.SelectedIndex = _themeService.Current == BlareTheme.StudioDark ? 1 : 0;
        BackdropComboBox.SelectedIndex = (int)_backdropService.Requested;

        // Say so rather than silently substituting when the OS can't do Mica.
        if (!BackdropService.MicaSupported)
        {
            BackdropCard.Description = "Mica needs Windows 11 — Acrylic is used instead on this system.";
        }

        _initializing = false;

        UpdateToggleWarningsButton();
        UpdateRaiseCeilingButton();
    }

    private async void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ThemeComboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        if (Enum.TryParse<BlareTheme>(tag, out var theme))
        {
            await _themeService.SetAsync(theme);
        }
    }

    private async void OnBackdropChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || BackdropComboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        if (Enum.TryParse<BackdropKind>(tag, out var kind))
        {
            await _backdropService.SetAsync(kind);
        }
    }

    private async void OnToggleWarningsClick(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;

        if (_safetyMonitor.WarningsDisabled(now))
        {
            _safetyMonitor.ReenableWarnings();
            UpdateToggleWarningsButton();
            return;
        }

        var dialog = new DisableWarningsDialog { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            _safetyMonitor.DisableWarnings(now);
        }

        UpdateToggleWarningsButton();
    }

    private void UpdateToggleWarningsButton()
    {
        ToggleWarningsButton.Content = _safetyMonitor.WarningsDisabled(DateTimeOffset.UtcNow)
            ? "Re-enable"
            : "Disable";
    }

    private async void OnRaiseCeilingClick(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;

        // Going back to the safe ceiling is always allowed without ceremony.
        if (_boostCoordinator.CurrentCeilingPercent(now) >= BoostCoordinator.OverriddenCeilingPercent)
        {
            _boostCoordinator.RevokeCeilingOverride();
            UpdateRaiseCeilingButton();
            return;
        }

        var dialog = new RaiseBoostCeilingDialog { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            _boostCoordinator.GrantCeilingOverride(now);
            UpdateRaiseCeilingButton();
        }
    }

    private void UpdateRaiseCeilingButton()
    {
        // Don't offer a ceiling control while boost can't do anything — an
        // enabled-looking button that changes nothing is worse than an honest
        // disabled one.
        if (!BoostCoordinator.BoostAvailable)
        {
            RaiseCeilingButton.Content = "Unavailable";
            RaiseCeilingButton.IsEnabled = false;
            BoostCeilingCard.Description =
                "Boost above 100% is currently unavailable: Windows applies per-app volume before Blare can capture the audio, so an app can't be silenced and re-amplified. Use Focus on a channel strip to make one app dominant instead.";
            return;
        }

        var allowed = _boostCoordinator.CurrentCeilingPercent(DateTimeOffset.UtcNow) >= BoostCoordinator.OverriddenCeilingPercent;
        RaiseCeilingButton.Content = allowed ? "Back to 150% limit" : "Allow up to 300%";
    }
}
