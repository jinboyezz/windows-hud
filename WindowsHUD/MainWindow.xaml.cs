using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WindowsHUD.Native;
using WindowsHUD.Services;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace WindowsHUD;

public partial class MainWindow : Window
{
    private readonly SystemMetricsService _metricsService = new();
    private readonly DispatcherTimer _metricsTimer;
    private readonly DispatcherTimer _topmostTimer;
    private HudConfig _config;
    private IntPtr _hwnd;
    private bool _isLocked;

    public event Action<bool>? LockStateChanged;

    public MainWindow()
    {
        InitializeComponent();

        _config = ConfigService.Load();
        _isLocked = _config.Locked;

        if (!double.IsNaN(_config.Left) && !double.IsNaN(_config.Top))
        {
            Left = _config.Left;
            Top = _config.Top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        Loaded += OnLoaded;

        _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _metricsTimer.Tick += async (_, _) => await RefreshMetricsAsync();

        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _topmostTimer.Tick += (_, _) => NativeMethods.ForceTopmost(_hwnd);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.ApplyBaseExStyle(_hwnd);
        ApplyLockState(_isLocked);

        if (double.IsNaN(Left) || double.IsNaN(Top))
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - 20;
            Top = workArea.Top + 20;
        }

        _metricsTimer.Start();
        _topmostTimer.Start();

        _ = RefreshMetricsAsync();
    }

    private async System.Threading.Tasks.Task RefreshMetricsAsync()
    {
        MetricsSnapshot? snapshot;
        try
        {
            snapshot = await _metricsService.SampleAsync();
        }
        catch
        {
            return;
        }

        if (snapshot is not { } s)
        {
            // Previous sample still in flight; skip this tick rather than showing stale/garbage data.
            return;
        }

        CpuText.Text = $"CPU {s.CpuPercent,5:0.0}%";
        GpuText.Text = $"GPU {s.GpuPercent,5:0.0}%";
        MemText.Text = $"MEM {s.MemoryPercent,5:0.0}%";
        NetText.Text = $"↓{FormatRate(s.DownloadBytesPerSec)} ↑{FormatRate(s.UploadBytesPerSec)}";
        DiskText.Text = $"DISK {s.DiskPercent,5:0.0}%";
    }

    private static string FormatRate(double bytesPerSec)
    {
        double kb = bytesPerSec / 1024.0;
        if (kb < 1024)
        {
            return $"{kb,6:0.0}KB/s";
        }
        return $"{kb / 1024.0,6:0.00}MB/s";
    }

    public bool IsLocked => _isLocked;

    public void ToggleLock()
    {
        ApplyLockState(!_isLocked);
    }

    private void ApplyLockState(bool locked)
    {
        _isLocked = locked;
        NativeMethods.SetClickThrough(_hwnd, locked);

        RootBorder.Background = locked
            ? new SolidColorBrush(Color.FromArgb(0xB2, 0x20, 0x20, 0x20))
            : new SolidColorBrush(Color.FromArgb(0xD0, 0x35, 0x35, 0x60));

        _config.Locked = locked;
        ConfigService.Save(_config);

        LockStateChanged?.Invoke(locked);
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!_isLocked)
        {
            DragMove();
        }
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (!_isLocked && _hwnd != IntPtr.Zero)
        {
            _config.Left = Left;
            _config.Top = Top;
            ConfigService.Save(_config);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _metricsTimer.Stop();
        _topmostTimer.Stop();
        _metricsService.Dispose();
        base.OnClosed(e);
    }
}
