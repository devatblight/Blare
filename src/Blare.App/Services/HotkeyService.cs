using System.Runtime.InteropServices;

namespace Blight.Blare.App.Services;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,

    /// <summary>Stops the hotkey repeating while the keys are held.</summary>
    NoRepeat = 0x4000,
}

/// <summary>
/// System-wide hotkeys.
///
/// Blare's whole reason for having these is that Windows' own volume keys act on
/// the default device: there is no built-in way to turn down just the app you are
/// looking at. That needs a key press to be heard while another app has focus,
/// which means a real system hotkey rather than a XAML accelerator.
///
/// Hotkeys are delivered as WM_HOTKEY to a window, so this owns a message-only
/// window of its own rather than borrowing the main one — the main window can be
/// closed to tray or rebuilt on a theme change, and a hotkey that stops working
/// when a window the user never sees goes away is worse than no hotkey.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private static readonly IntPtr HwndMessage = new(-3);

    private delegate IntPtr WindowProcedure(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public IntPtr MenuName;
        public IntPtr ClassName;
        public IntPtr SmallIcon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClass windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        int exStyle, string className, string? windowName, int style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    private readonly Dictionary<int, Action> _handlers = new();

    // Held in a field because the window class keeps a raw pointer to it; a
    // local would be collected and the first hotkey would crash the process.
    private readonly WindowProcedure _procedure;

    private readonly IntPtr _window;
    private int _nextId = 1;
    private bool _disposed;

    public HotkeyService()
    {
        _procedure = OnMessage;

        var className = $"BlareHotkeys_{Environment.ProcessId}";
        var instance = GetModuleHandleW(null);

        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Procedure = Marshal.GetFunctionPointerForDelegate(_procedure),
            Instance = instance,
            ClassName = Marshal.StringToHGlobalUni(className),
        };

        RegisterClassExW(ref windowClass);

        _window = CreateWindowExW(
            0, className, null, 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, instance, IntPtr.Zero);
    }

    /// <summary>Whether the message window exists. Everything else is a no-op without it.</summary>
    public bool IsAvailable => _window != IntPtr.Zero;

    /// <summary>
    /// Registers a hotkey and returns its id, or null when the combination is
    /// already taken by something else on the system.
    ///
    /// A clash is normal rather than exceptional — plenty of apps hold global
    /// hotkeys — so this reports it and lets the caller tell the user which one
    /// failed instead of throwing.
    /// </summary>
    public int? Register(HotkeyModifiers modifiers, uint virtualKey, Action handler)
    {
        if (!IsAvailable)
        {
            return null;
        }

        var id = _nextId++;

        if (!RegisterHotKey(_window, id, (uint)(modifiers | HotkeyModifiers.NoRepeat), virtualKey))
        {
            return null;
        }

        _handlers[id] = handler;
        return id;
    }

    public void Unregister(int id)
    {
        if (_handlers.Remove(id) && IsAvailable)
        {
            UnregisterHotKey(_window, id);
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys.ToList())
        {
            Unregister(id);
        }
    }

    private IntPtr OnMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmHotkey && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            // A throwing handler here would propagate into a native message
            // pump, where it becomes an unexplained process death.
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
            }

            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();

        if (IsAvailable)
        {
            DestroyWindow(_window);
        }
    }
}
