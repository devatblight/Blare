using System.ComponentModel;
using System.Diagnostics;
using BLight.Blare.App.Controls;
using BLight.Blare.App.Services;
using BLight.Blare.App.ViewModels;
using BLight.Blare.Audio.Analysis;
using BLight.Blare.Audio.Devices;
using BLight.Blare.Audio.Sessions;
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
    private readonly IconResolver _iconResolver = new();
    private readonly DispatcherQueueTimer _safetyTimer;
    private readonly DispatcherQueueTimer _meterTimer;
    private readonly DispatcherQueueTimer _sessionTimer;
    private readonly Dictionary<uint, ChannelStrip> _strips = new();
    private readonly double[] _bandScratch;

    private bool _suppressMasterVolumePush;
    private string? _masterDeviceId;

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
        _sessionTimer = CreateTimer(TimeSpan.FromSeconds(3), () => _ = RefreshSessionsAsync());

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
        foreach (var (processId, strip) in _strips)
        {
            if (_spectrumMonitor.TryGetBands(processId, _bandScratch))
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
        var samples = _strips.Values
            .Select(strip => strip.ViewModel)
            .Where(vm => vm is not null)
            .Select(vm => (AppKeyFor(vm!), vm!.VolumePercent));

        var warned = _safetyMonitor.Sample(samples, DateTimeOffset.UtcNow);
        UpdateStatusChips();

        if (warned.Count == 0)
        {
            WarningInfoBar.IsOpen = false;
            return;
        }

        var names = _strips.Values
            .Select(strip => strip.ViewModel)
            .Where(vm => vm is not null && warned.Contains(AppKeyFor(vm)))
            .Select(vm => vm!.DisplayName);

        WarningInfoBar.Message = $"{string.Join(", ", names)} — running near full volume for a while. This is a relative signal level, not a measurement of sound at your ears.";
        WarningInfoBar.IsOpen = true;
    }

    private static string AppKeyFor(SessionRowViewModel? viewModel) =>
        viewModel is null
            ? string.Empty
            : viewModel.ExecutablePath is { Length: > 0 } path ? path : $"pid:{viewModel.ProcessId}";

    public async Task RefreshSessionsAsync()
    {
        var liveSessions = _sessionManager.GetSessionsForDefaultDevice()
            .Where(s => !s.IsSystemSoundsSession)
            .ToList();

        var livePids = liveSessions.Select(s => s.ProcessId).ToHashSet();
        var ceiling = _boostCoordinator.CurrentCeilingPercent(DateTimeOffset.UtcNow);

        // Drop strips for apps that stopped playing.
        foreach (var goneProcessId in _strips.Keys.Where(pid => !livePids.Contains(pid)).ToList())
        {
            StripsPanel.Children.Remove(_strips[goneProcessId]);
            _strips.Remove(goneProcessId);
            _spectrumMonitor.Stop(goneProcessId);
        }

        foreach (var session in liveSessions)
        {
            if (_strips.TryGetValue(session.ProcessId, out var existingStrip))
            {
                if (existingStrip.ViewModel is { } existingViewModel)
                {
                    // Mute can be changed outside Blare (Windows' own mixer, the
                    // app itself), so keep the strip honest about the real OS state.
                    if (existingViewModel.IsMuted != session.IsMuted)
                    {
                        existingViewModel.IsMuted = session.IsMuted;
                    }

                    // The safe-boost ceiling can be granted or revoked while
                    // strips are live; pull anything now over the limit back down.
                    if (Math.Abs(existingViewModel.MaxVolumePercent - ceiling) > 0.5)
                    {
                        existingViewModel.MaxVolumePercent = ceiling;
                        if (existingViewModel.VolumePercent > ceiling)
                        {
                            existingViewModel.VolumePercent = ceiling;
                        }
                    }
                }

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

            var viewModel = new SessionRowViewModel
            {
                ProcessId = session.ProcessId,
                DisplayName = string.IsNullOrWhiteSpace(session.DisplayName) ? displayName : session.DisplayName,
                ExecutablePath = executablePath,
                VolumePercent = Math.Min(savedVolumePercent ?? liveVolumePercent, ceiling),
                MaxVolumePercent = ceiling,
                IsMuted = session.IsMuted,
            };
            viewModel.PropertyChanged += OnViewModelPropertyChanged;

            var strip = new ChannelStrip();
            strip.Bind(viewModel);

            _strips[session.ProcessId] = strip;
            StripsPanel.Children.Add(strip);
            _spectrumMonitor.Watch(session.ProcessId);

            if (!string.IsNullOrEmpty(executablePath))
            {
                _ = ResolveIconAsync(viewModel, executablePath);
            }
        }

        EmptyStateText.Visibility = _strips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

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
                _sessionManager.SetMute(viewModel.ProcessId, viewModel.IsMuted);
                break;
        }
    }

    private async Task ApplyVolumeChangeAsync(SessionRowViewModel viewModel)
    {
        // Dragging a fader unmutes, matching the Windows mixer. Reflect that in
        // the view model too, otherwise the strip keeps showing a mute state the
        // OS no longer has.
        if (viewModel.IsMuted)
        {
            viewModel.IsMuted = false;
        }

        await _boostCoordinator.SetVolumePercentAsync(viewModel.ProcessId, viewModel.VolumePercent);
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
