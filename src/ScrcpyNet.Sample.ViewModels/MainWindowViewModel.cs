using DynamicData.Binding;
using ReactiveUI.Reactive;
using Serilog;
using SharpAdbClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ScrcpyNet.Sample.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public ReactiveCommand<Unit, List<ScrcpyViewModel>> LoadAvailableDevicesCommand { get; }

        public ObservableCollectionExtended<ScrcpyViewModel> Scrcpys { get; } = new ObservableCollectionExtended<ScrcpyViewModel>();

        /// <summary>Global UI settings shown in the top bar (persisted across restarts).</summary>
        public AppSettings Settings => AppSettings.Instance;

        private static readonly ILogger log = Log.ForContext<MainWindowViewModel>();

        public MainWindowViewModel()
        {
            LoadAvailableDevicesCommand = ReactiveCommand.Create(LoadAvailableDevices);
            // The command delivers its result on the taskpool, hop back to the UI thread
            // before touching the ObservableCollection.
            LoadAvailableDevicesCommand
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(devices =>
            {
                foreach (var item in devices)
                {
                    Scrcpys.Add(item);
                    // Connect automatically on startup.
                    item.ConnectCommand.Execute().Subscribe();
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
                        srv.StartServer(Path.Combine(AppContext.BaseDirectory, "ScrcpyNet", "adb.exe"), false);
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
                // Resolve Devices.txt relative to the exe first — the process working
                // directory is unpredictable when the app is launched from elsewhere.
                string filePath = Path.Combine(AppContext.BaseDirectory, "Devices.txt");
                if (!File.Exists(filePath))
                    filePath = "Devices.txt";
                FileReader fileReader = new FileReader();
                List<string[]> lines = fileReader.ReadFile(filePath);
                var devices = new AdbClient().GetDevices();
                UiDiagnostics.Log($"LoadAvailableDevices: Devices.txt='{filePath}' entries={lines.Count}, adb devices={devices.Count}");
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
                UiDiagnostics.Log($"LoadAvailableDevices: matched {list.Count} device(s)");
                return list;
            }
            catch (Exception ex)
            {
                UiDiagnostics.Log($"LoadAvailableDevices failed: {ex}");
                log.Error("Couldn't load available devices", ex);
                return new List<ScrcpyViewModel>();
            }
        }
    }
}
