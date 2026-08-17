using System.Windows.Forms;

namespace Blight.Blare.App.Services;

/// <summary>
/// WinUI 3 has no native tray API, so this wraps System.Windows.Forms.NotifyIcon
/// (see Blare.App.csproj's FrameworkReference note) rather than hand-rolling
/// Shell_NotifyIcon COM interop for a single icon and a short menu.
///
/// The tray icon is the surface Blare is used from most, so it carries the
/// actions worth reaching without opening a window: middle-click to silence
/// whatever is in front, and a menu for the rest.
///
/// Scrolling the icon to change volume is deliberately absent. NotifyIcon has no
/// wheel event because the taskbar receives WM_MOUSEWHEEL, not this process, so
/// it would take a global low-level mouse hook running on every mouse move
/// system-wide — too much standing cost for a convenience.
///
/// Icon asset is a system stock icon for now — swapping in the real
/// normal/warning glyph set is an asset-design task, not logic.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? MuteEverythingRequested;

    public event EventHandler? MuteForegroundRequested;

    public TrayIconService()
    {
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open Blare", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Mute the app in front", null, (_, _) => MuteForegroundRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add("Mute everything", null, (_, _) => MuteEverythingRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Blare",
            Visible = true,
            ContextMenuStrip = contextMenu,
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            switch (e.Button)
            {
                case MouseButtons.Left:
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case MouseButtons.Middle:
                    MuteForegroundRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        };

    }

    /// <summary>Reflects whether anything is currently warned about, so the tray says something true at a glance.</summary>
    public void SetWarning(bool warning)
    {
        _notifyIcon.Icon = warning
            ? System.Drawing.SystemIcons.Warning
            : System.Drawing.SystemIcons.Application;

        _notifyIcon.Text = warning ? "Blare — running loud" : "Blare";
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
