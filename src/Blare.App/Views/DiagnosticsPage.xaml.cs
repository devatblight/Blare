using System.Text;
using BLight.Blare.App.Services;
using BLight.Blare.Audio.Analysis;
using BLight.Blare.Audio.Devices;
using BLight.Blare.Audio.Sessions;
using BLight.Blare.Core.Safety;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace BLight.Blare.App.Views;

/// <summary>
/// Live view of everything Blare is doing. Exists so no behaviour is hidden
/// from the user: which sessions are seen, which capture streams are running,
/// what safety state is in force and when it expires, and where settings live
/// on disk. Read-only by design.
/// </summary>
public sealed partial class DiagnosticsPage : Page
{
    private readonly AudioSessionManager _sessionManager;
    private readonly AudioDeviceManager _deviceManager;
    private readonly SpectrumMonitor _spectrumMonitor;
    private readonly SafetyMonitor _safetyMonitor;
    private readonly BoostCoordinator _boostCoordinator;
    private readonly ConsentState _consent;
    private readonly ThemeService _themeService;
    private readonly AppPaths _paths;
    private readonly DispatcherQueueTimer _refreshTimer;

    public DiagnosticsPage()
    {
        _sessionManager = App.Services.GetRequiredService<AudioSessionManager>();
        _deviceManager = App.Services.GetRequiredService<AudioDeviceManager>();
        _spectrumMonitor = App.Services.GetRequiredService<SpectrumMonitor>();
        _safetyMonitor = App.Services.GetRequiredService<SafetyMonitor>();
        _boostCoordinator = App.Services.GetRequiredService<BoostCoordinator>();
        _consent = App.Services.GetRequiredService<ConsentState>();
        _themeService = App.Services.GetRequiredService<ThemeService>();
        _paths = App.Services.GetRequiredService<AppPaths>();

        InitializeComponent();

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(1);
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        Unloaded += (_, _) => _refreshTimer.Stop();

        Refresh();
    }

    private void OnLiveToggled(object sender, RoutedEventArgs e)
    {
        if (LiveToggle.IsOn)
        {
            _refreshTimer.Start();
        }
        else
        {
            _refreshTimer.Stop();
        }
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        var report = new StringBuilder()
            .AppendLine("=== AUDIO SESSIONS ===").AppendLine(SessionsText.Text)
            .AppendLine("=== OUTPUT DEVICES ===").AppendLine(DevicesText.Text)
            .AppendLine("=== CAPTURE & ANALYSIS ===").AppendLine(CaptureText.Text)
            .AppendLine("=== SAFETY STATE ===").AppendLine(SafetyText.Text)
            .AppendLine("=== BOOST ENGINE ===").AppendLine(BoostText.Text)
            .AppendLine("=== ENVIRONMENT ===").AppendLine(EnvironmentText.Text)
            .ToString();

        var package = new DataPackage();
        package.SetText(report);
        Clipboard.SetContent(package);
    }

    private void Refresh()
    {
        var now = DateTimeOffset.UtcNow;

        RefreshSessions();
        RefreshDevices();
        RefreshCapture();
        RefreshSafety(now);
        RefreshBoost(now);
        RefreshEnvironment();
    }

    private void RefreshSessions()
    {
        try
        {
            var sessions = _sessionManager.GetSessionsForDefaultDevice();
            var text = new StringBuilder();

            foreach (var session in sessions)
            {
                text.AppendLine(
                    $"pid {session.ProcessId,-7} vol {session.Volume,6:P0}  peak {session.PeakLevel,7:F4}  " +
                    $"{(session.IsMuted ? "MUTED  " : "       ")}" +
                    $"{(session.IsSystemSoundsSession ? "system " : "       ")}" +
                    $"{(string.IsNullOrWhiteSpace(session.DisplayName) ? "" : session.DisplayName)}");
            }

            SessionsText.Text = sessions.Count == 0 ? "(no sessions)" : text.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            SessionsText.Text = $"Failed to enumerate sessions: {ex.Message}";
        }
    }

