using Blight.Blare.App.Services;
using Blight.Blare.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Blight.Blare.App;

public sealed partial class MainWindow : Window
{
    private readonly ThemeService _themeService;
    private readonly BackdropService _backdropService;

    public MainWindow(ThemeService themeService, BackdropService backdropService)
    {
        _themeService = themeService;
        _backdropService = backdropService;

        InitializeComponent();

        Title = "Blare";
        _backdropService.Attach(this);

        // Draw our own caption so it sits on the Mica surface rather than in
        // a separate opaque bar above it.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // WinUI3 windows have no sane default size — left alone this opens
        // near full-screen. Sized so the default dashboard is fully visible:
        // the top row of cards plus roughly six channel strips, without the
        // desk needing to scroll or cards being squeezed.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1240, 900));

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

}
