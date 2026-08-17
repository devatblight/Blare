using System.ComponentModel;
using System.Diagnostics;
using BLight.Blare.App.Controls;
using BLight.Blare.App.Services;
using BLight.Blare.App.ViewModels;
using BLight.Blare.Audio.Analysis;
using BLight.Blare.Audio.Devices;
using BLight.Blare.Audio.Sessions;
using BLight.Blare.Core.Mixing;
using BLight.Blare.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BLight.Blare.App.Views;

public sealed partial class MixerPage : Page
{
    private readonly AudioSessionManager _sessionManager;
    private readonly AudioDeviceManager _deviceManager;
    private readonly SessionVolumeStore _volumeStore;
    private readonly SafetyMonitor _safetyMonitor;
    private readonly BoostCoordinator _boostCoordinator;
    private readonly SpectrumMonitor _spectrumMonitor;
    private readonly SessionGroupTracker _groupTracker = new();
    private readonly IconResolver _iconResolver = new();
    private readonly DispatcherQueueTimer _safetyTimer;
    private readonly DispatcherQueueTimer _meterTimer;
    private readonly DispatcherQueueTimer _sessionTimer;

    /// <summary>Strips are keyed by app identity, not process id — a browser spreads audio across many processes but is one thing to the user.</summary>
    private readonly Dictionary<string, ChannelStrip> _strips = new();

    /// <summary>Every live process backing each app strip, so a fader moves all of them.</summary>
    private readonly Dictionary<string, List<uint>> _processesByApp = new();

    /// <summary>The one process per app we run spectrum capture on — capture streams are too costly to run for every renderer.</summary>
    private readonly Dictionary<string, uint> _meteredProcessByApp = new();

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
        _bandScratch = new double[_spectrumMonitor.BandCount];

        InitializeComponent();

        _safetyTimer = CreateTimer(TimeSpan.FromSeconds(5), RunSafetySample);
        _meterTimer = CreateTimer(TimeSpan.FromMilliseconds(50), RefreshMeters);
        _sessionTimer = CreateTimer(TimeSpan.FromSeconds(2), () => _ = RefreshSessionsAsync());

        Unloaded += (_, _) =>
        {
            _safetyTimer.Stop();
            _meterTimer.Stop();
            _sessionTimer.Stop();
            // Capture streams are expensive — never leave them running for a page nobody's looking at.
            _spectrumMonitor.StopAll();
        };

        _ = InitializeAsync();
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
        await RefreshSessionsAsync();
        UpdateStatusChips();
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
            if (_meteredProcessByApp.TryGetValue(appKey, out var processId)
                && _spectrumMonitor.TryGetBands(processId, _bandScratch))
            {
                strip.SetLevels(_bandScratch);
            }
        }
    }

    private void UpdateStatusChips()
    {
        BoostedCountText.Text = _boostCoordinator.BoostedCount.ToString();
        WarningCountText.Text = _safetyMonitor.WarningCount.ToString();

        var minutesLoud = _safetyMonitor.TotalTimeAboveThreshold.TotalMinutes;
        ExposureText.Text = $"{minutesLoud:F0}m";
        // Bar fills across a nominal 60-minute reference so it reads as a
        // trend, not a clinical dose measurement.
        ExposureBar.Value = Math.Min(100, minutesLoud / 60.0 * 100);
    }

    private void RunSafetySample()
    {
        var samples = _strips
            .Where(pair => pair.Value.ViewModel is not null)
            .Select(pair => (pair.Key, pair.Value.ViewModel!.VolumePercent));

        var warned = _safetyMonitor.Sample(samples, DateTimeOffset.UtcNow);
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

            if (_meteredProcessByApp.Remove(goneKey, out var meteredProcessId))
            {
                _spectrumMonitor.Stop(meteredProcessId);
            }
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
            var savedVolumePercent = string.IsNullOrEmpty(entry.ExecutablePath)
                ? null
                : _volumeStore.GetVolume(entry.ExecutablePath);

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
            strip.FocusRequested += (_, key) => _ = ToggleFocusAsync(key);

            _strips[groupKey] = strip;
            StripsPanel.Children.Add(strip);

            if (!string.IsNullOrEmpty(entry.ExecutablePath))
            {
                _ = ResolveIconAsync(viewModel, entry.ExecutablePath);
            }
        }

        UpdateMeteredProcesses(liveSessions);

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

    /// <summary>Picks the loudest process per app to visualise, so a browser gets one capture stream rather than one per tab.</summary>
    private void UpdateMeteredProcesses(List<(AudioSessionInfo Session, string DisplayName, string ExecutablePath)> liveSessions)
    {
        foreach (var group in liveSessions.GroupBy(entry =>
                     SessionGroupTracker.ComputeGroupKey(
                         Guid.Empty,
                         AppKeyFor(entry.ExecutablePath, entry.Session.ProcessId),
                         entry.Session.ProcessId)))
        {
            var loudest = group.OrderByDescending(entry => entry.Session.PeakLevel).First().Session.ProcessId;

            if (_meteredProcessByApp.TryGetValue(group.Key, out var current))
            {
                if (current == loudest)
                {
                    continue;
                }

                _spectrumMonitor.Stop(current);
            }

            _meteredProcessByApp[group.Key] = loudest;
            _spectrumMonitor.Watch(loudest);
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

    private void OnFocusBannerClosed(InfoBar sender, object args) => _ = ReleaseFocusAsync();

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
                _ = ApplyVolumeChangeAsync(viewModel);
                break;
            case nameof(SessionRowViewModel.IsMuted):
                foreach (var processId in ProcessesFor(viewModel))
                {
                    _sessionManager.SetMute(processId, viewModel.IsMuted);
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
        // Dragging a fader unmutes, matching the Windows mixer. Reflect that in
        // the view model too, otherwise the strip keeps showing a mute state the
        // OS no longer has.
        if (viewModel.IsMuted)
        {
            viewModel.IsMuted = false;
        }

        foreach (var processId in ProcessesFor(viewModel))
        {
            await _boostCoordinator.SetVolumePercentAsync(processId, viewModel.VolumePercent);
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
