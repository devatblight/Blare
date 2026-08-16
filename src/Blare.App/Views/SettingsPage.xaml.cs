using BLight.Blare.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BLight.Blare.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SafetyMonitor _safetyMonitor;
    private readonly BoostCoordinator _boostCoordinator;

    public SettingsPage()
    {
        _safetyMonitor = App.Services.GetRequiredService<SafetyMonitor>();
        _boostCoordinator = App.Services.GetRequiredService<BoostCoordinator>();

        InitializeComponent();

        UpdateToggleWarningsButton();
        UpdateRaiseCeilingButton();
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

        var dialog = new DisableWarningsDialog { XamlRoot = Content.XamlRoot };
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

        var dialog = new RaiseBoostCeilingDialog { XamlRoot = Content.XamlRoot };
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
