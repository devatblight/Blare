using Blight.Blare.App.Views;
using Blight.Blare.Core.Settings;
using Microsoft.UI.Xaml;

namespace Blight.Blare.App.Services;

/// <summary>
/// Closing the window hides Blare to the tray instead of quitting.
///
/// A mixer that exits when you close its window can't warn you about anything,
/// so hiding is the useful behaviour. But silently staying resident after the
/// user pressed a close button is the sort of thing people find creepy, so the
/// first close explains what happened and how to actually quit — once, then
/// never again.
/// </summary>
public sealed class WindowCloseBehavior
{
    private const string NoticeShownKey = "close-to-tray-notice-shown";

    private readonly ISettingsStore _store;
    private readonly FlyoutService _flyout;

    private bool _noticeShown;

    public WindowCloseBehavior(ISettingsStore store, FlyoutService flyout)
    {
        _store = store;
        _flyout = flyout;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _noticeShown = await _store.LoadAsync<bool?>(NoticeShownKey, cancellationToken) ?? false;
    }

    public void Attach(Window window)
    {
        window.AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            window.AppWindow.Hide();

            if (!_noticeShown)
            {
                _noticeShown = true;
                CrashLog.FireAndForget(_store.SaveAsync(NoticeShownKey, true));

                _flyout.Show(
                    "Blare is still running",
                    "It's in the tray, so it can keep an eye on your listening. Right-click the tray icon to quit for real.",
                    FlyoutTone.Neutral,
                    TimeSpan.FromSeconds(9));
            }
        };
    }
}
