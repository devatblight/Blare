using BLight.Blare.App.Services;
using BLight.Blare.Audio.Analysis;
using BLight.Blare.Audio.Devices;
using BLight.Blare.Audio.Sessions;
using BLight.Blare.Core.Safety;
using BLight.Blare.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace BLight.Blare.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private Window? _mainWindow;
    private TrayIconService? _trayIcon;

    public App()
    {
        InitializeComponent();
        Services = BuildServiceProvider();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Theme resources must exist before any page that references them loads.
        await Services.GetRequiredService<ThemeService>().LoadAsync();

        _mainWindow = Services.GetRequiredService<MainWindow>();
        _mainWindow.Activate();

        _trayIcon = Services.GetRequiredService<TrayIconService>();
        _trayIcon.OpenRequested += (_, _) => _mainWindow.Activate();
        _trayIcon.ExitRequested += async (_, _) =>
        {
            await Services.GetRequiredService<BoostCoordinator>().StopAllAsync();
            _trayIcon.Dispose();
            Exit();
        };
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BLight",
            "Blare");

        services.AddSingleton<AudioSessionManager>();
        services.AddSingleton<AudioDeviceManager>();
        services.AddSingleton<LoudnessTracker>();
        services.AddSingleton<ConsentState>();
        services.AddSingleton<SafetyMonitor>();
        services.AddSingleton<BoostCoordinator>();
        services.AddSingleton<SpectrumMonitor>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<ISettingsStore>(new JsonFileSettingsStore(settingsDirectory));
        services.AddSingleton<SessionVolumeStore>();
        services.AddSingleton<ThemeService>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
