using DynamicData.Binding;
using ReactiveUI;
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
        public ReactiveCommand<Unit, Unit> AddDeviceCommand { get; }

        /// <summary>Ask the view for a new device (serial + name); null output = cancelled.</summary>
        public Interaction<Unit, DeviceRecord?> AddDeviceInteraction { get; } = new();

        /// <summary>Ask the view for a new name; input is the current name, null output = cancelled.</summary>
        public Interaction<string, string?> RenameDeviceInteraction { get; } = new();

        /// <summary>Ask the view to confirm removal; input is the device name.</summary>
        public Interaction<string, bool> RemoveDeviceInteraction { get; } = new();

        public ObservableCollectionExtended<ScrcpyViewModel> Scrcpys { get; } = new ObservableCollectionExtended<ScrcpyViewModel>();

        /// <summary>Global UI settings shown in the top bar (persisted across restarts).</summary>
        public AppSettings Settings => AppSettings.Instance;

        /// <summary>Device registry persisted in SQLite (replaces the old Devices.txt).</summary>
        public DeviceDatabase DeviceDatabase { get; }

        private static readonly ILogger log = Log.ForContext<MainWindowViewModel>();

        public MainWindowViewModel()
        {
            DeviceDatabase = DeviceDatabase.Default;
            LoadAvailableDevicesCommand = ReactiveCommand.Create(LoadAvailableDevices);
            // The command delivers its result on the taskpool, hop back to the UI thread
            // before touching the ObservableCollection.
            LoadAvailableDevicesCommand
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(devices =>
            {
                // The load is a full replace: on reloads (e.g. after adding a device)
                // the old cards are shut down and dropped first.
                foreach (var old in Scrcpys)
                    old.Shutdown();
                Scrcpys.Clear();

                foreach (var item in devices)
                {
                    Scrcpys.Add(item);
                    // Connect automatically on startup.
                    item.ConnectCommand.Execute().Subscribe();
                }
            });
            AddDeviceCommand = ReactiveCommand.CreateFromTask(AddDevice);
            AddDeviceCommand.ThrownExceptions.Subscribe(ex => log.Error(ex, "AddDevice failed"));

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

        private async Task AddDevice()
        {
            DeviceRecord? record = await AddDeviceInteraction.Handle(new Unit());
            if (record == null || string.IsNullOrWhiteSpace(record.Serial))
                return;

            if (!DeviceDatabase.Add(record.Serial, record.Name))
            {
                UiDiagnostics.Log($"AddDevice: serial '{record.Serial}' is already registered");
                return;
            }
            UiDiagnostics.Log($"AddDevice: registered '{record.Serial}' as '{record.Name}'");

            // Re-run the load so the new device gets a card (when adb sees it).
            await LoadAvailableDevicesCommand.Execute();
        }

        /// <summary>Drops a card from the layout (the device was removed from the database).</summary>
        internal void RemoveCard(ScrcpyViewModel item)
        {
            item.Shutdown();
            Scrcpys.Remove(item);
        }

        private List<ScrcpyViewModel> LoadAvailableDevices()
        {
            try
            {
                var port = 27183;
                List<DeviceRecord> records = DeviceDatabase.GetAll();
                var devices = new AdbClient().GetDevices();
                UiDiagnostics.Log($"LoadAvailableDevices: db='{DeviceDatabase.DatabasePath}' entries={records.Count}, adb devices={devices.Count}");
                List<ScrcpyViewModel> list = new List<ScrcpyViewModel>();
                foreach (var record in records)
                {
                    var deviceToUpdate = devices.FirstOrDefault(d => d.Serial == record.Serial);
                    if (deviceToUpdate != null)
                    {
                        deviceToUpdate.Name = record.Name;
                        var scrcpyvm = new ScrcpyViewModel(deviceToUpdate, port, record.Name, this);
                        list.Add(scrcpyvm);
                        port++;
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
