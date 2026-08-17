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
using Blight.Blare.Core.Scenes;
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
    private readonly LimitsStore _limits;
    private readonly FlyoutService _flyout;
    private readonly SceneStore _scenes;
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
    private UIElement? _emptyState;
    private Slider? _masterVolumeSlider;
    private TextBlock? _masterVolumeText;
    private TextBlock? _masterDeviceNameText;
    private TextBlock? _warningCountText;
    private TextBlock? _exposureText;
    private Ellipse? _warningDot;
    private Ellipse? _exposureDot;
    private TextBlock? _nowPlayingText;
    private TextBlock? _nowPlayingDetail;
    private StackPanel? _scenesPanel;

    private bool _suppressMasterVolumePush;
    private string? _masterDeviceId;

    /// <summary>The band the mixer card is currently in, so strips created later match the ones already there.</summary>
    private CardDensity _stripDensity = CardDensity.Normal;

    /// <summary>Levels captured before focus was engaged, so releasing focus puts the desk back exactly as it was.</summary>
    private IReadOnlyList<FocusLevel>? _levelsBeforeFocus;
    private string? _focusedAppKey;

    /// <summary>Mute states captured before solo, so clearing it restores them rather than unmuting everything.</summary>
    private readonly Dictionary<string, bool> _mutesBeforeSolo = new();
    private string? _soloedAppKey;

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
        _limits = App.Services.GetRequiredService<LimitsStore>();
        _flyout = App.Services.GetRequiredService<FlyoutService>();
        _scenes = App.Services.GetRequiredService<SceneStore>();
        _bandScratch = new double[_spectrumMonitor.BandCount];

        InitializeComponent();
        BuildAddCardMenu();
        BuildDropPreview();

        _safetyTimer = CreateTimer(TimeSpan.FromSeconds(5), RunSafetySample);
        // 30fps. The meter now only rewrites segments that actually changed, so
        // the extra frames cost little and buy visibly smoother ballistics.
        _meterTimer = CreateTimer(TimeSpan.FromMilliseconds(33), RefreshMeters);
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
        _dropPreview.Opacity = 0;

        if (Application.Current.Resources.TryGetValue("BlareAccent", out var accent) && accent is Brush brush)
        {
            _dropPreview.Stroke = brush;
            _dropPreview.Fill = brush;
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
        _scenesPanel = null;
        _masterVolumeSlider = null;
        _warningCountText = null;
        _nowPlayingText = null;
        _strips.Clear();

        var index = 0;

        foreach (var card in _dashboardStore.Layout.Cards)
        {
            // Built at Normal; the first layout pass reports the card's real size
            // and rebuilds it in the right band if that isn't what it got.
            var host = new DashboardCardHost(card, TitleFor(card.Kind), BuildCardContent(card.Kind, CardDensity.Normal));
            host.Previewing += OnCardPreviewing;
            host.Committed += OnCardCommitted;
            host.RemoveButton.Click += (_, _) => RemoveCard(card.Id);
            host.DensityChanged += OnCardDensityChanged;
            host.SetEditing(EditModeToggle.IsChecked == true);
            host.PlayEntrance(index++);

            _hosts.Add(host);
            CardCanvas.Children.Add(host);
        }

        LayoutCards();
        CrashLog.FireAndForget(RefreshSessionsAsync());
    }

    /// <summary>
    /// A card has crossed into a different size band, so its content is rebuilt
    /// for the room it now has.
    ///
    /// The mixer is the exception: its strips already know how to lay themselves
    /// out, and rebuilding the card would throw away every meter's state and
    /// re-resolve every icon for no reason.
    /// </summary>
    private void OnCardDensityChanged(object? sender, CardDensity density)
    {
        if (sender is not DashboardCardHost host)
        {
            return;
        }

        if (host.Card.Kind == CardKind.AppMixer)
        {
            _stripDensity = density;

            foreach (var strip in _strips.Values)
            {
                strip.SetDensity(density);
            }

            return;
        }

        host.SetBody(BuildCardContent(host.Card.Kind, density));
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

        if (_dropPreview.Visibility != Visibility.Visible)
        {
            Motion.ToggleLayer(_dropPreview, true, Motion.Fast, shownOpacity: 0.18);
        }
    }

    private void OnCardCommitted(object? sender, Blight.Blare.Core.Layout.DashboardCard card)
    {
        Motion.ToggleLayer(_dropPreview, false);

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

        Motion.ToggleLayer(EditTools, editing, Motion.Normal);
        Motion.ToggleLayer(GridGuides, editing, Motion.Normal, shownOpacity: 0.9);

        foreach (var host in _hosts)
        {
            host.SetEditing(editing);
        }

        DrawGuides();
    }

    /// <summary>Faint cell lines while editing, so the snap grid is visible rather than guessed at.</summary>
    private void DrawGuides()
    {
        // Leaving edit mode fades the guide layer out. Clearing it here would
        // delete the lines mid-fade and turn that into a blink, so the stale
        // lines are left in the hidden canvas and cleared on the way back in.
        if (EditModeToggle.IsChecked != true)
        {
            return;
        }

        GridGuides.Children.Clear();

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

    /// <summary>
    /// Mutes everything except one app.
    ///
    /// Distinct from focus, which ducks the others by level: solo is absolute
    /// and reversible, and clicking it again puts every mute back exactly as it
    /// was rather than unmuting things the user had muted themselves.
    /// </summary>
    private void Solo(string appKey)
    {
        if (_soloedAppKey == appKey)
        {
            foreach (var (key, strip) in _strips)
            {
                if (strip.ViewModel is { } viewModel && _mutesBeforeSolo.TryGetValue(key, out var wasMuted))
                {
                    viewModel.IsMuted = wasMuted;
                }
            }

            _soloedAppKey = null;
            _mutesBeforeSolo.Clear();
            UpdateHeader();
            return;
        }

        _mutesBeforeSolo.Clear();

        foreach (var (key, strip) in _strips)
        {
            if (strip.ViewModel is not { } viewModel)
            {
                continue;
            }

            _mutesBeforeSolo[key] = viewModel.IsMuted;
            viewModel.IsMuted = key != appKey;
        }

        _soloedAppKey = appKey;
        UpdateHeader();
    }

    // ---- scenes --------------------------------------------------------------

    /// <summary>Captures every strip's level and mute under a name.</summary>
    private void SaveScene(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _flyout.Show("Name it first", "A scene needs a name to be recalled by.", FlyoutTone.Caution, TimeSpan.FromSeconds(3));
            return;
        }

        var levels = _strips.Values
            .Select(strip => strip.ViewModel)
            .Where(viewModel => viewModel is not null)
            .Select(viewModel => new SceneLevel(
                string.IsNullOrEmpty(viewModel!.ExecutablePath) ? viewModel.AppKey : viewModel.ExecutablePath,
                viewModel.VolumePercent,
                viewModel.IsMuted))
            .ToList();

        _scenes.Book.Save(new Scene(name.Trim(), levels));
        RefreshScenes();

        _flyout.Show(name.Trim(), $"Saved {levels.Count} levels.", FlyoutTone.Neutral, TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// Puts the desk back into a scene.
    ///
    /// Apps in the scene that aren't running are skipped rather than treated as
    /// an error — a gaming scene naming five apps is still useful when only two
    /// of them are open.
    /// </summary>
    private void RecallScene(string name)
    {
        if (_scenes.Book.Get(name) is not { } scene)
        {
            return;
        }

        var applied = 0;

        foreach (var strip in _strips.Values)
        {
            if (strip.ViewModel is not { } viewModel)
            {
                continue;
            }

            var key = string.IsNullOrEmpty(viewModel.ExecutablePath) ? viewModel.AppKey : viewModel.ExecutablePath;

            if (scene.For(key) is not { } level)
            {
                continue;
            }

            viewModel.VolumePercent = level.VolumePercent;
            viewModel.IsMuted = level.IsMuted;
            applied++;
        }

        _flyout.Show(scene.Name, applied == 0 ? "None of its apps are running." : $"Restored {applied} levels.",
            FlyoutTone.Neutral, TimeSpan.FromSeconds(3));
    }

    /// <summary>Redraws the scene list after the book changes.</summary>
    private void RefreshScenes()
    {
        if (_scenesPanel is null)
        {
            return;
        }

        _scenesPanel.Children.Clear();

        if (_scenes.Book.Scenes.Count == 0)
        {
            _scenesPanel.Children.Add(new TextBlock
            {
                Text = "No scenes yet. Set your levels, then save them.",
                FontSize = 11.5,
                Opacity = 0.5,
                TextWrapping = TextWrapping.Wrap,
            });

            return;
        }

        foreach (var scene in _scenes.Book.Scenes)
        {
            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var recall = new Button
            {
                Content = new TextBlock { Text = scene.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 4, 10, 4),
            };

            var name = scene.Name;
            recall.Click += (_, _) => RecallScene(name);

            var remove = new Button
            {
                Content = new FontIcon { Glyph = char.ConvertFromUtf32(0xE711), FontSize = 11 },
                Padding = new Thickness(7, 4, 7, 4),
            };

            ToolTipService.SetToolTip(remove, $"Delete {name}");
            remove.Click += (_, _) =>
            {
                _scenes.Book.Remove(name);
                RefreshScenes();
            };

            Grid.SetColumn(recall, 0);
            Grid.SetColumn(remove, 1);
            row.Children.Add(recall);
            row.Children.Add(remove);

            _scenesPanel.Children.Add(row);
        }
    }

    private void SetLimit(SessionRowViewModel viewModel, double? ceiling)
    {
        var key = string.IsNullOrEmpty(viewModel.ExecutablePath) ? viewModel.AppKey : viewModel.ExecutablePath;

        if (ceiling is null)
        {
            _limits.Limits.ClearCap(key);
            _flyout.Show(viewModel.DisplayName, "Limit removed.", FlyoutTone.Neutral, TimeSpan.FromSeconds(3));
            return;
        }

        _limits.Limits.SetCap(key, ceiling.Value);
        _flyout.Show(viewModel.DisplayName, $"Never above {ceiling:F0}%.", FlyoutTone.Neutral, TimeSpan.FromSeconds(3));

        // Applies immediately rather than only next time the fader moves — a
        // limit that leaves the app loud until you touch it isn't a limit.
        if (viewModel.VolumePercent > ceiling.Value)
        {
            viewModel.VolumePercent = ceiling.Value;
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
                continue;
            }

            // No capture for this app this frame — let its meter fall away rather
            // than leaving it frozen at whatever it last showed.
            strip.DecayLevels();
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

        // Blare holds an audio session of its own and turns up on the desk as a
        // strip you can fade. It plays nothing, so that strip is pure noise.
        var ownProcessId = (uint)Environment.ProcessId;

        var liveSessions = _sessionManager.GetSessionsForDefaultDevice()
            .Where(s => !s.IsSystemSoundsSession && s.ProcessId != ownProcessId)
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
            strip.SetDensity(_stripDensity);
            strip.Bind(viewModel);
            strip.FocusRequested += (_, key) => CrashLog.FireAndForget(ToggleFocusAsync(key));
            strip.SoloRequested += (_, key) => Solo(key);
            strip.LimitRequested += (_, ceiling) => SetLimit(viewModel, ceiling);

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
            Motion.ToggleLayer(_emptyState, _strips.Count == 0, Motion.Normal);
        }

        UpdateHeader();
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

        // Enforced here, at the one place a level reaches the audio API, so a
        // cap holds no matter what moved the fader — the slider, a hotkey, a
        // scene or a restored level from a previous session.
        var limitKey = string.IsNullOrEmpty(viewModel.ExecutablePath) ? viewModel.AppKey : viewModel.ExecutablePath;
        var allowed = _limits.Limits.Apply(limitKey, viewModel.VolumePercent, TimeOnly.FromDateTime(DateTime.Now));

        if (allowed < viewModel.VolumePercent)
        {
            // Pushes the fader back to the ceiling so the UI never shows a level
            // the app isn't actually at.
            viewModel.VolumePercent = allowed;
            return;
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

        UpdateHeader();
    }

    /// <summary>Keeps the page subtitle saying what the desk is actually doing right now.</summary>
    private void UpdateHeader()
    {
        var summary = _strips.Count switch
        {
            0 => "Nothing playing",
            1 => "1 app playing",
            var count => $"{count} apps playing",
        };

        if (_focusedAppKey is not null
            && _strips.TryGetValue(_focusedAppKey, out var focused)
            && focused.ViewModel is { } viewModel)
        {
            summary += $" · focused on {viewModel.DisplayName}";
        }

        if (_soloedAppKey is not null
            && _strips.TryGetValue(_soloedAppKey, out var soloed)
            && soloed.ViewModel is { } soloModel)
        {
            summary += $" · solo {soloModel.DisplayName}";
        }

        if (_limits.Limits.QuietHours.Contains(TimeOnly.FromDateTime(DateTime.Now)))
        {
            summary += $" · quiet hours, {_limits.Limits.QuietHours.CeilingPercent:F0}% ceiling";
        }

        HeaderSubtitle.Text = summary;
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
