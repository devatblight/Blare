using Blight.Blare.App.Services;
using Blight.Blare.App.Views;
using Blight.Blare.Audio.Analysis;
using Blight.Blare.Audio.Devices;
using Blight.Blare.Audio.Sessions;
using Blight.Blare.Core.Layout;
using Blight.Blare.Core.Safety;
using Blight.Blare.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Blight.Blare.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private Window? _mainWindow;
    private TrayIconService? _trayIcon;

    public App()
    {
        InitializeComponent();
        Services = BuildServiceProvider();

        // A crash with no trace is the worst possible outcome for a tray app —
        // the window just vanishes. Record it where Diagnostics can point at it.
        UnhandledException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            e.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                CrashLog.Write(exception);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) => CrashLog.Write(e.Exception);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Checked once at startup rather than per animation: an app about
        // hearing has no business ignoring the system's accessibility settings.
        Controls.Motion.Reduced = !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

        // Theme resources must exist before any page that references them loads,
        // and consent must be restored before anything can read a safety state.
        await Services.GetRequiredService<ThemeService>().LoadAsync();
        await Services.GetRequiredService<ConsentStore>().LoadAsync();
        await Services.GetRequiredService<BackdropService>().LoadAsync();
        await Services.GetRequiredService<FlyoutService>().LoadAsync();

        var updates = Services.GetRequiredService<UpdateService>();
        await updates.LoadAsync();
        // Checked in the background so a slow or unreachable network never
        // delays startup.
        CrashLog.FireAndForget(updates.CheckAsync(notify: true));

        var closeBehavior = Services.GetRequiredService<WindowCloseBehavior>();
        await closeBehavior.LoadAsync();

        var startup = Services.GetRequiredService<StartupService>();
        await startup.LoadAsync();

        _mainWindow = Services.GetRequiredService<MainWindow>();
        closeBehavior.Attach(_mainWindow);

        // Launched by Windows at sign-in: stay in the tray rather than throwing
        // a window over whatever the user is doing.
        if (!StartupService.LaunchedToTray(Environment.GetCommandLineArgs()))
        {
            _mainWindow.Activate();
        }

        await Services.GetRequiredService<LimitsStore>().LoadAsync();
        await Services.GetRequiredService<SceneStore>().LoadAsync();

        // Hotkeys are registered once for the process, not per window, so they
        // keep working while Blare sits in the tray with no window at all.
        var hotkeys = Services.GetRequiredService<HotkeyCommands>();
        hotkeys.RegisterDefaults();

        if (hotkeys.Unavailable.Count > 0)
        {
            Services.GetRequiredService<FlyoutService>().Show(
                "Some shortcuts are taken",
                $"{string.Join(", ", hotkeys.Unavailable)} already belong to another app.",
                FlyoutTone.Caution,
                TimeSpan.FromSeconds(6));
        }

        Services.GetRequiredService<BreakReminder>().Start();

        _trayIcon = Services.GetRequiredService<TrayIconService>();
        _trayIcon.OpenRequested += (_, _) => _mainWindow.Activate();
        _trayIcon.MuteEverythingRequested += (_, _) => hotkeys.MuteEverything();
        _trayIcon.MuteForegroundRequested += (_, _) => hotkeys.ToggleMuteForeground();
        _trayIcon.ExitRequested += (_, _) =>
        {
            _trayIcon.Dispose();
            Exit();
        };
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Blight",
            "Blare");

        services.AddSingleton<AudioSessionManager>();
        services.AddSingleton<AudioDeviceManager>();
        services.AddSingleton<MonitorVolumeController>();
        services.AddSingleton<LoudnessTracker>();
        services.AddSingleton<ConsentState>();
        services.AddSingleton<SafetyMonitor>();
        services.AddSingleton<VolumeCoordinator>();
        services.AddSingleton<SpectrumMonitor>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<ISettingsStore>(new JsonFileSettingsStore(settingsDirectory));
        services.AddSingleton<SessionVolumeStore>();
        services.AddSingleton<ConsentStore>();
        services.AddSingleton<FlyoutService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<StartupService>();
        services.AddSingleton<DashboardStore>();
        services.AddSingleton<WindowCloseBehavior>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<BackdropService>();
        services.AddSingleton<HotkeyService>();
        services.AddSingleton<HotkeyCommands>();
        services.AddSingleton<LimitsStore>();
        services.AddSingleton<SceneStore>();
        services.AddSingleton<BreakReminder>();
        services.AddSingleton(new AppPaths(settingsDirectory));
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
