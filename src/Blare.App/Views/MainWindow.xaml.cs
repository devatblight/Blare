using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using BLight.Blare.App.Services;
using BLight.Blare.App.ViewModels;
using BLight.Blare.App.Views;
using BLight.Blare.Audio.Sessions;
using BLight.Blare.Core.Settings;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT;

namespace BLight.Blare.App;

public sealed partial class MainWindow : Window
{
    private readonly AudioSessionManager _sessionManager;
    private readonly SessionVolumeStore _volumeStore;
    private readonly SafetyMonitor _safetyMonitor;
    private readonly IconResolver _iconResolver = new();
    private readonly DispatcherQueueTimer _safetyTimer;
    private MicaController? _micaController;
    private SystemBackdropConfiguration? _backdropConfiguration;
    private bool _suppressVolumePush;

    public ObservableCollection<SessionRowViewModel> Sessions { get; } = new();

    public MainWindow(AudioSessionManager sessionManager, SessionVolumeStore volumeStore, SafetyMonitor safetyMonitor)
    {
        _sessionManager = sessionManager;
        _volumeStore = volumeStore;
        _safetyMonitor = safetyMonitor;
        InitializeComponent();

        Title = "Blare";
        TrySetMicaBackdrop();

        _safetyTimer = DispatcherQueue.CreateTimer();
        _safetyTimer.Interval = TimeSpan.FromSeconds(5);
        _safetyTimer.Tick += (_, _) => RunSafetySample();
        _safetyTimer.Start();

        UpdateToggleWarningsButtonText();

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _volumeStore.LoadAsync();
        await RefreshSessionsAsync();
    }

    private void RunSafetySample()
    {
        var samples = Sessions.Select(row =>
            ((row.ExecutablePath is { Length: > 0 } path ? path : $"pid:{row.ProcessId}"), row.VolumePercent));

        var warned = _safetyMonitor.Sample(samples, DateTimeOffset.UtcNow);

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

    private async void OnToggleWarningsClick(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;

        if (_safetyMonitor.WarningsDisabled(now))
        {
            _safetyMonitor.ReenableWarnings();
            UpdateToggleWarningsButtonText();
            return;
        }

        var dialog = new DisableWarningsDialog { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            _safetyMonitor.DisableWarnings(now);
            WarningInfoBar.IsOpen = false;
        }

        UpdateToggleWarningsButtonText();
    }

    private void UpdateToggleWarningsButtonText()
    {
        ToggleWarningsButton.Content = _safetyMonitor.WarningsDisabled(DateTimeOffset.UtcNow)
            ? "Re-enable health warnings"
            : "Disable health warnings";
    }

    private void TrySetMicaBackdrop()
    {
        if (!MicaController.IsSupported())
        {
            return;
        }

        _backdropConfiguration = new SystemBackdropConfiguration { IsInputActive = true };
        _micaController = new MicaController();
        _micaController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
        _micaController.SetSystemBackdropConfiguration(_backdropConfiguration);
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
                _sessionManager.SetVolume(row.ProcessId, (float)(row.VolumePercent / 100.0));
                if (!string.IsNullOrEmpty(row.ExecutablePath))
                {
                    _ = _volumeStore.SetVolumeAsync(row.ExecutablePath, row.VolumePercent);
                }

                break;
            case nameof(SessionRowViewModel.IsMuted):
                _sessionManager.SetMute(row.ProcessId, row.IsMuted);
                break;
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
