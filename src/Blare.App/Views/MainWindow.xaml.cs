using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using BLight.Blare.App.Services;
using BLight.Blare.App.ViewModels;
using BLight.Blare.Audio.Sessions;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using WinRT;

namespace BLight.Blare.App;

public sealed partial class MainWindow : Window
{
    private readonly AudioSessionManager _sessionManager;
    private readonly IconResolver _iconResolver = new();
    private MicaController? _micaController;
    private SystemBackdropConfiguration? _backdropConfiguration;
    private bool _suppressVolumePush;

    public ObservableCollection<SessionRowViewModel> Sessions { get; } = new();

    public MainWindow(AudioSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
        InitializeComponent();

        Title = "Blare";
        TrySetMicaBackdrop();

        _ = RefreshSessionsAsync();
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

            var row = new SessionRowViewModel
            {
                ProcessId = session.ProcessId,
                DisplayName = string.IsNullOrWhiteSpace(session.DisplayName) ? displayName : session.DisplayName,
                VolumePercent = Math.Round(session.Volume * 100),
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
