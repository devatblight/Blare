using System.ComponentModel;
using System.Diagnostics;
using Blight.Blare.App.Controls;
using Blight.Blare.App.Services;
using Blight.Blare.App.ViewModels;
using Blight.Blare.Audio.Analysis;
using Blight.Blare.Audio.Devices;
using Blight.Blare.Audio.Sessions;
using Blight.Blare.Core.Mixing;
using Blight.Blare.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Blight.Blare.App.Views;

public sealed partial class MixerPage : Page
{
    private readonly AudioSessionManager _sessionManager;
    private readonly AudioDeviceManager _deviceManager;
    private readonly SessionVolumeStore _volumeStore;
    private readonly SafetyMonitor _safetyMonitor;
    private readonly BoostCoordinator _boostCoordinator;
    private readonly SpectrumMonitor _spectrumMonitor;
    private readonly MonitorVolumeController _monitorVolume;
    private readonly SessionGroupTracker _groupTracker = new();
    private readonly IconResolver _iconResolver = new();
    private readonly DispatcherQueueTimer _safetyTimer;
    private readonly DispatcherQueueTimer _meterTimer;
    private readonly DispatcherQueueTimer _sessionTimer;

    /// <summary>Strips are keyed by app identity, not process id — a browser spreads audio across many processes but is one thing to the user.</summary>
    private readonly Dictionary<string, ChannelStrip> _strips = new();

    /// <summary>Every live process backing each app strip, so a fader moves all of them.</summary>
    private readonly Dictionary<string, List<uint>> _processesByApp = new();

    private readonly double[] _bandScratch;

    private bool _suppressMasterVolumePush;
    private string? _masterDeviceId;

    /// <summary>Levels captured before focus was engaged, so releasing focus puts the desk back exactly as it was.</summary>
    private IReadOnlyList<FocusLevel>? _levelsBeforeFocus;
    private string? _focusedAppKey;

