using System.ComponentModel;
using System.Diagnostics;
using Blight.Blare.App.Controls;
using Blight.Blare.App.Services;
using Blight.Blare.App.ViewModels;
using Blight.Blare.Audio.Analysis;
using Blight.Blare.Audio.Devices;
using Blight.Blare.Audio.Sessions;
using Blight.Blare.Core.Layout;
using Blight.Blare.Core.Mixing;
using Blight.Blare.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Blight.Blare.App.Views;

public sealed partial class MixerPage : Page
{
    private const double CardGap = 8;

    private readonly AudioSessionManager _sessionManager;
    private readonly AudioDeviceManager _deviceManager;
    private readonly MonitorVolumeController _monitorVolume;
    private readonly SessionVolumeStore _volumeStore;
    private readonly SafetyMonitor _safetyMonitor;
    private readonly VolumeCoordinator _volumeCoordinator;
    private readonly SpectrumMonitor _spectrumMonitor;
    private readonly DashboardStore _dashboardStore;
    private readonly SessionGroupTracker _groupTracker = new();
    private readonly IconResolver _iconResolver = new();

    private readonly DispatcherQueueTimer _safetyTimer;
    private readonly DispatcherQueueTimer _meterTimer;
    private readonly DispatcherQueueTimer _sessionTimer;

    /// <summary>Strips are keyed by app identity, not process id — a browser spreads audio across many processes but is one thing to the user.</summary>
    private readonly Dictionary<string, ChannelStrip> _strips = new();

    /// <summary>Every live process backing each app strip, so a fader moves all of them.</summary>
    private readonly Dictionary<string, List<uint>> _processesByApp = new();

    private readonly List<DashboardCardHost> _hosts = new();

    /// <summary>Ghost showing where a dragged card will land.</summary>
    private readonly Rectangle _dropPreview = new();
    private readonly double[] _bandScratch;

    // Built at runtime by the card content, so any of these may be absent when
    // the user has removed that card.
    private StackPanel? _stripsPanel;
    private TextBlock? _emptyState;
    private Slider? _masterVolumeSlider;
    private TextBlock? _masterVolumeText;
    private TextBlock? _masterDeviceNameText;
    private TextBlock? _warningCountText;
    private TextBlock? _exposureText;
    private Ellipse? _warningDot;
    private Ellipse? _exposureDot;
    private TextBlock? _nowPlayingText;
    private TextBlock? _nowPlayingDetail;

    private bool _suppressMasterVolumePush;
    private string? _masterDeviceId;

    /// <summary>Levels captured before focus was engaged, so releasing focus puts the desk back exactly as it was.</summary>
    private IReadOnlyList<FocusLevel>? _levelsBeforeFocus;
    private string? _focusedAppKey;

    public MixerPage()
    {
        _sessionManager = App.Services.GetRequiredService<AudioSessionManager>();
        _deviceManager = App.Services.GetRequiredService<AudioDeviceManager>();
        _monitorVolume = App.Services.GetRequiredService<MonitorVolumeController>();
        _volumeStore = App.Services.GetRequiredService<SessionVolumeStore>();
        _safetyMonitor = App.Services.GetRequiredService<SafetyMonitor>();
        _volumeCoordinator = App.Services.GetRequiredService<VolumeCoordinator>();
        _spectrumMonitor = App.Services.GetRequiredService<SpectrumMonitor>();
        _dashboardStore = App.Services.GetRequiredService<DashboardStore>();
        _bandScratch = new double[_spectrumMonitor.BandCount];

        InitializeComponent();
        BuildAddCardMenu();
        BuildDropPreview();

        _safetyTimer = CreateTimer(TimeSpan.FromSeconds(5), RunSafetySample);
        _meterTimer = CreateTimer(TimeSpan.FromMilliseconds(50), RefreshMeters);
        _sessionTimer = CreateTimer(TimeSpan.FromSeconds(2), () => CrashLog.FireAndForget(RefreshSessionsAsync()));

        Unloaded += (_, _) =>
        {
            _safetyTimer.Stop();
            _meterTimer.Stop();
            _sessionTimer.Stop();
            // Capture streams are expensive — never leave them running for a page nobody's looking at.
            _spectrumMonitor.StopAll();
        };

        CrashLog.FireAndForget(InitializeAsync());
    }

