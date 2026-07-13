using System.Windows;
using Application = System.Windows.Application;

namespace WindowsHUD;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private TrayIconManager? _trayIconManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mainWindow = new MainWindow();
        _trayIconManager = new TrayIconManager(_mainWindow);

        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconManager?.Dispose();
        base.OnExit(e);
    }
}
