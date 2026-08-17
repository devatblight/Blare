using BLight.Blare.App.Services;
using BLight.Blare.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT;

namespace BLight.Blare.App;

public sealed partial class MainWindow : Window
{
    private readonly ThemeService _themeService;
    private MicaController? _micaController;
    private SystemBackdropConfiguration? _backdropConfiguration;

    public MainWindow(ThemeService themeService)
    {
        _themeService = themeService;

        InitializeComponent();

        Title = "Blare";
        TrySetMicaBackdrop();

        // Draw our own caption so it sits on the Mica surface rather than in
        // a separate opaque bar above it.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // WinUI3 windows have no sane default size — left alone this opens
        // near full-screen. Sized to fit the nav rail plus roughly five
        // channel strips before the desk needs to scroll.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(880, 660));

        // Swapping resource dictionary values doesn't retro-actively update
        // elements that already resolved {ThemeResource}, so rebuild the page.
        _themeService.ThemeChanged += (_, _) => ReloadCurrentPage();

        ContentFrame.Navigate(typeof(MixerPage));
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        ContentFrame.Navigate(PageTypeFor(tag));
    }

    private void ReloadCurrentPage()
    {
        var tag = (RootNavigationView.SelectedItem as NavigationViewItem)?.Tag as string;
        var pageType = PageTypeFor(tag);

        // Navigating to the same type is a no-op unless the cache is cleared first.
        ContentFrame.BackStack.Clear();
        ContentFrame.Navigate(typeof(BlankPage));
        ContentFrame.Navigate(pageType);
    }

    private static Type PageTypeFor(string? tag) => tag switch
    {
        "settings" => typeof(SettingsPage),
        "diagnostics" => typeof(DiagnosticsPage),
        _ => typeof(MixerPage),
    };

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
