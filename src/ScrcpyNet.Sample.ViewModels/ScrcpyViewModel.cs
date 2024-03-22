using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SharpAdbClient;
using System;
using System.Reactive;
using System.Threading.Tasks;

namespace ScrcpyNet.Sample.ViewModels
{
    public class ScrcpyViewModel : ViewModelBase
    {
        [Reactive] public double BitrateKb { get; set; } = 1_000;
        DeviceData device { get; set; }
        int port;
        [Reactive] public bool IsConnected { get; private set; }
        [Reactive] public string DeviceName { get; private set; } = "";
        [Reactive] public Scrcpy? Scrcpy { get; private set; }

        public ReactiveCommand<Unit, Unit> ConnectCommand { get; }
        public ReactiveCommand<Unit, Unit> DisconnectCommand { get; }

        public ReactiveCommand<AndroidKeycode, Unit> SendKeycodeCommand { get; }

        public ScrcpyViewModel(DeviceData d,int p)
        {
            port= p;
            device = d;
            // `outputScheduler: RxApp.TaskpoolScheduler` is only needed for the WPF frontend
            // TODO: This code only works ONCE. Aka you can't reconnect after disconnecting.
            ConnectCommand = ReactiveCommand.CreateFromTask(Connect);
            DisconnectCommand = ReactiveCommand.Create(Disconnect);
            SendKeycodeCommand = ReactiveCommand.Create<AndroidKeycode>(SendKeycode);
        }

        private async Task Connect()
        {
            if (device == null) return;
            if (Scrcpy != null) throw new Exception("Already connected.");

            Scrcpy = new Scrcpy(device, port);
            Scrcpy.Bitrate = (long)(BitrateKb * 1000);
            await Task.Run(()=> Scrcpy.Start()) ;
            DeviceName = Scrcpy.DeviceName;
            IsConnected = true;
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
