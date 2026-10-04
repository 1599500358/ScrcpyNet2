# ScrcpyNet

[English](README.md) | [简体中文](README.zh-CN.md)

A C#/.NET reimplementation of the [scrcpy client](https://github.com/Genymobile/scrcpy/tree/master/app) protocol, plus a WPF sample application for **Android device farms** (群控): mirror and control a whole fleet of phones from a single window.

Compatible with **scrcpy-server 4.1** (the latest release) and built against **.NET 10**.

## Projects

| Project | Description |
|---|---|
| `ScrcpyNet` | Core library: scrcpy-server 4.1 protocol, H.264 video decoding, control messages |
| `ScrcpyNet.Wpf` | WPF `ScrcpyDisplay` control: live video with automatic rotation and the device's real aspect ratio |
| `ScrcpyNet.Sample.Wpf` | **Device farm app (群控)**: connects every device registered in the SQLite device database automatically and shows them as a grid of live cards (2–5 per row) |

## Features

- **Multi-device**: one `Scrcpy` instance per device, each with its own local port — connect to dozens of phones at the same time
- **scrcpy-server 4.1 protocol**: key/value server options, device/codec/session meta parsing, and dynamic video size changes (device rotation) via session meta packets — no reconnect needed
- **Video**: H.264 with hardware decoding (DXVA2 etc.) and automatic fallback to software decoding, powered by the FFmpeg 9 shared libraries
- **Input**: keycodes, touch with pressure, scroll (range ±16), UTF-8 text injection, back/screen-on, display power on/off, rotate device
- **Server options**: bitrate, max size, max fps, capture orientation lock, show touches, stay awake, video encoder selection
- **Resilience**: a `Disconnected` event fires when a device vanishes mid-session, `Stop()` is idempotent, and the streaming threads can never crash the process
- **No** audio support (audio is disabled server-side)

## Setup

The ScrcpyNet library should automatically copy the files from the `deps/{shared,win64}` folder to the `ScrcpyNet` folder inside your bin folder.
If for some reason this doesn't happen, copy those files to a `ScrcpyNet` folder next to your executable.

This folder contains `scrcpy-server.jar` (scrcpy-server 4.1), `adb.exe` and the FFmpeg 9 shared libraries (`avcodec-63` & co) used by the video decoder.

## Usage (library)

Reference the `ScrcpyNet` and `ScrcpyNet.Wpf` projects (or packages, if published) and add the xml namespace to your xaml file:

```xml
xmlns:scrcpy="clr-namespace:ScrcpyNet.Wpf;assembly=ScrcpyNet.Wpf"
```

Place the display control:

```xml
<scrcpy:ScrcpyDisplay x:Name="ScrcpyDisplay"/>
```

Create a `Scrcpy` instance per device and start it. Each concurrent instance needs **its own loopback port** — the sample app assigns 27183, 27184, 27185, … one per device.

```cs
public MainWindow()
{
    InitializeComponent();

    // (optional) Logging via Serilog
    //Log.Logger = new LoggerConfiguration()
    //    .MinimumLevel.Verbose()
    //    .WriteTo.Console()
    //    .CreateLogger();

    // (optional) Start the ADB server if needed
    var srv = new AdbServer();
    if (!srv.GetStatus().IsRunning)
        srv.StartServer(Path.Combine(AppContext.BaseDirectory, "ScrcpyNet", "adb.exe"), false);

    // Find connected devices
    var devices = new AdbClient().GetDevices();

    if (devices.Count == 0)
    {
        MessageBox.Show("No device connected!");
        Close();
        return;
    }

    // Create a scrcpy instance (the second argument is the local port the server
    // connects back to) and set it on the ScrcpyDisplay.
    // NOTE: Data bindings are nicer — see the sample app.
    ScrcpyDisplay.Scrcpy = new Scrcpy(devices[0], 27183);
    ScrcpyDisplay.Scrcpy.Start(); // Start scrcpy and start streaming

    // React to unexpected disconnects (USB unplug, server crash, ...)
    ScrcpyDisplay.Scrcpy.Disconnected += () => { /* update your UI */ };
}

// Disconnect when closing, or the device session will linger.
protected override void OnClosing(CancelEventArgs e)
{
    ScrcpyDisplay.Scrcpy?.Stop();
    base.OnClosing(e);
}
```

## Usage (device farm app / 群控)

`ScrcpyNet.Sample.Wpf` is a ready-to-use group-control tool:

- Devices are managed through a SQLite database at `%AppData%\ScrcpyNet\devices.db` (each device: serial + user-assigned name, shown as the card title)
- Manage the list from the app itself: **添加设备** in the top bar registers a device, the rename/delete buttons on each card edit or remove it — no more hand-editing text files
- On the first run a legacy `Devices.txt` next to the executable (`<serial> <name>` per line) is imported into the database once; the txt file is ignored afterwards
- On startup it enumerates the adb devices, matches them against the database and connects to all of them automatically
- The top bar sets the cards per row (2–5) and the presentation orientation (portrait/landscape — applied client-side, the video itself follows the device)
- Settings persist in `%AppData%\ScrcpyNet\settings.json`; a lightweight diagnostic log is written to `%AppData%\ScrcpyNet\debug.log`

## Troubleshooting

### My device doesn't show up or can't connect

Make sure USB debugging is enabled and that you accepted the connection prompt on the phone (once per computer/phone). `adb devices` must list the device with status `device` — if it says `unauthorized`, reconnect and accept the prompt on the screen.

## Notes

- scrcpy-server 4.1 deletes its own jar from `/data/local/tmp` when it exits (`cleanup=true`). `Scrcpy.Start()` re-pushes it on every start, so reconnecting always works.
- This code (ab)uses `unsafe` C# for the FFmpeg interop.
- If you set the bitrate too high the decoder might not keep up and the video lags.

## Credits

- [Genymobile/scrcpy](https://github.com/Genymobile/scrcpy) — the protocol and server come from this project; the client is a reimplementation of theirs
- [The Android Open Source Project](https://android.googlesource.com/platform/frameworks/native/+/master/include/android) — for the input/keycodes
- [Fusion86/ScrcpyNet](https://github.com/Fusion86/ScrcpyNet) — the original library this fork evolved from
