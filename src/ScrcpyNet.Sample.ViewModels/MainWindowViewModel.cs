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
                    // Only registered devices auto-connect on startup; unregistered
                    // ones wait for a manual click on the connect button.
                    if (item.IsRegistered)
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

        /// <summary>Applies a drag &amp; drop reorder: moves the card in the layout
        /// and persists the new order of registered devices to the database.
        /// Called by the view (MainWindow drag &amp; drop handlers).</summary>
        public void ReorderCard(ScrcpyViewModel item, ScrcpyViewModel target, bool insertAfter)
        {
            int oldIndex = Scrcpys.IndexOf(item);
            int targetIndex = Scrcpys.IndexOf(target);
            if (oldIndex < 0 || targetIndex < 0 || ReferenceEquals(item, target))
                return;

            int newIndex = insertAfter ? targetIndex + 1 : targetIndex;
            if (oldIndex < newIndex)
                newIndex--; // removing the dragged card shifts later positions left
            if (newIndex == oldIndex)
                return;

            Scrcpys.Move(oldIndex, newIndex);

            // Persist the registered subset in its new order. Serials of
            // unregistered cards match no row and are skipped; on the next load
            // they reappear at the end, after the registered devices.
            DeviceDatabase.Reorder(Scrcpys.Select(s => s.Serial));
            UiDiagnostics.Log("Reordered device cards");
        }

        private List<ScrcpyViewModel> LoadAvailableDevices()
        {
            try
            {
                List<DeviceRecord> records = DeviceDatabase.GetAll();
                var devices = new AdbClient().GetDevices();
                UiDiagnostics.Log($"LoadAvailableDevices: db='{DeviceDatabase.DatabasePath}' entries={records.Count}, adb devices={devices.Count}");
                List<ScrcpyViewModel> list = new List<ScrcpyViewModel>();
                var matched = new HashSet<string>();
                foreach (var record in records)
                {
                    var deviceToUpdate = devices.FirstOrDefault(d => d.Serial == record.Serial);
                    if (deviceToUpdate != null)
                    {
                        deviceToUpdate.Name = record.Name;
                        matched.Add(deviceToUpdate.Serial);
                        var scrcpyvm = new ScrcpyViewModel(deviceToUpdate, GetFreePort(), record.Name, isRegistered: true, this);
                        list.Add(scrcpyvm);
                    }
                }

                // Devices adb sees that are NOT in the database still get a card,
                // appended after the registered ones. They show under their serial
                // and never auto-connect; renaming one registers it in the database.
                int unregistered = 0;
                foreach (var device in devices)
                {
                    if (matched.Contains(device.Serial))
                        continue;
                    list.Add(new ScrcpyViewModel(device, GetFreePort(), device.Serial, isRegistered: false, this));
                    unregistered++;
                }
                UiDiagnostics.Log($"LoadAvailableDevices: matched {list.Count - unregistered} device(s), {unregistered} unregistered device(s)");
                return list;
            }
            catch (Exception ex)
            {
                UiDiagnostics.Log($"LoadAvailableDevices failed: {ex}");
                log.Error("Couldn't load available devices", ex);
                return new List<ScrcpyViewModel>();
            }
        }

        /// <summary>Binds port 0 and reads back the OS-assigned port. A fixed range
        /// (the classic 27183+) collides with other scrcpy clients running on the
        /// machine and with our own earlier sessions. The gap before Scrcpy rebinds
        /// the port is negligible (Scrcpy binds with ReuseAddress), same pattern as
        /// the live test in UnitTest1.</summary>
        private static int GetFreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
