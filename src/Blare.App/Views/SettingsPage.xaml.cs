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
    private bool _initializing = true;

    public SettingsPage()
    {
        _safetyMonitor = App.Services.GetRequiredService<SafetyMonitor>();
        _boostCoordinator = App.Services.GetRequiredService<BoostCoordinator>();
        _themeService = App.Services.GetRequiredService<ThemeService>();

        InitializeComponent();

        ThemeComboBox.SelectedIndex = _themeService.Current == BlareTheme.StudioDark ? 1 : 0;
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

        if (_boostCoordinator.CurrentCeilingPercent(now) >= BoostCoordinator.OverriddenCeilingPercent)
        {
            return; // already granted and not yet expired
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
        var allowed = _boostCoordinator.CurrentCeilingPercent(DateTimeOffset.UtcNow) >= BoostCoordinator.OverriddenCeilingPercent;
        RaiseCeilingButton.Content = allowed ? "Allowed" : "Allow up to 300%";
        RaiseCeilingButton.IsEnabled = !allowed;
    }
}