    private void RefreshDevices()
    {
        try
        {
            var devices = _deviceManager.GetRenderDevices();
            var text = new StringBuilder();

            foreach (var device in devices)
            {
                var volume = _deviceManager.GetMasterVolume(device.DeviceId);
                text.AppendLine($"{(device.IsDefault ? "* " : "  ")}{volume,5:P0}  {device.DisplayName}");
            }

            DevicesText.Text = text.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            DevicesText.Text = $"Failed to enumerate devices: {ex.Message}";
        }
    }

    private void RefreshCapture()
    {
        var watched = _spectrumMonitor.WatchedProcesses;
        var text = new StringBuilder()
            .AppendLine($"FFT bands        {_spectrumMonitor.BandCount}")
            .AppendLine($"capture streams  {watched.Count}");

        if (watched.Count > 0)
        {
            text.AppendLine($"watching pids    {string.Join(", ", watched.OrderBy(p => p))}");
        }

        text.AppendLine();
        text.AppendLine("Each stream is one per-process WASAPI loopback capture plus an FFT.");
        text.AppendLine("Streams stop when the mixer page is not visible.");

        CaptureText.Text = text.ToString().TrimEnd();
    }

    private void RefreshSafety(DateTimeOffset now)
    {
        var text = new StringBuilder()
            .AppendLine($"warnings raised     {_safetyMonitor.WarningCount}")
            .AppendLine($"total time loud     {_safetyMonitor.TotalTimeAboveThreshold.TotalMinutes:F1} min")
            .AppendLine($"warnings suppressed {(_safetyMonitor.WarningsDisabled(now) ? "YES" : "no")}")
            .AppendLine()
            .AppendLine($"re-confirmation interval  {_consent.ReconfirmationInterval.TotalDays:F0} days");

        var records = _consent.Records.ToList();
        if (records.Count == 0)
        {
            text.AppendLine("no consent records — all protections at their safe defaults");
        }
        else
        {
            foreach (var record in records)
            {
                var active = _consent.IsActive(record.Kind, now);
                var remaining = _consent.TimeUntilExpiry(record.Kind, now);

                text.AppendLine(
                    $"{record.Kind,-26} {(active ? "ACTIVE" : "off   ")}" +
                    (remaining is { } left ? $"  lapses in {left.TotalDays:F1} days" : string.Empty));
            }
        }

        SafetyText.Text = text.ToString().TrimEnd();
    }

    private void RefreshBoost(DateTimeOffset now)
    {
        BoostText.Text = new StringBuilder()
            .AppendLine($"boost pipeline   DISABLED")
            .AppendLine($"apps boosted     {_boostCoordinator.BoostedCount}")
            .AppendLine($"volume ceiling   {_boostCoordinator.CurrentCeilingPercent(now):F0}%")
            .AppendLine()
            .AppendLine("Above-unity boost is off because per-process loopback capture is")
            .AppendLine("applied after session volume and mute: silencing the original to")
            .AppendLine("replace it also silences the copy being amplified. Measured as")
            .AppendLine("captured peak 0.000000 when muted, 0.567 when not.")
            .ToString().TrimEnd();
    }

    private void RefreshEnvironment()
    {
        EnvironmentText.Text = new StringBuilder()
            .AppendLine($"theme        {_themeService.Current}")
            .AppendLine($"settings     {_paths.SettingsDirectory}")
            .AppendLine($"OS           {Environment.OSVersion.VersionString}")
            .AppendLine($"process      {Environment.ProcessId} ({(Environment.Is64BitProcess ? "x64" : "x86")})")
            .AppendLine($"working set  {Environment.WorkingSet / 1024 / 1024} MB")
            .AppendLine()
            .AppendLine("Blare makes no network connections. Nothing here leaves this machine.")
            .ToString().TrimEnd();
    }
}
