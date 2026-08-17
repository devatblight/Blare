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
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    // The frame bits a top-level window gets by default. Every one of these
    // draws something around the content.
    private const long WsCaption = 0x00C00000;
    private const long WsThickFrame = 0x00040000;
    private const long WsBorder = 0x00800000;
    private const long WsDlgFrame = 0x00400000;
    private const long WsSysMenu = 0x00080000;
    private const long WsPopup = unchecked((long)0x80000000);

    private static readonly IntPtr HwndTopmost = new(-1);

    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;

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

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>
    /// The window's scale factor, straight from the OS.
    ///
    /// XamlRoot.RasterizationScale would do the same job but reads null until
    /// the window has been shown once, so the first message of a session got
    /// measured as though the display were at 100% and came out too short.
    /// </summary>
    public static double ScaleFor(IntPtr handle)
    {
        var dpi = GetDpiForWindow(handle);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;

    /// <summary>DWMWA_COLOR_NONE — suppresses the border entirely.</summary>
    private const uint DwmColorNone = 0xFFFFFFFE;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref uint value, int size);

    /// <summary>
    /// Strips the window frame Windows draws around every top-level window.
    ///
    /// Three separate things draw that pale rectangle and all three have to go.
    /// Hiding the title bar through the presenter leaves the window's own frame
    /// styles in place, so the caption and border bits are cleared outright and
    /// the window is made a popup; DWM still paints its own border colour on top
    /// of that, so it is set to none. Corners are then rounded by DWM rather than
    /// squared off, which is what a Fluent flyout should look like and avoids the
    /// content having to fake it.
    /// </summary>
    public static void RemoveSystemFrame(IntPtr handle)
    {
        var style = GetWindowLongPtrW(handle, GwlStyle).ToInt64();
        var stripped = (style & ~(WsCaption | WsThickFrame | WsBorder | WsDlgFrame | WsSysMenu)) | WsPopup;

        if (stripped != style)
        {
            SetWindowLongPtrW(handle, GwlStyle, new IntPtr(stripped));

            // Styles only take effect once the frame is recalculated.
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }

        var noBorder = DwmColorNone;
        DwmSetWindowAttribute(handle, DwmwaBorderColor, ref noBorder, sizeof(uint));

        var rounded = DwmwcpRound;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref rounded, sizeof(int));
    }
}
