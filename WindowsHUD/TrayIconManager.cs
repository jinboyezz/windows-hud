using System;
using System.Drawing;
using System.Windows.Forms;
using WindowsHUD.Services;
using Application = System.Windows.Application;

namespace WindowsHUD;

public sealed class TrayIconManager : IDisposable
{
    private readonly Icon _icon;
    private readonly NotifyIcon _notifyIcon;
    private readonly MainWindow _mainWindow;
    private readonly ToolStripMenuItem _lockMenuItem;
    private readonly ToolStripMenuItem _autoStartMenuItem;

    public TrayIconManager(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;

        var menu = new ContextMenuStrip();

        _lockMenuItem = new ToolStripMenuItem();
        _lockMenuItem.Click += (_, _) => ToggleLock();
        menu.Items.Add(_lockMenuItem);

        _autoStartMenuItem = new ToolStripMenuItem("开机自启动")
        {
            CheckOnClick = true,
            Checked = AutoStartService.IsEnabled()
        };
        _autoStartMenuItem.Click += (_, _) => AutoStartService.SetEnabled(_autoStartMenuItem.Checked);
        menu.Items.Add(_autoStartMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exitItem);

        var iconResource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))
            ?? throw new InvalidOperationException("Application icon resource is missing.");
        using (iconResource.Stream)
        {
            _icon = new Icon(iconResource.Stream);
        }

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Visible = true,
            Text = "Windows HUD",
            ContextMenuStrip = menu
        };

        UpdateLockMenuText();
        _mainWindow.LockStateChanged += _ => UpdateLockMenuText();
    }

    private void ToggleLock()
    {
        _mainWindow.ToggleLock();
    }

    private void UpdateLockMenuText()
    {
        _lockMenuItem.Text = _mainWindow.IsLocked ? "解锁拖动" : "锁定位置";
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
