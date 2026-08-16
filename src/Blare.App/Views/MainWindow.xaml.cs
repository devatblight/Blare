using BLight.Blare.Audio.Sessions;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT;

namespace BLight.Blare.App;

public sealed partial class MainWindow : Window
{
    private readonly AudioSessionManager _sessionManager;
    private MicaController? _micaController;
    private SystemBackdropConfiguration? _backdropConfiguration;

    public MainWindow(AudioSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
        InitializeComponent();

        Title = "Blare";
        TrySetMicaBackdrop();

        LoadSessions();
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

    private void LoadSessions()
    {
        var sessions = _sessionManager.GetSessionsForDefaultDevice();

        foreach (var session in sessions)
        {
            var label = string.IsNullOrWhiteSpace(session.DisplayName)
                ? $"pid {session.ProcessId}"
                : session.DisplayName;

            SessionListView.Items.Add(new TextBlock
            {
                Text = $"{label} — {session.Volume:P0}{(session.IsMuted ? " (muted)" : string.Empty)}",
            });
        }
    }
}
