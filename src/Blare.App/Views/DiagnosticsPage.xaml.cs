using System.Text;
using Blight.Blare.App.Services;
using Blight.Blare.Audio.Analysis;
using Blight.Blare.Audio.Devices;
using Blight.Blare.Audio.Sessions;
using Blight.Blare.Core.Safety;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace Blight.Blare.App.Views;

/// <summary>
/// Live view of everything Blare is doing, so no behaviour is hidden.
///
/// Built in code rather than XAML for two reasons: each section can fail
/// independently into an inline error instead of taking the page (and the app)
/// down, and rows are generated from live data rather than duplicated markup.
/// </summary>
public sealed partial class DiagnosticsPage : Page
{
    private readonly Dictionary<string, StackPanel> _sectionBodies = new();
    private DispatcherQueueTimer? _refreshTimer;

    // Segoe MDL2 Assets code points, given numerically because the glyphs sit in
    // the Unicode private use area and do not survive being pasted around.
    private static readonly (string Title, int Glyph, string Blurb)[] Sections =
    [
        ("Playing now", 0xE767, "Apps Windows currently reports as holding an audio session."),
        ("Output devices", 0xE7F5, "Every output Windows can see. The starred one is the default."),
        ("Live analysis", 0xE9D9, "Audio capture feeding the spectrum meters."),
        ("Hearing safety", 0xE7BA, "What Blare is tracking and which protections are in force."),
        ("Boost", 0xEC48, "Why above-100% boost is currently unavailable."),
        ("App & storage", 0xE713, "Where Blare keeps its settings, and what it is running on."),
        ("Recent errors", 0xE783, "Problems Blare recorded. Empty is good."),
    ];

    public DiagnosticsPage()
    {
        InitializeComponent();

        foreach (var (title, glyph, blurb) in Sections)
        {
            AddSection(title, glyph, blurb);
        }

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(1);
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        Unloaded += (_, _) => _refreshTimer?.Stop();

        Refresh();
    }

    // ---- section scaffolding -------------------------------------------------

    private void AddSection(string title, int glyph, string blurb)
    {
        var body = new StackPanel { Spacing = 2 };

        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        heading.Children.Add(new FontIcon { Glyph = char.ConvertFromUtf32(glyph), FontSize = 14, Foreground = Brush("BlareAccent") });
        heading.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(heading);
        stack.Children.Add(new TextBlock { Text = blurb, FontSize = 11.5, Opacity = 0.55, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        stack.Children.Add(body);

        var border = new Border
        {
            Padding = new Thickness(14, 12, 14, 12),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = Brush("BlareStripBackground"),
            BorderBrush = Brush("BlareStripBorder"),
            Child = stack,
        };

        SectionsPanel.Children.Add(border);
        _sectionBodies[title] = body;
    }

    private static Brush? Brush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;

    /// <summary>A label/value line — the workhorse of this page.</summary>
    private static UIElement Row(string label, string value, Brush? valueBrush = null)
    {
        var grid = new Grid { ColumnSpacing = 14, Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock { Text = label, FontSize = 12.5, Opacity = 0.6, TextWrapping = TextWrapping.Wrap };
        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };

        if (valueBrush is not null)
        {
            valueBlock.Foreground = valueBrush;
        }

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        return grid;
    }

    private static UIElement Note(string text) => new TextBlock
    {
        Text = text,
        FontSize = 11.5,
        Opacity = 0.55,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
    };

    /// <summary>A coloured state pill, so "is this fine?" reads without parsing text.</summary>
    private static UIElement Pill(string text, string brushKey)
    {
        return new Border
        {
            Background = Brush(brushKey),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 1, 7, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 3, 0, 3),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            },
        };
    }

    // ---- refresh -------------------------------------------------------------

    private void OnLiveToggled(object sender, RoutedEventArgs e)
    {
        if (LiveToggle.IsOn)
        {
            _refreshTimer?.Start();
        }
        else
        {
            _refreshTimer?.Stop();
        }
    }

    private void Refresh()
    {
        var now = DateTimeOffset.UtcNow;

        Fill("Playing now", BuildSessions);
        Fill("Output devices", BuildDevices);
        Fill("Live analysis", BuildAnalysis);
        Fill("Hearing safety", body => BuildSafety(body, now));
        Fill("Boost", body => BuildBoost(body, now));
        Fill("App & storage", BuildEnvironment);
        Fill("Recent errors", BuildErrors);
    }

    /// <summary>Rebuilds one section, degrading to an inline message if it throws.</summary>
    private void Fill(string title, Action<StackPanel> build)
    {
        if (!_sectionBodies.TryGetValue(title, out var body))
        {
            return;
        }

        body.Children.Clear();

        try
        {
            build(body);
        }
        catch (Exception ex)
        {
            body.Children.Add(Row("Unavailable", $"{ex.GetType().Name}: {ex.Message}", Brush("BlareMeterHigh")));
        }
    }

