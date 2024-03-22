using DynamicData.Binding;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SharpAdbClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ScrcpyNet.Sample.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public ReactiveCommand<Unit, List<ScrcpyViewModel>> LoadAvailableDevicesCommand { get; }

        public ObservableCollectionExtended<ScrcpyViewModel> Scrcpys { get; } = new ObservableCollectionExtended<ScrcpyViewModel>();

        private static readonly ILogger log = Log.ForContext<MainWindowViewModel>();

        public MainWindowViewModel()
        {
            LoadAvailableDevicesCommand = ReactiveCommand.Create(LoadAvailableDevices);
            LoadAvailableDevicesCommand.Subscribe(devices =>
            {
                foreach (var item in devices)
                {
                    Scrcpys.Add(item);
                    //item.ConnectCommand.Execute();
                }
            });
            Task.Run(async () =>
            {
                // Start ADB server if needed
                var srv = new AdbServer();
                if (!srv.GetStatus().IsRunning)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        srv.StartServer("ScrcpyNet/adb.exe", false);
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        srv.StartServer("/usr/bin/adb", false);
                    }
                    else
                    {
                        log.Warning("Can't automatically start the ADB server on this platform.");
                    }
                }

                await LoadAvailableDevicesCommand.Execute();

            });
        }

        private List<ScrcpyViewModel> LoadAvailableDevices()
        {
            try
            {
                var port = 27183;
                string filePath = "Devices.txt";
                FileReader fileReader = new FileReader();
                List<string[]> lines = fileReader.ReadFile(filePath);
                var devices = new AdbClient().GetDevices();
                List<ScrcpyViewModel> list = new List<ScrcpyViewModel>();
                foreach (var line in lines)
                {
                    if (line.Length >= 2)
                    {
                        var Serial = line[0];
                        var newName = line[1];

                        var deviceToUpdate = devices.FirstOrDefault(d => d.Serial == Serial);
                        if (deviceToUpdate != null)
                        {
                            deviceToUpdate.Name = newName;
                            var scrcpyvm = new ScrcpyViewModel(deviceToUpdate,port);
                            list.Add(scrcpyvm);
                            port++;
                        }
                    }
                }
                return list;
            }
            catch (Exception ex)
            {
                log.Error("Couldn't load available devices", ex);
                return new List<ScrcpyViewModel>();
            }
        }
    }
}
