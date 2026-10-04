using ReactiveUI;
using ReactiveUI.Reactive;
using ReactiveUI.SourceGenerators;
using Serilog;
using SharpAdbClient;
using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ScrcpyNet.Sample.ViewModels
{
    public partial class ScrcpyViewModel : ViewModelBase
    {
        [Reactive] public partial double BitrateKb { get; set; }
        DeviceData device { get; set; }
        int port;
        [Reactive] public partial bool IsConnected { get; private set; }
        [Reactive] public partial bool IsConnecting { get; private set; }
        /// <summary>User-assigned device name from the database, shown as the card title.</summary>
        [Reactive] public partial string DeviceName { get; private set; }
        /// <summary>Device model reported by scrcpy once connected (e.g. SM-G965U1).</summary>
        [Reactive] public partial string? DeviceModel { get; private set; }
        [Reactive] public partial Scrcpy? Scrcpy { get; private set; }

        public ReactiveCommand<Unit, Unit> ConnectCommand { get; }
        public ReactiveCommand<Unit, Unit> DisconnectCommand { get; }
        public ReactiveCommand<Unit, Unit> RenameDeviceCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveDeviceCommand { get; }

        public ReactiveCommand<AndroidKeycode, Unit> SendKeycodeCommand { get; }

        /// <summary>Serializes connect attempts against disconnects and orientation restarts.</summary>
        private readonly SemaphoreSlim connectLock = new(1, 1);

        private readonly MainWindowViewModel owner;

        /// <summary>The adb serial of this device (primary key in the device database).</summary>
        public string Serial => device.Serial;

        public ScrcpyViewModel(DeviceData d, int p, string name, MainWindowViewModel owner)
        {
            port = p;
            device = d;
            this.owner = owner;
            BitrateKb = 1_000;
            // The card shows the database name immediately, before any connection;
            // DeviceModel is filled in separately once scrcpy reports it.
            DeviceName = name;
            // Block the command while a connection exists or is being established, so the
            // button shows as disabled and double clicks are ignored.
            var canConnect = this.WhenAnyValue(
                x => x.IsConnected,
                x => x.IsConnecting,
                (connected, connecting) => !connected && !connecting);
            ConnectCommand = ReactiveCommand.CreateFromTask(Connect, canConnect);
            DisconnectCommand = ReactiveCommand.Create(Disconnect, this.WhenAnyValue(x => x.IsConnected));
            SendKeycodeCommand = ReactiveCommand.Create<AndroidKeycode>(SendKeycode);

            // Renaming/removing rewrites the database and (for remove) the card list;
            // block both while a connect attempt is in flight to avoid racing it.
            var canEdit = this.WhenAnyValue(x => x.IsConnecting, connecting => !connecting);
            RenameDeviceCommand = ReactiveCommand.CreateFromTask(RenameDevice, canEdit);
            RemoveDeviceCommand = ReactiveCommand.CreateFromTask(RemoveDevice, canEdit);

            // Log command errors instead of letting them surface as unhandled.
            ConnectCommand.ThrownExceptions.Subscribe(ex => Log.Error(ex, "Connect failed for {Serial}", device.Serial));
            DisconnectCommand.ThrownExceptions.Subscribe(ex => Log.Error(ex, "Disconnect failed for {Serial}", device.Serial));
            SendKeycodeCommand.ThrownExceptions.Subscribe(ex => Log.Error(ex, "SendKeycode failed for {Serial}", device.Serial));
            RenameDeviceCommand.ThrownExceptions.Subscribe(ex => Log.Error(ex, "RenameDevice failed for {Serial}", device.Serial));
            RemoveDeviceCommand.ThrownExceptions.Subscribe(ex => Log.Error(ex, "RemoveDevice failed for {Serial}", device.Serial));

            // The orientation setting is display-only (client-side video rotation), so
            // there is nothing to push to the device here — the UI handles it.
        }

        /// <summary>Stops the mirror (if any) without touching the database or the card list.</summary>
        public void Shutdown()
        {
            Scrcpy?.Stop();
            Scrcpy = null;
            IsConnected = false;
        }

        private async Task RenameDevice()
        {
            // The dialog handler is registered by MainWindow; null means cancelled.
            string? newName = await owner.RenameDeviceInteraction.Handle(DeviceName);
            if (string.IsNullOrWhiteSpace(newName))
                return;

            if (owner.DeviceDatabase.Rename(Serial, newName))
            {
                DeviceName = newName.Trim();
                UiDiagnostics.Log($"Renamed device '{Serial}' to '{DeviceName}'");
            }
        }

        private async Task RemoveDevice()
        {
            bool confirmed = await owner.RemoveDeviceInteraction.Handle(DeviceName);
            if (!confirmed)
                return;

            owner.DeviceDatabase.Remove(Serial);
            Shutdown();
            owner.RemoveCard(this);
            UiDiagnostics.Log($"Removed device '{Serial}' from the database");
        }

        private async Task Connect()
        {
            // Serialize with other connect attempts.
            await connectLock.WaitAsync();
            try
            {
                if (device == null) return;
                // canExecute already prevents this; stay a no-op instead of throwing.
                if (Scrcpy != null) return;

                IsConnecting = true;
                try
                {
                    var scrcpy = new Scrcpy(device, port);
                    scrcpy.Bitrate = (long)(BitrateKb * 1000);
                    // SM-G965U1 (Android 10) only offers the default vendor OMX.qcom.video.encoder.avc
                    // and the software c2.android.avc.encoder for h264 — force the latter for this phone.
                    if (device.Serial == "4254395143353098")
                        scrcpy.VideoEncoder = "c2.android.avc.encoder";
                    // The video always streams in the device's physical orientation; the
                    // rotation setting is applied client-side on the display only.
                    await Task.Run(() => scrcpy.Start());

                    // The device can vanish mid-session (USB unplug, server crash): reflect
                    // it in the UI. Fires on a threadpool thread; WPF marshals scalar
                    // PropertyChanged events to the UI thread.
                    scrcpy.Disconnected += () =>
                    {
                        Log.Warning("Connection to {Serial} was lost.", device.Serial);
                        IsConnected = false;
                        Scrcpy = null;
                    };

                    Scrcpy = scrcpy;
                    DeviceModel = scrcpy.DeviceName;
                    IsConnected = true;
                }
                catch (Exception ex)
                {
                    // Devices connect automatically on startup, so one failing device
                    // (offline, unauthorized, ...) must not take down the whole app.
                    Log.Error(ex, "Couldn't connect to {Serial}", device.Serial);
                    IsConnected = false;
                }
                finally
                {
                    IsConnecting = false;
                }
            }
            finally
            {
                connectLock.Release();
            }
        }

        private void Disconnect()
        {
            if (Scrcpy != null)
            {
                Scrcpy.Stop();
                IsConnected = false;
                Scrcpy = null;
            }
        }

        private void SendKeycode(AndroidKeycode key)
        {
            if (Scrcpy == null) return;

            Scrcpy.SendControlCommand(new KeycodeControlMessage
            {
                KeyCode = key,
                Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN
            });

            // No need to wait before sending the KeyUp event.

            Scrcpy.SendControlCommand(new KeycodeControlMessage
            {
                KeyCode = key,
                Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP
            });
        }
    }
}
