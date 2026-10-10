using System.Windows;
using Forms = System.Windows.Forms;

namespace FullscreenStart.Services;

/// <summary>Notification-area icon; a left click toggles the menu, the right-click menu is built by the caller.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu = new();

    public TrayIcon(string tooltip, Action toggleMenu)
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream;
        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = tooltip,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) toggleMenu();
        };
    }

    public TrayIcon AddItem(string text, Action action)
    {
        _menu.Items.Add(text, null, (_, _) => action());
        return this;
    }

    /// <summary>A checkable entry; <paramref name="set"/> applies the wanted state and returns the actual one.</summary>
    public TrayIcon AddToggle(string text, bool isChecked, Func<bool, bool> set)
    {
        var item = new Forms.ToolStripMenuItem(text) { Checked = isChecked };
        item.Click += (_, _) => item.Checked = set(!item.Checked);
        _menu.Items.Add(item);
        return this;
    }

    public TrayIcon AddSeparator()
    {
        _menu.Items.Add(new Forms.ToolStripSeparator());
        return this;
    }

    /// <summary>Non-blocking notification next to the tray icon.</summary>
    public void ShowNotice(string text) => _icon.ShowBalloonTip(8000, "Fullscreen Start", text, Forms.ToolTipIcon.Warning);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