    public MixerPage()
    {
        _sessionManager = App.Services.GetRequiredService<AudioSessionManager>();
        _deviceManager = App.Services.GetRequiredService<AudioDeviceManager>();
        _volumeStore = App.Services.GetRequiredService<SessionVolumeStore>();
        _safetyMonitor = App.Services.GetRequiredService<SafetyMonitor>();
        _boostCoordinator = App.Services.GetRequiredService<BoostCoordinator>();
        _spectrumMonitor = App.Services.GetRequiredService<SpectrumMonitor>();
        _monitorVolume = App.Services.GetRequiredService<MonitorVolumeController>();
        _bandScratch = new double[_spectrumMonitor.BandCount];

        InitializeComponent();

        _safetyTimer = CreateTimer(TimeSpan.FromSeconds(5), RunSafetySample);
        _meterTimer = CreateTimer(TimeSpan.FromMilliseconds(50), RefreshMeters);
        _sessionTimer = CreateTimer(TimeSpan.FromSeconds(2), () => CrashLog.FireAndForget(RefreshSessionsAsync()));

        // Boost lapses on its own after 30 minutes, or if its pipeline fails —
        // the fader has to follow it back down rather than keep claiming 180%.
        _boostCoordinator.BoostEnded += OnBoostEnded;

        Unloaded += (_, _) =>
        {
            _boostCoordinator.BoostEnded -= OnBoostEnded;
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
        RefreshMasterDevice();
        BuildHardwareControls();
        await RefreshSessionsAsync();
        UpdateStatusChips();
    }

    /// <summary>
    /// Adds a row per display that exposes its speaker volume over DDC/CI.
    ///
    /// Built once rather than polled: DDC/CI is a slow serial channel and
    /// hammering it makes monitors unresponsive. Displays that don't answer
    /// are simply left out, and the whole section hides when none do.
    /// </summary>
    private void BuildHardwareControls()
    {
        try
        {
            var controls = _monitorVolume.GetControls().Where(c => c.SupportsVolume).ToList();

            HardwareRows.Children.Clear();

            foreach (var control in controls)
            {
                HardwareRows.Children.Add(BuildHardwareRow(control));
            }

            HardwarePanel.Visibility = controls.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            // A monitor refusing DDC/CI must never break the mixer.
            CrashLog.Write(ex);
            HardwarePanel.Visibility = Visibility.Collapsed;
        }
    }

    private UIElement BuildHardwareRow(Audio.Devices.MonitorAudioControl control)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = control.DisplayName,
            FontSize = 11.5,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var readout = new TextBlock
        {
            Text = $"{control.VolumePercent:F0}%",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            MinWidth = 42,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = control.VolumePercent,
            VerticalAlignment = VerticalAlignment.Center,
        };

        slider.ValueChanged += (_, e) =>
        {
            readout.Text = $"{e.NewValue:F0}%";

            // Writes go straight to the display; a refusal is reported rather
            // than silently leaving the slider somewhere the hardware isn't.
            if (!_monitorVolume.TrySetVolumePercent(control.Index, e.NewValue))
            {
                readout.Text = "n/a";
            }
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(readout, 2);
        grid.Children.Add(label);
        grid.Children.Add(slider);
        grid.Children.Add(readout);

        return grid;
    }

    private void RefreshMasterDevice()
    {
        var defaultDevice = _deviceManager.GetRenderDevices().FirstOrDefault(d => d.IsDefault);
        if (defaultDevice is null)
        {
            return;
        }

        _masterDeviceId = defaultDevice.DeviceId;
        MasterDeviceNameText.Text = defaultDevice.DisplayName;

        _suppressMasterVolumePush = true;
        MasterVolumeSlider.Value = Math.Round(_deviceManager.GetMasterVolume(defaultDevice.DeviceId) * 100);
        _suppressMasterVolumePush = false;
        MasterVolumeText.Text = $"{MasterVolumeSlider.Value:F0}%";
    }

    private void OnMasterVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        MasterVolumeText.Text = $"{e.NewValue:F0}%";

        if (_suppressMasterVolumePush || _masterDeviceId is null)
        {
            return;
        }

        _deviceManager.SetMasterVolume(_masterDeviceId, (float)(e.NewValue / 100.0));
    }

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
        var boosted = _boostCoordinator.BoostedCount;
        var warnings = _safetyMonitor.WarningCount;
        var minutesLoud = _safetyMonitor.TotalTimeAboveThreshold.TotalMinutes;

        BoostedCountText.Text = boosted == 0 ? "no boost" : $"{boosted} boosted";
        WarningCountText.Text = warnings switch
        {
            0 => "no warnings",
            1 => "1 warning",
            _ => $"{warnings} warnings",
        };
        ExposureText.Text = $"{minutesLoud:F0}m loud today";

