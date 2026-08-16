using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using BLight.Blare.App.Services;
using BLight.Blare.App.ViewModels;
using BLight.Blare.Audio.Devices;
using BLight.Blare.Audio.Sessions;
using BLight.Blare.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace BLight.Blare.App.Views;

public sealed partial class MixerPage : Page
{
    private readonly AudioSessionManager _sessionManager;
    private readonly AudioDeviceManager _deviceManager;
    private readonly SessionVolumeStore _volumeStore;
    private readonly SafetyMonitor _safetyMonitor;
    private readonly BoostCoordinator _boostCoordinator;
    private readonly IconResolver _iconResolver = new();
    private readonly DispatcherQueueTimer _safetyTimer;
    private readonly DispatcherQueueTimer _meterTimer;
    private bool _suppressVolumePush;
    private bool _suppressMasterVolumePush;
    private string? _masterDeviceId;

    public ObservableCollection<SessionRowViewModel> Sessions { get; } = new();

    public MixerPage()
    {
        _sessionManager = App.Services.GetRequiredService<AudioSessionManager>();
        _deviceManager = App.Services.GetRequiredService<AudioDeviceManager>();
        _volumeStore = App.Services.GetRequiredService<SessionVolumeStore>();
        _safetyMonitor = App.Services.GetRequiredService<SafetyMonitor>();
        _boostCoordinator = App.Services.GetRequiredService<BoostCoordinator>();

        InitializeComponent();

        _safetyTimer = DispatcherQueue.CreateTimer();
        _safetyTimer.Interval = TimeSpan.FromSeconds(5);
        _safetyTimer.Tick += (_, _) => RunSafetySample();
        _safetyTimer.Start();

        // Fast enough to read as a live meter without hammering the audio APIs.
        _meterTimer = DispatcherQueue.CreateTimer();
        _meterTimer.Interval = TimeSpan.FromMilliseconds(120);
        _meterTimer.Tick += (_, _) => RefreshLiveLevels();
        _meterTimer.Start();

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _volumeStore.LoadAsync();
        await RefreshSessionsAsync();
        RefreshMasterDevice();
        UpdateStatusChips();
    }

    private void RefreshMasterDevice()
    {
        var devices = _deviceManager.GetRenderDevices();
        var defaultDevice = devices.FirstOrDefault(d => d.IsDefault);
        if (defaultDevice is null)
        {
            return;
        }

        _masterDeviceId = defaultDevice.DeviceId;
        MasterDeviceNameText.Text = defaultDevice.DisplayName;

        _suppressMasterVolumePush = true;
        MasterVolumeSlider.Value = Math.Round(_deviceManager.GetMasterVolume(defaultDevice.DeviceId) * 100);
        _suppressMasterVolumePush = false;
    }

    private void OnMasterVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressMasterVolumePush || _masterDeviceId is null)
        {
            return;
        }

        _deviceManager.SetMasterVolume(_masterDeviceId, (float)(e.NewValue / 100.0));
    }

    /// <summary>Lightweight per-tick refresh: just peak levels for the meters, not a full session/icon re-resolve.</summary>
    private void RefreshLiveLevels()
    {
        if (Sessions.Count == 0)
        {
            return;
        }

        var liveSessions = _sessionManager.GetSessionsForDefaultDevice();

        foreach (var session in liveSessions)
        {
            var row = Sessions.FirstOrDefault(r => r.ProcessId == session.ProcessId);
            if (row is null)
            {
                continue;
            }

            var target = session.PeakLevel * 100.0;
            // Attack fast (jump straight to a louder reading), decay slower
            // (ease down) so the meter reads as a level, not a strobe.
            row.MeterPercent = target > row.MeterPercent ? target : row.MeterPercent * 0.7 + target * 0.3;
        }
    }

    private void UpdateStatusChips()
    {
        BoostedCountText.Text = _boostCoordinator.BoostedCount == 1 ? "1 boosted" : $"{_boostCoordinator.BoostedCount} boosted";
        WarningCountText.Text = _safetyMonitor.WarningCount == 1 ? "1 warning" : $"{_safetyMonitor.WarningCount} warnings";
    }

    private void RunSafetySample()
    {
        var samples = Sessions.Select(row =>
            ((row.ExecutablePath is { Length: > 0 } path ? path : $"pid:{row.ProcessId}"), row.VolumePercent));

        var warned = _safetyMonitor.Sample(samples, DateTimeOffset.UtcNow);
        UpdateStatusChips();

        if (warned.Count == 0)
        {
            WarningInfoBar.IsOpen = false;
            return;
        }

        var names = Sessions
            .Where(row => warned.Contains(row.ExecutablePath is { Length: > 0 } path ? path : $"pid:{row.ProcessId}"))
            .Select(row => row.DisplayName);

        WarningInfoBar.Message = $"{string.Join(", ", names)} — running near full volume for a while. This is a relative signal level, not a measurement of sound at your ears.";
        WarningInfoBar.IsOpen = true;
    }

    public async Task RefreshSessionsAsync()
    {
        var liveSessions = _sessionManager.GetSessionsForDefaultDevice();

        // Full-set replace for now — a real diff-and-update pass belongs
        // together with SessionGroupTracker wiring, not duplicated here.
        _suppressVolumePush = true;
        Sessions.Clear();

        foreach (var session in liveSessions)
        {
            if (session.IsSystemSoundsSession)
            {
                continue;
            }

            var (displayName, executablePath) = ResolveProcessInfo(session);

            var liveVolumePercent = Math.Round(session.Volume * 100);
            var savedVolumePercent = string.IsNullOrEmpty(executablePath)
                ? null
                : _volumeStore.GetVolume(executablePath);

            if (savedVolumePercent is { } saved && Math.Abs(saved - liveVolumePercent) > 0.5)
            {
                _sessionManager.SetVolume(session.ProcessId, (float)(saved / 100.0));
            }

            var row = new SessionRowViewModel
            {
                ProcessId = session.ProcessId,
                DisplayName = string.IsNullOrWhiteSpace(session.DisplayName) ? displayName : session.DisplayName,
                ExecutablePath = executablePath,
                VolumePercent = savedVolumePercent ?? liveVolumePercent,
                MaxVolumePercent = _boostCoordinator.CurrentCeilingPercent(DateTimeOffset.UtcNow),
                IsMuted = session.IsMuted,
            };
            row.PropertyChanged += OnRowPropertyChanged;
            Sessions.Add(row);

            if (!string.IsNullOrEmpty(executablePath))
            {
                _ = ResolveIconAsync(row, executablePath);
            }
        }

        _suppressVolumePush = false;
    }

    private async Task ResolveIconAsync(SessionRowViewModel row, string executablePath)
    {
        var icon = await _iconResolver.ResolveAsync(executablePath);
        if (icon is not null)
        {
            row.Icon = icon;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressVolumePush || sender is not SessionRowViewModel row)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(SessionRowViewModel.VolumePercent):
                _ = ApplyVolumeChangeAsync(row);
                break;
            case nameof(SessionRowViewModel.IsMuted):
                _sessionManager.SetMute(row.ProcessId, row.IsMuted);
                break;
        }
    }

    private async Task ApplyVolumeChangeAsync(SessionRowViewModel row)
    {
        await _boostCoordinator.SetVolumePercentAsync(row.ProcessId, row.VolumePercent);
        row.IsBoosted = _boostCoordinator.IsBoosted(row.ProcessId);
        UpdateStatusChips();

        if (!string.IsNullOrEmpty(row.ExecutablePath))
        {
            await _volumeStore.SetVolumeAsync(row.ExecutablePath, row.VolumePercent);
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
