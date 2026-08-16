using BLight.Blare.App.Services;
using BLight.Blare.Audio.Sessions;
using BLight.Blare.Core.Safety;
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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = Services.GetRequiredService<MainWindow>();
        _mainWindow.Activate();

        _trayIcon = Services.GetRequiredService<TrayIconService>();
        _trayIcon.OpenRequested += (_, _) => _mainWindow.Activate();
        _trayIcon.ExitRequested += (_, _) =>
        {
            _trayIcon.Dispose();
            Exit();
        };
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<AudioSessionManager>();
        services.AddSingleton<LoudnessTracker>();
        services.AddSingleton<ConsentState>();
        services.AddSingleton<TrayIconService>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
