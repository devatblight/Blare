using System.Windows.Forms;

namespace Blight.Blare.App.Services;

/// <summary>
/// WinUI 3 has no native tray API, so this wraps System.Windows.Forms.NotifyIcon
/// (see Blare.App.csproj's FrameworkReference note) rather than hand-rolling
/// Shell_NotifyIcon COM interop for a single icon + two-item menu.
///
/// Icon asset is a system stock icon for now — swapping in the real
/// normal/boosted/warning glyph set is an asset-design task, not logic.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open Blare", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
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
            if (e.Button == MouseButtons.Left)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
