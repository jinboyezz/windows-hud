using System;
using System.Drawing;
using System.Threading.Tasks;
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
    private readonly ToolStripMenuItem _releaseMemoryMenuItem;

    public TrayIconManager(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;

        var menu = new ContextMenuStrip();

        _releaseMemoryMenuItem = new ToolStripMenuItem("一键释放内存");
        _releaseMemoryMenuItem.Click += (_, _) => ReleaseMemory();
        menu.Items.Add(_releaseMemoryMenuItem);

        menu.Items.Add(new ToolStripSeparator());

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

    private async void ReleaseMemory()
    {
        _releaseMemoryMenuItem.Enabled = false;
        try
        {
            var result = await Task.Run(() => MemoryCleanupService.RunCleanup());
            long freedMb = Math.Max(0, result.FreedBytes) / 1024 / 1024;
            _notifyIcon.ShowBalloonTip(
                3000,
                "Windows HUD",
                $"已清理 {result.ProcessesCleaned} 个进程，释放约 {freedMb} MB 内存",
                ToolTipIcon.Info);
        }
        finally
        {
            _releaseMemoryMenuItem.Enabled = true;
        }
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
