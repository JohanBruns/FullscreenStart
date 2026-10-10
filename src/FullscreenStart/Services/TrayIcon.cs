using System.Windows;
using Forms = System.Windows.Forms;

namespace FullscreenStart.Services;

/// <summary>Notification-area icon with a small menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(string hotkey, Action toggleMenu, Action refreshApps, Action openDataFolder, Action exit,
        bool winKeyEnabled, Func<bool, bool> setWinKey)
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Startmenü öffnen", null, (_, _) => toggleMenu());
        var winKey = new Forms.ToolStripMenuItem("Windows-Taste öffnet dieses Menü") { Checked = winKeyEnabled };
        winKey.Click += (_, _) => winKey.Checked = setWinKey(!winKey.Checked);
        menu.Items.Add(winKey);
        menu.Items.Add("Apps neu einlesen", null, (_, _) => refreshApps());
        menu.Items.Add("Einstellungsordner öffnen", null, (_, _) => openDataFolder());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = $"Fullscreen Start ({hotkey})",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) toggleMenu();
        };
    }

    /// <summary>Non-blocking notification next to the tray icon.</summary>
    public void ShowNotice(string text) => _icon.ShowBalloonTip(8000, "Fullscreen Start", text, Forms.ToolTipIcon.Warning);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
