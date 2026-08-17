using System.Runtime.InteropServices;

namespace Blight.Blare.App.Services;

/// <summary>
/// Which app the user is actually looking at.
///
/// This is the piece that makes Blare's hotkeys worth having. Windows' volume
/// keys act on the default output device, so turning down "the thing making the
/// noise" means finding it in the mixer by hand. Resolving the foreground window
/// to a process lets a single key press act on whatever is in front of you.
/// </summary>
internal static class ForegroundApp
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>The process id owning the foreground window, or null when there isn't one.</summary>
    public static uint? ProcessId()
    {
        var window = GetForegroundWindow();

        if (window == IntPtr.Zero)
        {
            return null;
        }

        return GetWindowThreadProcessId(window, out var processId) == 0 || processId == 0
            ? null
            : processId;
    }
}