    private static T Service<T>() where T : notnull => App.Services.GetRequiredService<T>();

    private static void BuildSessions(StackPanel body)
    {
        var sessions = Service<AudioSessionManager>().GetSessionsForDefaultDevice();

        if (sessions.Count == 0)
        {
            body.Children.Add(Row("Sessions", "Nothing is playing"));
            return;
        }

        foreach (var session in sessions.OrderByDescending(s => s.PeakLevel))
        {
            var name = ResolveName(session);
            var state = session.IsMuted ? "muted" : session.PeakLevel > 0.001f ? "playing" : "silent";
            var detail = $"{session.Volume:P0} volume · {state} · pid {session.ProcessId}";

            body.Children.Add(Row(name, detail, session.PeakLevel > 0.001f ? Brush("BlareMeterLow") : null));
        }
    }

    private static string ResolveName(AudioSessionInfo session)
    {
        if (session.IsSystemSoundsSession)
        {
            return "Windows sounds";
        }

        if (!string.IsNullOrWhiteSpace(session.DisplayName))
        {
            return session.DisplayName;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)session.ProcessId);
            return process.ProcessName;
        }
        catch
        {
            return $"pid {session.ProcessId}";
        }
    }

    private static void BuildDevices(StackPanel body)
    {
        var deviceManager = Service<AudioDeviceManager>();

        foreach (var device in deviceManager.GetRenderDevices())
        {
            var volume = deviceManager.GetMasterVolume(device.DeviceId);
            body.Children.Add(Row(
                device.IsDefault ? $"★ {device.DisplayName}" : device.DisplayName,
                $"{volume:P0} volume",
                device.IsDefault ? Brush("BlareAccent") : null));
        }

        foreach (var control in Service<MonitorVolumeController>().GetControls())
        {
            body.Children.Add(Row(
                control.DisplayName,
                control.SupportsVolume
                    ? $"{control.VolumePercent:F0}% speaker volume (display hardware)"
                    : "no speaker volume over DDC/CI",
                control.SupportsVolume ? Brush("BlareMeterLow") : null));
        }

        body.Children.Add(Note(
            "Displays with built-in speakers have their own amplifier volume, separate from " +
            "Windows. Blare reads it over the display data channel. Speakers on a headphone or " +
            "line-out jack have a purely analogue knob with no connection back to the PC, so " +
            "their position cannot be read by any software."));
    }

    private static void BuildAnalysis(StackPanel body)
    {
        var monitor = Service<SpectrumMonitor>();
        var statuses = monitor.Statuses;

        body.Children.Add(Row("Frequency bands", monitor.BandCount.ToString()));
        body.Children.Add(Row("Capture streams", statuses.Count == 0 ? "none running" : statuses.Count.ToString()));

        foreach (var status in statuses.OrderBy(s => s.ProcessId))
        {
            var value = status.Error is { } error
                ? $"failed — {error}"
                : status.BlocksReceived == 0
                    ? "connected, no audio received yet"
                    : $"receiving audio ({status.BlocksReceived:N0} blocks)";

            var brush = status.Error is not null
                ? Brush("BlareMeterHigh")
                : status.BlocksReceived > 0
                    ? Brush("BlareMeterLow")
                    : Brush("BlareMeterMid");

            body.Children.Add(Row($"pid {status.ProcessId}", value, brush));
        }

        body.Children.Add(Note(
            "Each stream is one per-process audio capture plus a Fourier transform. " +
            "They stop when the mixer isn't on screen. If a meter isn't moving, the row above says why."));
    }

    private static void BuildSafety(StackPanel body, DateTimeOffset now)
    {
        var safety = Service<SafetyMonitor>();
        var consent = Service<ConsentState>();

        var suppressed = safety.WarningsDisabled(now);

        body.Children.Add(Pill(
            suppressed ? "PROTECTION OFF" : "PROTECTION ON",
            suppressed ? "BlareMeterHigh" : "BlareMeterLow"));

        body.Children.Add(Row("Warnings raised", safety.WarningCount.ToString()));
        body.Children.Add(Row("Time spent loud", $"{safety.TotalTimeAboveThreshold.TotalMinutes:F0} minutes"));
        body.Children.Add(Row("Counts as loud", $"output at or above {safety.LoudLevel:P0}"));
        body.Children.Add(Row("Warns after", $"{safety.WarnAfter.TotalMinutes:F0} minutes loud"));
        body.Children.Add(Row("Re-confirm after", $"{consent.ReconfirmationInterval.TotalDays:F0} days"));

        var records = consent.Records.Where(r => r.IsActive).ToList();
        if (records.Count == 0)
        {
            body.Children.Add(Row("Opt-outs", "None — everything at its safe default"));
        }
        else
        {
            foreach (var record in records)
            {
                var friendly = record.Kind == ConsentKind.SafetyWarningsDisabled
                    ? "Health warnings off"
                    : "Boost ceiling raised";

                var remaining = consent.TimeUntilExpiry(record.Kind, now);
                body.Children.Add(Row(
                    friendly,
                    remaining is { } left
                        ? $"active · returns to safe in {left.TotalDays:F0} days"
                        : "lapsed · back to safe default",
                    Brush("BlareMeterMid")));
            }
        }

        body.Children.Add(Note(
            "Loudness is judged from the sound actually leaving your machine — an app's measured " +
            "level scaled by the output device volume. An app's own slider sitting at 100% is not " +
            "loud on its own: Windows sets every app to 100% by default, and it only means the app " +
            "isn't being turned down. Silence never counts. This is a relative measure, not sound " +
            "pressure at your ears — Blare can't know your speaker or headphone volume."));
    }

    private static void BuildBoost(StackPanel body, DateTimeOffset now)
    {
        var boost = Service<BoostCoordinator>();

        var anyBoosted = boost.AnyBoosted;

        body.Children.Add(Pill(anyBoosted ? "BOOSTING" : "IDLE", anyBoosted ? "BlareMeterHigh" : "BlareMeterLow"));

        body.Children.Add(Row("Volume ceiling", $"{boost.CurrentCeilingPercent(now):F0}%"));
        body.Children.Add(Row("Apps boosted", boost.BoostedCount.ToString()));
        body.Children.Add(Row("Turns off after", $"{BoostCoordinator.AutoDisableAfter.TotalMinutes:F0} minutes"));
        body.Children.Add(Row(
            "Original held at",
            $"{Blight.Blare.Audio.Boost.BoostEngine.ResidualLevel:P0} while boosting"));

        body.Children.Add(Note(
            "Windows applies an app's volume before Blare captures it, so a muted app captures " +
            "silence and can't be amplified — measured at 0.000000 muted against 0.567 unmuted. " +
            "Boost therefore holds the original at a residual level rather than muting it, and " +
            "multiplies the captured signal back up. What leaks through directly is about -34 dB " +
            "below the boosted output. Every boost expires on its own so amplified audio can't " +
            "run indefinitely."));
    }

    private static void BuildEnvironment(StackPanel body)
    {
        var theme = Service<ThemeService>();
        var backdrop = Service<BackdropService>();
        var paths = Service<AppPaths>();

        body.Children.Add(Row("Theme", theme.Current.ToString()));
        body.Children.Add(Row(
            "Window material",
            backdrop.Requested == backdrop.EffectiveKind
                ? backdrop.EffectiveKind.ToString()
                : $"{backdrop.EffectiveKind} (asked for {backdrop.Requested})"));
        body.Children.Add(Row("Mica", BackdropService.MicaSupported ? "supported" : "not available on this Windows version"));
        body.Children.Add(Row("Acrylic", BackdropService.AcrylicSupported ? "supported" : "not available on this Windows version"));
        body.Children.Add(Row("Settings folder", paths.SettingsDirectory));
        body.Children.Add(Row("Error log", CrashLog.FilePath));
        body.Children.Add(Row("Windows", Environment.OSVersion.VersionString));
        body.Children.Add(Row("Memory in use", $"{Environment.WorkingSet / 1024 / 1024} MB"));

        body.Children.Add(Note("Blare makes no network connections. Nothing here leaves this machine."));
    }

    private static void BuildErrors(StackPanel body)
    {
        var log = CrashLog.ReadRecent(3000);

        if (log.StartsWith('('))
        {
            body.Children.Add(Row("Status", "No errors recorded", Brush("BlareMeterLow")));
            return;
        }

        body.Children.Add(new TextBlock
        {
            Text = log.Trim(),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        });
    }

    // ---- copy ----------------------------------------------------------------

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        var report = new StringBuilder($"Blare diagnostics — {DateTimeOffset.Now:u}").AppendLine().AppendLine();

        foreach (var (title, panel) in _sectionBodies)
        {
            report.AppendLine($"## {title}");
            foreach (var text in panel.Children.OfType<Grid>()
                         .Select(grid => grid.Children.OfType<TextBlock>().Select(t => t.Text).ToList())
                         .Where(parts => parts.Count == 2))
            {
                report.AppendLine($"  {text[0],-22} {text[1]}");
            }

            report.AppendLine();
        }

        try
        {
            var package = new DataPackage();
            package.SetText(report.ToString());
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
    }
}
