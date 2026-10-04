# ScrcpyNet

[English](README.md) | [简体中文](README.zh-CN.md)

用 C#/.NET 重新实现的 [scrcpy 客户端](https://github.com/Genymobile/scrcpy/tree/master/app)协议库，并附带一个 **安卓群控** WPF 示例应用：在一个窗口里同时投屏并控制一整批手机。

兼容最新版 **scrcpy-server 4.1**，基于 **.NET 10** 构建。

## 项目结构

| 项目 | 说明 |
|---|---|
| `ScrcpyNet` | 核心库：scrcpy-server 4.1 协议、H.264 视频解码、控制消息 |
| `ScrcpyNet.Wpf` | WPF `ScrcpyDisplay` 控件：实时画面，支持自动旋转与设备真实宽高比 |
| `ScrcpyNet.Sample.Wpf` | **群控应用**：自动连接 SQLite 设备数据库中登记的所有设备，以卡片网格（每行 2–5 个）实时显示 |

## 功能特性

- **多设备**：每台设备一个 `Scrcpy` 实例、一个独立本地端口——可同时连接数十台手机
- **scrcpy-server 4.1 协议**：key/value 启动参数、设备名/编码器/会话元数据解析、通过会话元数据包动态感知视频尺寸变化（设备旋转），无需重连
- **视频**：H.264，硬件解码（DXVA2 等）失败自动回退软解，使用 FFmpeg 9 共享库
- **输入**：按键码、带压感的触摸、滚动（范围 ±16）、UTF-8 文本注入、返回/亮屏、屏幕点亮/熄灭、旋转设备
- **服务端参数**：比特率、最大尺寸、最大帧率、采集方向锁定、显示触摸、保持唤醒、视频编码器选择
- **稳定性**：意外掉线（拔线、server 崩溃）会触发 `Disconnected` 事件；`Stop()` 幂等；流线程的异常不会导致进程崩溃
- **不支持**音频（服务端已禁用音频）

## 部署

ScrcpyNet 库会把 `deps/{shared,win64}` 目录下的文件自动复制到输出目录的 `ScrcpyNet` 文件夹中。
如果没有自动复制，请手动把这些文件放到可执行文件旁边的 `ScrcpyNet` 文件夹里。

该目录包含 `scrcpy-server.jar`（scrcpy-server 4.1）、`adb.exe` 以及视频解码所需的 FFmpeg 9 共享库（`avcodec-63` 等）。

## 用法（库）

引用 `ScrcpyNet` 和 `ScrcpyNet.Wpf` 项目（如有发布也可用 NuGet 包），在 xaml 中添加命名空间：

```xml
xmlns:scrcpy="clr-namespace:ScrcpyNet.Wpf;assembly=ScrcpyNet.Wpf"
```

放置显示控件：

```xml
<scrcpy:ScrcpyDisplay x:Name="ScrcpyDisplay"/>
```

为每台设备创建一个 `Scrcpy` 实例并启动。每个并发实例都需要**独立的本地回环端口**——示例应用按设备依次分配 27183、27184、27185……。

```cs
public MainWindow()
{
    InitializeComponent();

    // （可选）通过 Serilog 记录日志
    //Log.Logger = new LoggerConfiguration()
    //    .MinimumLevel.Verbose()
    //    .WriteTo.Console()
    //    .CreateLogger();

    // （可选）按需启动 ADB 服务
    var srv = new AdbServer();
    if (!srv.GetStatus().IsRunning)
        srv.StartServer(Path.Combine(AppContext.BaseDirectory, "ScrcpyNet", "adb.exe"), false);

    // 枚举已连接设备
    var devices = new AdbClient().GetDevices();

    if (devices.Count == 0)
    {
        MessageBox.Show("未检测到设备！");
        Close();
        return;
    }

    // 创建 scrcpy 实例（第二个参数是 server 回连使用的本地端口），
    // 并设置到 ScrcpyDisplay 上。示例应用使用了数据绑定，更推荐那种写法。
    ScrcpyDisplay.Scrcpy = new Scrcpy(devices[0], 27183);
    ScrcpyDisplay.Scrcpy.Start(); // 启动 scrcpy 并开始拉流

    // 处理意外掉线（拔线、server 崩溃等）
    ScrcpyDisplay.Scrcpy.Disconnected += () => { /* 更新你的界面状态 */ };
}

// 关闭窗口时记得断开设备，否则设备会话会一直挂着。
protected override void OnClosing(CancelEventArgs e)
{
    ScrcpyDisplay.Scrcpy?.Stop();
    base.OnClosing(e);
}
```

## 用法（群控应用）

`ScrcpyNet.Sample.Wpf` 是一个开箱即用的群控工具：

- 设备通过 SQLite 数据库管理：`%AppData%\ScrcpyNet\devices.db`（每台设备一条：序列号 + 自定义名称，名称显示为卡片标题）
- 直接在应用内管理设备列表：顶栏 **添加设备** 登记新设备，卡片上的重命名/删除按钮可随时修改或移除——不再需要手动编辑文本文件
- 首次运行时会自动把可执行文件旁的旧版 `Devices.txt`（每行 `<序列号> <名称>`）一次性导入数据库，之后该 txt 文件不再被读取
- 启动时自动枚举 adb 设备，与数据库匹配后全部自动连接
- 顶栏可设置每行卡片数（2–5）和呈现方向（竖屏/横屏——仅客户端旋转显示，视频本身跟随设备）
- 设置持久化在 `%AppData%\ScrcpyNet\settings.json`；轻量诊断日志写入 `%AppData%\ScrcpyNet\debug.log`

## 常见问题

### 设备不显示或连不上

确认已开启 USB 调试，并且首次连接时在手机上点击了"允许"（每台电脑/手机只需一次）。`adb devices` 中设备状态必须是 `device`——如果显示 `unauthorized`，重新插拔并在手机上确认授权弹窗。

## 说明

- scrcpy-server 4.1 退出时会自删 `/data/local/tmp` 上的 jar（`cleanup=true`）。`Scrcpy.Start()` 每次启动都会重新上传，因此重连始终可用。
- 代码在 FFmpeg 互操作部分使用了 C# 的 `unsafe`。
- 比特率设置过高时，解码可能跟不上，导致画面卡顿。

## 致谢

- [Genymobile/scrcpy](https://github.com/Genymobile/scrcpy) —— 协议与 server 均来自该项目，客户端为其重新实现
- [The Android Open Source Project](https://android.googlesource.com/platform/frameworks/native/+/master/include/android) —— 输入事件/按键码定义
- [Fusion86/ScrcpyNet](https://github.com/Fusion86/ScrcpyNet) —— 本项目演化自该原始库