    private DispatcherQueueTimer CreateTimer(TimeSpan interval, Action tick)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = interval;
        timer.Tick += (_, _) => tick();
        timer.Start();
        return timer;
    }

    private async Task InitializeAsync()
    {
        await _volumeStore.LoadAsync();
        await _dashboardStore.LoadAsync();

        RebuildDashboard();
        await RefreshSessionsAsync();
        UpdateStatusChips();
    }

    // ---- dashboard -----------------------------------------------------------

    private void BuildDropPreview()
    {
        _dropPreview.RadiusX = 8;
        _dropPreview.RadiusY = 8;
        _dropPreview.StrokeThickness = 1.5;
        _dropPreview.StrokeDashArray = [3, 3];
        _dropPreview.Visibility = Visibility.Collapsed;
        _dropPreview.IsHitTestVisible = false;

        if (Application.Current.Resources.TryGetValue("BlareAccent", out var accent) && accent is Brush brush)
        {
            _dropPreview.Stroke = brush;
            _dropPreview.Fill = brush;
            _dropPreview.Opacity = 0.18;
        }

        DropLayer.Children.Add(_dropPreview);
    }

    private void BuildAddCardMenu()
    {
        foreach (var kind in Enum.GetValues<CardKind>())
        {
            var item = new MenuFlyoutItem { Text = TitleFor(kind), Tag = kind };
            item.Click += OnAddCardClick;
            AddCardFlyout.Items.Add(item);
        }
    }

    private void RebuildDashboard()
    {
        CardCanvas.Children.Clear();
        _hosts.Clear();

        // Card content re-creates these; drop the stale references first.
        _stripsPanel = null;
        _emptyState = null;
        _masterVolumeSlider = null;
        _warningCountText = null;
        _nowPlayingText = null;
        _strips.Clear();

        foreach (var card in _dashboardStore.Layout.Cards)
        {
            var host = new DashboardCardHost(card, TitleFor(card.Kind), BuildCardContent(card.Kind));
            host.Previewing += OnCardPreviewing;
            host.Committed += OnCardCommitted;
            host.RemoveButton.Click += (_, _) => RemoveCard(card.Id);
            host.SetEditing(EditModeToggle.IsChecked == true);

            _hosts.Add(host);
            CardCanvas.Children.Add(host);
        }

        LayoutCards();
        CrashLog.FireAndForget(RefreshSessionsAsync());
    }

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        LayoutCards();
        DrawGuides();
    }

    /// <summary>Positions every card from its grid coordinates. One place converts cells to pixels.</summary>
    private void LayoutCards()
    {
        var (cellWidth, cellHeight) = CellSize();

        if (cellWidth <= 0 || cellHeight <= 0)
        {
            return;
        }

        foreach (var host in _hosts)
        {
            var card = host.Card;

            host.SetCellSize(cellWidth, cellHeight);
            host.Width = Math.Max(0, card.ColumnSpan * cellWidth - CardGap);
            host.Height = Math.Max(0, card.RowSpan * cellHeight - CardGap);
            Canvas.SetLeft(host, card.Column * cellWidth);
            Canvas.SetTop(host, card.Row * cellHeight);
        }
    }

    private (double Width, double Height) CellSize() =>
        (DashboardSurface.ActualWidth / DashboardLayout.Columns,
         DashboardSurface.ActualHeight / DashboardLayout.Rows);

    /// <summary>Shows where a dragged card would land, so the drop isn't a guess.</summary>
    private void OnCardPreviewing(object? sender, Blight.Blare.Core.Layout.DashboardCard card)
    {
        var (cellWidth, cellHeight) = CellSize();

        _dropPreview.Width = Math.Max(0, card.ColumnSpan * cellWidth - CardGap);
        _dropPreview.Height = Math.Max(0, card.RowSpan * cellHeight - CardGap);
        Canvas.SetLeft(_dropPreview, Math.Clamp(card.Column, 0, DashboardLayout.Columns - card.ColumnSpan) * cellWidth);
        Canvas.SetTop(_dropPreview, Math.Clamp(card.Row, 0, DashboardLayout.Rows - card.RowSpan) * cellHeight);
        _dropPreview.Visibility = Visibility.Visible;
    }

    private void OnCardCommitted(object? sender, Blight.Blare.Core.Layout.DashboardCard card)
    {
        _dropPreview.Visibility = Visibility.Collapsed;

        // Resize first so displacement is computed against the final footprint.
        _dashboardStore.Layout.Resize(card.Id, card.ColumnSpan, card.RowSpan);

        // Place pushes anything in the way aside, and refuses the drop outright
        // if there's nowhere for the displaced card to go.
        _dashboardStore.Layout.Place(card.Id, card.Column, card.Row);

        // Every host adopts the model's answer — the dragged card may have been
        // clamped or refused, and others may have been displaced.
        foreach (var host in _hosts)
        {
            if (_dashboardStore.Layout.Get(host.Card.Id) is { } placed)
            {
                host.ApplyCard(placed);
            }
        }

        LayoutCards();
        CrashLog.FireAndForget(_dashboardStore.SaveAsync());
    }

    private void OnAddCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: CardKind kind })
        {
            return;
        }

        var slot = _dashboardStore.Layout.FindFreeSlot(4, 3);

        if (slot is null)
        {
            App.Services.GetRequiredService<FlyoutService>().Show(
                "No room for another card",
                "Move or shrink something first.",
                FlyoutTone.Caution,
                TimeSpan.FromSeconds(4));
            return;
        }

        _dashboardStore.Layout.Add(new Blight.Blare.Core.Layout.DashboardCard(
            Guid.NewGuid().ToString("n")[..8], kind, slot.Value.Column, slot.Value.Row, 4, 3));

        CrashLog.FireAndForget(_dashboardStore.SaveAsync());
        RebuildDashboard();
    }

    private void RemoveCard(string id)
    {
        _dashboardStore.Layout.Remove(id);
        CrashLog.FireAndForget(_dashboardStore.SaveAsync());
        RebuildDashboard();
    }

    private async void OnResetLayoutClick(object sender, RoutedEventArgs e)
    {
        await _dashboardStore.ResetAsync();
        RebuildDashboard();
    }

    private void OnEditModeChanged(object sender, RoutedEventArgs e)
    {
        var editing = EditModeToggle.IsChecked == true;

        EditTools.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        GridGuides.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        foreach (var host in _hosts)
        {
            host.SetEditing(editing);
        }

        DrawGuides();
    }

    /// <summary>Faint cell lines while editing, so the snap grid is visible rather than guessed at.</summary>
    private void DrawGuides()
    {
        GridGuides.Children.Clear();

        if (EditModeToggle.IsChecked != true)
        {
            return;
        }

        var (cellWidth, cellHeight) = CellSize();
        var stroke = Application.Current.Resources.TryGetValue("BlareStripBorder", out var value)
            ? value as Brush
            : null;

        for (var column = 1; column < DashboardLayout.Columns; column++)
        {
            GridGuides.Children.Add(Guide(column * cellWidth, 0, 1, DashboardSurface.ActualHeight, stroke));
        }

        for (var row = 1; row < DashboardLayout.Rows; row++)
        {
            GridGuides.Children.Add(Guide(0, row * cellHeight, DashboardSurface.ActualWidth, 1, stroke));
        }
    }

    private static Rectangle Guide(double x, double y, double width, double height, Brush? stroke)
    {
        var line = new Rectangle { Width = width, Height = height, Fill = stroke, Opacity = 0.35 };
        Canvas.SetLeft(line, x);
        Canvas.SetTop(line, y);
        return line;
    }

    // ---- quick actions -------------------------------------------------------

    private void SetAllMuted(bool muted)
    {
        foreach (var viewModel in _strips.Values.Select(s => s.ViewModel).Where(v => v is not null))
        {
            viewModel!.IsMuted = muted;
        }
    }

    private void SetAllVolume(double percent)
    {
        foreach (var viewModel in _strips.Values.Select(s => s.ViewModel).Where(v => v is not null))
        {
            viewModel!.VolumePercent = percent;
        }
    }

    // ---- devices -------------------------------------------------------------

    private void RefreshMasterDevice()
    {
        var defaultDevice = _deviceManager.GetRenderDevices().FirstOrDefault(d => d.IsDefault);

        if (defaultDevice is null || _masterVolumeSlider is null)
        {
            return;
        }

        _masterDeviceId = defaultDevice.DeviceId;

        if (_masterDeviceNameText is not null)
        {
            _masterDeviceNameText.Text = defaultDevice.DisplayName;
        }

        _suppressMasterVolumePush = true;
        _masterVolumeSlider.Value = Math.Round(_deviceManager.GetMasterVolume(defaultDevice.DeviceId) * 100);
        _suppressMasterVolumePush = false;

        if (_masterVolumeText is not null)
        {
            _masterVolumeText.Text = $"{_masterVolumeSlider.Value:F0}%";
        }
    }

    private void OnMasterVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_masterVolumeText is not null)
        {
            _masterVolumeText.Text = $"{e.NewValue:F0}%";
        }

        if (_suppressMasterVolumePush || _masterDeviceId is null)
        {
            return;
        }

        _deviceManager.SetMasterVolume(_masterDeviceId, (float)(e.NewValue / 100.0));
    }

    // ---- meters and safety ---------------------------------------------------

    private void RefreshMeters()
    {
        foreach (var (appKey, strip) in _strips)
        {
            if (_processesByApp.TryGetValue(appKey, out var processes)
                && _spectrumMonitor.TryGetMergedBands(processes, _bandScratch))
            {
                strip.SetLevels(_bandScratch);
            }
        }
    }

    private void UpdateStatusChips()
    {
        var warnings = _safetyMonitor.WarningCount;
        var minutesLoud = _safetyMonitor.TotalTimeAboveThreshold.TotalMinutes;

        if (_warningCountText is not null)
        {
            _warningCountText.Text = warnings switch
            {
                0 => "no warnings",
                1 => "1 warning",
                _ => $"{warnings} warnings",
            };
        }

        if (_exposureText is not null)
        {
            _exposureText.Text = $"{minutesLoud:F0}m loud today";
        }

        // Dots only light when there's something to report, so a calm desk reads as calm.
        if (_warningDot is not null)
        {
            _warningDot.Fill = BrushFor(warnings > 0 ? "BlareMeterMid" : "BlareMeterUnlit");
        }

        if (_exposureDot is not null)
        {
            _exposureDot.Fill = BrushFor(
                minutesLoud >= 60 ? "BlareMeterMid" : minutesLoud > 0 ? "BlareMeterLow" : "BlareMeterUnlit");
        }
    }

    private static Brush BrushFor(string resourceKey) => (Brush)Application.Current.Resources[resourceKey];

    private void RunSafetySample()
    {
        var peaksByApp = new Dictionary<string, double>();

        foreach (var session in _sessionManager.GetSessionsForDefaultDevice())
        {
            if (session.IsSystemSoundsSession)
            {
                continue;
            }

            var appKey = _strips.Keys.FirstOrDefault(key =>
                _processesByApp.TryGetValue(key, out var processes) && processes.Contains(session.ProcessId));

            if (appKey is null)
            {
                continue;
            }

            peaksByApp[appKey] = Math.Max(peaksByApp.GetValueOrDefault(appKey), session.PeakLevel);
        }

        UpdateNowPlaying(peaksByApp);

        var masterVolume = _masterDeviceId is null ? 1.0 : _deviceManager.GetMasterVolume(_masterDeviceId);
        var warned = _safetyMonitor.Sample(
            peaksByApp.Select(pair => (pair.Key, pair.Value)), masterVolume, DateTimeOffset.UtcNow);

        UpdateStatusChips();

        if (warned.Count == 0)
        {
            WarningInfoBar.IsOpen = false;
            return;
        }

        var names = _strips
            .Where(pair => warned.Contains(pair.Key) && pair.Value.ViewModel is not null)
            .Select(pair => pair.Value.ViewModel!.DisplayName);

        WarningInfoBar.Message = $"{string.Join(", ", names)} — running loud for a while. This is a relative signal level, not a measurement of sound at your ears.";
        WarningInfoBar.IsOpen = true;
    }

    private void UpdateNowPlaying(Dictionary<string, double> peaksByApp)
    {
        if (_nowPlayingText is null)
        {
            return;
        }

        var loudest = peaksByApp.OrderByDescending(pair => pair.Value).FirstOrDefault();

        if (loudest.Key is null || loudest.Value <= 0.001)
        {
            _nowPlayingText.Text = "Nothing playing";
            if (_nowPlayingDetail is not null)
            {
                _nowPlayingDetail.Text = string.Empty;
            }

            return;
        }

        var viewModel = _strips.TryGetValue(loudest.Key, out var strip) ? strip.ViewModel : null;
        _nowPlayingText.Text = viewModel?.DisplayName ?? loudest.Key;

        if (_nowPlayingDetail is not null)
        {
            _nowPlayingDetail.Text = $"{viewModel?.VolumePercent ?? 100:F0}% · peaking at {loudest.Value:P0}";
        }
    }

    // ---- sessions ------------------------------------------------------------

    public async Task RefreshSessionsAsync()
    {
        if (_stripsPanel is null)
        {
            // The app mixer card isn't on the dashboard.
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var ceiling = VolumeCoordinator.MaximumPercent;

        var liveSessions = _sessionManager.GetSessionsForDefaultDevice()
            .Where(s => !s.IsSystemSoundsSession)
            .Select(session =>
            {
                var (displayName, executablePath) = ResolveProcessInfo(session);
                return (Session: session, DisplayName: displayName, ExecutablePath: executablePath);
            })
            .ToList();

        // Group by app identity. Passing Guid.Empty keys purely on that identity,
        // which collapses a browser's many audio processes into a single strip;
        // the tracker still supplies the debounce so a session blinking out
        // doesn't flicker the desk.
        var snapshots = liveSessions
            .Select(entry => new SessionSnapshot(
                AppKeyFor(entry.ExecutablePath, entry.Session.ProcessId), Guid.Empty, entry.Session.ProcessId))
            .ToList();

        var rows = _groupTracker.Reconcile(snapshots, now);
        var surviving = rows.Select(row => row.GroupKey).ToHashSet();

        foreach (var goneKey in _strips.Keys.Where(key => !surviving.Contains(key)).ToList())
        {
            _stripsPanel.Children.Remove(_strips[goneKey]);
            _strips.Remove(goneKey);
            _processesByApp.Remove(goneKey);
        }

        _processesByApp.Clear();
        foreach (var entry in liveSessions)
        {
            var groupKey = GroupKeyFor(entry.ExecutablePath, entry.Session.ProcessId);

            if (!_processesByApp.TryGetValue(groupKey, out var processes))
            {
                processes = new List<uint>();
                _processesByApp[groupKey] = processes;
            }

            processes.Add(entry.Session.ProcessId);
        }

        foreach (var entry in liveSessions)
        {
            var groupKey = GroupKeyFor(entry.ExecutablePath, entry.Session.ProcessId);

            if (_strips.TryGetValue(groupKey, out var existing))
            {
                SyncExistingStrip(existing, entry.Session, ceiling);
                continue;
            }

            var liveVolumePercent = Math.Round(entry.Session.Volume * 100);

            double? savedVolumePercent = null;
            if (!string.IsNullOrEmpty(entry.ExecutablePath)
                && _volumeStore.GetVolume(entry.ExecutablePath) is { } stored)
            {
                savedVolumePercent = Math.Clamp(stored, 0, ceiling);
            }

            if (savedVolumePercent is { } saved && Math.Abs(saved - liveVolumePercent) > 0.5)
            {
                _volumeCoordinator.SetVolumePercent(entry.Session.ProcessId, saved);
            }

            var viewModel = new SessionRowViewModel
            {
                ProcessId = entry.Session.ProcessId,
                AppKey = groupKey,
                DisplayName = string.IsNullOrWhiteSpace(entry.Session.DisplayName) ? entry.DisplayName : entry.Session.DisplayName,
                ExecutablePath = entry.ExecutablePath,
                VolumePercent = Math.Min(savedVolumePercent ?? liveVolumePercent, ceiling),
                MaxVolumePercent = ceiling,
                IsMuted = entry.Session.IsMuted,
            };
            viewModel.PropertyChanged += OnViewModelPropertyChanged;

            var strip = new ChannelStrip();
            strip.Bind(viewModel);
            strip.FocusRequested += (_, key) => CrashLog.FireAndForget(ToggleFocusAsync(key));

            _strips[groupKey] = strip;
            _stripsPanel.Children.Add(strip);

            if (!string.IsNullOrEmpty(entry.ExecutablePath))
            {
                CrashLog.FireAndForget(ResolveIconAsync(viewModel, entry.ExecutablePath));
            }
        }

        UpdateMeteredProcesses();

        if (_emptyState is not null)
        {
            _emptyState.Visibility = _strips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static string GroupKeyFor(string executablePath, uint processId) =>
        SessionGroupTracker.ComputeGroupKey(Guid.Empty, AppKeyFor(executablePath, processId), processId);

    private void SyncExistingStrip(ChannelStrip strip, AudioSessionInfo session, double ceiling)
    {
        if (strip.ViewModel is not { } viewModel)
        {
            return;
        }

        // Mute can be changed outside Blare, so keep the strip honest about the real OS state.
        if (viewModel.IsMuted != session.IsMuted)
        {
            viewModel.IsMuted = session.IsMuted;
        }

        if (Math.Abs(viewModel.MaxVolumePercent - ceiling) > 0.5)
        {
            viewModel.MaxVolumePercent = ceiling;
            if (viewModel.VolumePercent > ceiling)
            {
                viewModel.VolumePercent = ceiling;
            }
        }
    }

    /// <summary>
    /// Captures every process backing an app, up to a cap. Watching only the
    /// "loudest" one leaves the meter dead whenever a different process of a
    /// multi-process app is the one actually rendering.
    /// </summary>
    private void UpdateMeteredProcesses()
    {
        const int maxStreamsPerApp = 4;

        var wanted = _processesByApp.SelectMany(pair => pair.Value.Take(maxStreamsPerApp)).ToHashSet();

        foreach (var stale in _spectrumMonitor.WatchedProcesses.Where(pid => !wanted.Contains(pid)))
        {
            _spectrumMonitor.Stop(stale);
        }

        foreach (var processId in wanted)
        {
            _spectrumMonitor.Watch(processId);
        }
    }

    private static string AppKeyFor(string executablePath, uint processId) =>
        string.IsNullOrEmpty(executablePath) ? $"pid:{processId}" : executablePath.ToLowerInvariant();

    private async Task ResolveIconAsync(SessionRowViewModel viewModel, string executablePath)
    {
        var icon = await _iconResolver.ResolveAsync(executablePath);
        if (icon is not null)
        {
            viewModel.Icon = icon;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SessionRowViewModel viewModel)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(SessionRowViewModel.VolumePercent):
                CrashLog.FireAndForget(ApplyVolumeChangeAsync(viewModel));
                break;
            case nameof(SessionRowViewModel.IsMuted):
                foreach (var processId in ProcessesFor(viewModel))
                {
                    _volumeCoordinator.SetMute(processId, viewModel.IsMuted);
                }

                break;
        }
    }

    private IReadOnlyList<uint> ProcessesFor(SessionRowViewModel viewModel) =>
        _processesByApp.TryGetValue(viewModel.AppKey, out var processes)
            ? processes
            : new List<uint> { viewModel.ProcessId };

    private async Task ApplyVolumeChangeAsync(SessionRowViewModel viewModel)
    {
        // Dragging a fader unmutes, matching the Windows mixer.
        if (viewModel.IsMuted)
        {
            viewModel.IsMuted = false;
        }

        foreach (var processId in ProcessesFor(viewModel))
        {
            _volumeCoordinator.SetVolumePercent(processId, viewModel.VolumePercent);
        }

        UpdateStatusChips();

        if (!string.IsNullOrEmpty(viewModel.ExecutablePath))
        {
            await _volumeStore.SetVolumeAsync(viewModel.ExecutablePath, viewModel.VolumePercent);
        }
    }

    // ---- focus ---------------------------------------------------------------

    public async Task ToggleFocusAsync(string appKey)
    {
        if (_focusedAppKey == appKey)
        {
            await ReleaseFocusAsync();
            return;
        }

        _levelsBeforeFocus ??= CurrentLevels();
        _focusedAppKey = appKey;

        await ApplyLevelsAsync(FocusMix.Apply(CurrentLevels(), appKey));
        UpdateFocusIndicator();
    }

    public async Task ReleaseFocusAsync()
    {
        if (_levelsBeforeFocus is null)
        {
            return;
        }

        await ApplyLevelsAsync(FocusMix.Restore(_levelsBeforeFocus, _strips.Keys));

        _levelsBeforeFocus = null;
        _focusedAppKey = null;
        UpdateFocusIndicator();
    }

    private IReadOnlyList<FocusLevel> CurrentLevels() =>
        _strips
            .Where(pair => pair.Value.ViewModel is not null)
            .Select(pair => new FocusLevel(pair.Key, pair.Value.ViewModel!.VolumePercent))
            .ToList();

    private async Task ApplyLevelsAsync(IReadOnlyList<FocusLevel> levels)
    {
        foreach (var level in levels)
        {
            if (_strips.TryGetValue(level.AppKey, out var strip) && strip.ViewModel is { } viewModel)
            {
                viewModel.VolumePercent = level.VolumePercent;
                await ApplyVolumeChangeAsync(viewModel);
            }
        }
    }

    private void UpdateFocusIndicator()
    {
        foreach (var (appKey, strip) in _strips)
        {
            strip.SetFocused(_focusedAppKey == appKey);
        }
    }

    private static (string DisplayName, string ExecutablePath) ResolveProcessInfo(AudioSessionInfo session)
    {
        try
        {
            using var process = Process.GetProcessById((int)session.ProcessId);
            var path = process.MainModule?.FileName ?? string.Empty;
            var friendlyName = process.MainModule?.FileVersionInfo.FileDescription;
            var name = string.IsNullOrWhiteSpace(friendlyName) ? process.ProcessName : friendlyName;
            return (name, path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            // Process exited between enumeration and lookup, or access is restricted.
            return ($"pid {session.ProcessId}", string.Empty);
        }
    }
}
