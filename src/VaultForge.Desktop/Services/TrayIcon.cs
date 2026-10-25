using System.Windows;
using Forms = System.Windows.Forms;

namespace VaultForge.Desktop.Services;

/// <summary>Notification-area icon: keeps the app reachable while its window is hidden.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _updateItem;
    private Action? _onHintClicked;
    private Action? _restartToUpdate;

    public TrayIcon(Action open, Action lockVault, Action exit)
    {
        var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/VaultForge;component/Assets/VaultForge.ico"))!.Stream;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open VaultForge", null, (_, _) => open());
        menu.Items.Add("Lock", null, (_, _) => lockVault());
        _updateItem = new Forms.ToolStripMenuItem { Visible = false };
        _updateItem.Click += (_, _) => _restartToUpdate?.Invoke();
        menu.Items.Add(_updateItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize),
            Text = "VaultForge",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) open();
        };
        _icon.BalloonTipClicked += (_, _) => _onHintClicked?.Invoke();
        _icon.BalloonTipClosed += (_, _) => _onHintClicked = null;
    }

    /// <summary>Shows a notification; <paramref name="onClick"/> runs if the user clicks it.</summary>
    public void ShowHint(string title, string text, Action? onClick = null)
    {
        _onHintClicked = onClick;
        _icon.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.Info);
    }

    /// <summary>Adds "Restart to update to …" to the tray menu.</summary>
    public void ShowUpdateAvailable(string version, Action restart)
    {
        _updateItem.Text = $"Restart to update to {version}";
        _restartToUpdate = restart;
        _updateItem.Visible = true;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
