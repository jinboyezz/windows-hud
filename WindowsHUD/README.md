# Windows HUD

一个常驻桌面的轻量级系统监控悬浮窗（WPF + .NET 8），实时显示 CPU、GPU、内存、网络、磁盘占用。

## 功能

- 桌面悬浮 HUD：无边框、半透明、始终置顶，每 2 秒刷新一次 CPU / GPU / 内存 / 网络上下行 / 磁盘使用率
- 位置记忆：拖动后自动保存窗口位置，下次启动恢复
- 锁定模式：锁定后窗口可穿透点击，避免误触；解锁后可拖动调整位置
- 系统托盘：右键菜单可切换锁定状态、开启/关闭开机自启动、退出程序
- 配置持久化：设置保存在 `%APPDATA%\WindowsHUD\config.json`

## 运行环境

- Windows 10/11
- .NET 8 Desktop Runtime（`net8.0-windows`，使用了 WPF 和 WinForms）

## 构建与运行

```powershell
cd WindowsHUD
dotnet build -c Release
dotnet run -c Release
```

生成的可执行文件位于 `bin\Release\net8.0-windows\WindowsHUD.exe`。

## 项目结构

- `App.xaml.cs` — 应用入口，启动主窗口和托盘图标
- `MainWindow.xaml` / `MainWindow.xaml.cs` — HUD 悬浮窗界面与刷新逻辑
- `TrayIconManager.cs` — 系统托盘图标与右键菜单
- `Native/NativeMethods.cs` — 窗口置顶、点击穿透等 Win32 互操作
- `Services/SystemMetricsService.cs` — 采集 CPU/GPU/内存/网络/磁盘指标
- `Services/ConfigService.cs` — 读写本地配置文件
- `Services/AutoStartService.cs` — 读写注册表实现开机自启动
- `Assets/app.ico` — 应用及托盘图标