        // Dots only light when there's something to report, so a calm desk reads as calm.
        BoostDot.Fill = BrushFor(boosted > 0 ? "BlareMeterHigh" : "BlareMeterUnlit");
        WarningDot.Fill = BrushFor(warnings > 0 ? "BlareMeterMid" : "BlareMeterUnlit");
        ExposureDot.Fill = BrushFor(minutesLoud >= 60 ? "BlareMeterMid" : minutesLoud > 0 ? "BlareMeterLow" : "BlareMeterUnlit");
    }

    private static Microsoft.UI.Xaml.Media.Brush BrushFor(string resourceKey) =>
        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[resourceKey];

    private void RunSafetySample()
    {
        // Loudness is judged from measured output, not slider positions — see
        // SafetyMonitor. Peaks come straight from the live sessions, taking the
        // loudest process where an app spans several.
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

        var masterVolume = _masterDeviceId is null ? 1.0 : _deviceManager.GetMasterVolume(_masterDeviceId);
        var warned = _safetyMonitor.Sample(
            peaksByApp.Select(pair => (pair.Key, pair.Value)),
            masterVolume,
            DateTimeOffset.UtcNow);

        UpdateStatusChips();

        if (warned.Count == 0)
        {
            WarningInfoBar.IsOpen = false;
            return;
        }

        var names = _strips
            .Where(pair => warned.Contains(pair.Key) && pair.Value.ViewModel is not null)
            .Select(pair => pair.Value.ViewModel!.DisplayName);

        WarningInfoBar.Message = $"{string.Join(", ", names)} — running near full volume for a while. This is a relative signal level, not a measurement of sound at your ears.";
        WarningInfoBar.IsOpen = true;
    }

    public async Task RefreshSessionsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var ceiling = _boostCoordinator.CurrentCeilingPercent(now);

        var liveSessions = _sessionManager.GetSessionsForDefaultDevice()
            .Where(s => !s.IsSystemSoundsSession)
            .Select(session =>
            {
                var (displayName, executablePath) = ResolveProcessInfo(session);
                return (Session: session, DisplayName: displayName, ExecutablePath: executablePath);
            })
            .ToList();

        // Group by app identity. Passing Guid.Empty makes the tracker key purely
        // on that identity, which is what collapses a browser's many audio
        // processes into a single strip; the tracker still supplies the
        // debounce so a session briefly disappearing doesn't flicker the desk.
        var snapshots = liveSessions
            .Select(entry => new SessionSnapshot(
                AppKeyFor(entry.ExecutablePath, entry.Session.ProcessId),
                Guid.Empty,
                entry.Session.ProcessId))
            .ToList();

        var rows = _groupTracker.Reconcile(snapshots, now);
        var survivingKeys = rows.Select(row => row.GroupKey).ToHashSet();

        // Remove strips the tracker has finished debouncing away.
        foreach (var goneKey in _strips.Keys.Where(key => !survivingKeys.Contains(key)).ToList())
        {
            StripsPanel.Children.Remove(_strips[goneKey]);
            _strips.Remove(goneKey);
            _processesByApp.Remove(goneKey);
        }

        _processesByApp.Clear();
        foreach (var entry in liveSessions)
        {
            var appKey = AppKeyFor(entry.ExecutablePath, entry.Session.ProcessId);
            var groupKey = SessionGroupTracker.ComputeGroupKey(Guid.Empty, appKey, entry.Session.ProcessId);

            if (!_processesByApp.TryGetValue(groupKey, out var processes))
            {
                processes = new List<uint>();
                _processesByApp[groupKey] = processes;
            }

            processes.Add(entry.Session.ProcessId);
        }

        foreach (var entry in liveSessions)
        {
            var appKey = AppKeyFor(entry.ExecutablePath, entry.Session.ProcessId);
            var groupKey = SessionGroupTracker.ComputeGroupKey(Guid.Empty, appKey, entry.Session.ProcessId);

            if (_strips.TryGetValue(groupKey, out var existingStrip))
            {
                SyncExistingStrip(existingStrip, entry.Session, ceiling);
                continue;
            }

            var liveVolumePercent = Math.Round(entry.Session.Volume * 100);

            // Clamp to the current ceiling: stored levels can predate a lower
            // ceiling (a 150% value saved while boost sliders went that high),
            // and restoring one raw would be out of range for the audio APIs.
            double? savedVolumePercent = null;
            if (!string.IsNullOrEmpty(entry.ExecutablePath)
                && _volumeStore.GetVolume(entry.ExecutablePath) is { } stored)
            {
                savedVolumePercent = Math.Clamp(stored, 0, ceiling);
            }

            if (savedVolumePercent is { } saved && Math.Abs(saved - liveVolumePercent) > 0.5)
            {
                _sessionManager.SetVolume(entry.Session.ProcessId, (float)(saved / 100.0));
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
            StripsPanel.Children.Add(strip);

            if (!string.IsNullOrEmpty(entry.ExecutablePath))
            {
                CrashLog.FireAndForget(ResolveIconAsync(viewModel, entry.ExecutablePath));
            }
        }

        UpdateMeteredProcesses();

        EmptyStateText.Visibility = _strips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncExistingStrip(ChannelStrip strip, AudioSessionInfo session, double ceiling)
    {
        if (strip.ViewModel is not { } viewModel)
        {
            return;
        }

        // Mute can be changed outside Blare (Windows' own mixer, the app itself),
        // so keep the strip honest about the real OS state.
        if (viewModel.IsMuted != session.IsMuted)
        {
            viewModel.IsMuted = session.IsMuted;
        }

        // The safe-boost ceiling can be granted or revoked while strips are
        // live; pull anything now over the limit back down.
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
    /// Captures every process backing an app, up to a cap.
    ///
    /// Watching only the "loudest" process doesn't work: which process of a
    /// multi-process app is actually rendering changes moment to moment, and
    /// picking one by an instantaneous peak reading leaves the meter dead
    /// whenever the sound is coming from a different one. Bands are merged by
    /// taking the loudest per band.
    /// </summary>
    private void UpdateMeteredProcesses()
    {
        const int maxStreamsPerApp = 4;

        var wanted = _processesByApp
            .SelectMany(pair => pair.Value.Take(maxStreamsPerApp))
            .ToHashSet();

        foreach (var stale in _spectrumMonitor.WatchedProcesses.Where(pid => !wanted.Contains(pid)))
        {
            _spectrumMonitor.Stop(stale);
        }

        foreach (var processId in wanted)
        {
            _spectrumMonitor.Watch(processId);
        }
    }

    /// <summary>Makes one app dominant by ducking the rest, or releases a focus already in place.</summary>
    public async Task ToggleFocusAsync(string appKey)
    {
        // Focusing a second app while one is already focused should swap, not stack.
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

    private void OnFocusBannerClosed(InfoBar sender, object args) => CrashLog.FireAndForget(ReleaseFocusAsync());

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

        FocusBanner.IsOpen = _focusedAppKey is not null;

        if (_focusedAppKey is not null && _strips.TryGetValue(_focusedAppKey, out var focusedStrip))
        {
            FocusBanner.Message = $"{focusedStrip.ViewModel?.DisplayName} is in focus — everything else is ducked. Raise master to bring the level up.";
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
                    _sessionManager.SetMute(processId, viewModel.IsMuted);
                }

                break;
        }
    }

    private void OnBoostEnded(object? sender, uint processId)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var strip = _strips.Values.FirstOrDefault(s => s.ViewModel?.ProcessId == processId);

            if (strip?.ViewModel is not { } viewModel)
            {
                return;
            }

            viewModel.VolumePercent = viewModel.LastUnboostedPercent;
            viewModel.IsBoosted = false;
            UpdateStatusChips();
        });
    }

    private IReadOnlyList<uint> ProcessesFor(SessionRowViewModel viewModel) =>
        _processesByApp.TryGetValue(viewModel.AppKey, out var processes)
            ? processes
            : new List<uint> { viewModel.ProcessId };

    private async Task ApplyVolumeChangeAsync(SessionRowViewModel viewModel)
    {
        // Dragging a fader unmutes, matching the Windows mixer. Reflect that in
        // the view model too, otherwise the strip keeps showing a mute state the
        // OS no longer has.
        if (viewModel.IsMuted)
        {
            viewModel.IsMuted = false;
        }

        var processes = ProcessesFor(viewModel);

        // Boost captures and re-renders one stream, so it only ever targets the
        // representative process; plain volume applies to all of them.
        if (viewModel.VolumePercent > 100)
        {
            _boostCoordinator.RememberName(viewModel.ProcessId, viewModel.DisplayName);
            await _boostCoordinator.SetVolumePercentAsync(
                viewModel.ProcessId, viewModel.VolumePercent, viewModel.LastUnboostedPercent);
        }
        else
        {
            viewModel.LastUnboostedPercent = viewModel.VolumePercent;

            foreach (var processId in processes)
            {
                await _boostCoordinator.SetVolumePercentAsync(processId, viewModel.VolumePercent, viewModel.VolumePercent);
            }
        }

        viewModel.IsBoosted = _boostCoordinator.IsBoosted(viewModel.ProcessId);
        UpdateStatusChips();

        if (!string.IsNullOrEmpty(viewModel.ExecutablePath))
        {
            await _volumeStore.SetVolumeAsync(viewModel.ExecutablePath, viewModel.VolumePercent);
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
            // Process exited between enumeration and lookup, or access is restricted (protected process).
            return ($"pid {session.ProcessId}", string.Empty);
        }
    }
}
