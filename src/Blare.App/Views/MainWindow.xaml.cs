using BLight.Blare.App.Views;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT;

namespace BLight.Blare.App;

public sealed partial class MainWindow : Window
{
    private MicaController? _micaController;
    private SystemBackdropConfiguration? _backdropConfiguration;

    public MainWindow()
    {
        InitializeComponent();

        Title = "Blare";
        TrySetMicaBackdrop();

        // WinUI3 windows have no sane default size — left alone this opens
        // near full-screen. A compact mixer doesn't need that much room.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(480, 640));

        ContentFrame.Navigate(typeof(MixerPage));
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;

        ContentFrame.Navigate(tag switch
        {
            "settings" => typeof(SettingsPage),
            _ => typeof(MixerPage),
        });
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
}
