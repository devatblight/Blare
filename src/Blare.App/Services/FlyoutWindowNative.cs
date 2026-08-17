using System.Runtime.InteropServices;

namespace Blight.Blare.App.Services;

/// <summary>
/// Window tricks a notification flyout needs that the framework doesn't expose.
///
/// Both of these are load-bearing, and both are the approach FluentFlyout takes
/// for the same reasons:
///
/// <list type="bullet">
/// <item>WS_EX_NOACTIVATE stops the flyout stealing focus. Without it a message
/// appearing mid-game or mid-sentence pulls focus away, which is far worse than
/// the message is useful.</item>
/// <item>SetWindowPos with HWND_TOPMOST puts it above windows that the
/// presenter's own always-on-top still loses to.</item>
/// </list>
/// </summary>
internal static class FlyoutWindowNative
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    private static readonly IntPtr HwndTopmost = new(-1);

    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// <summary>Makes the window non-activating and hides it from Alt-Tab.</summary>
    public static void MakePassive(IntPtr handle)
    {
        var style = GetWindowLongPtrW(handle, GwlExStyle).ToInt64();
        SetWindowLongPtrW(handle, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExToolWindow));
    }

    /// <summary>Raises the window above everything, without activating it.</summary>
    public static void BringToTop(IntPtr handle) =>
        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
}
