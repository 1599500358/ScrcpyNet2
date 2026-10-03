using ReactiveUI;
using ReactiveUI.Reactive;
using ReactiveUI.SourceGenerators;
using System;
using System.IO;
using System.Reactive.Linq;
using System.Text.Json;

namespace ScrcpyNet.Sample.ViewModels
{
    /// <summary>Device rotation option for the card layout.</summary>
    public enum DeviceOrientationOption
    {
        /// <summary>竖屏（1080 x 2216）</summary>
        Portrait,

        /// <summary>横屏（2216 x 1080）</summary>
        Landscape,
    }

    /// <summary>
    /// Global UI settings, shown in the top bar of the main window and persisted to
    /// disk so they survive an app restart. Access everywhere via <see cref="Instance"/>.
    /// </summary>
    public partial class AppSettings : ViewModelBase
    {
        // 注意：这些静态字段必须声明在 Instance 之前——静态字段按文本顺序初始化，
        // Instance 的构造会调用 Load()，如果路径字段还没初始化，FilePath 会是 null，
        // 导致每次启动都读不到已保存的设置。
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScrcpyNet");
        private static readonly string FilePath = Path.Combine(Dir, "settings.json");

        public static AppSettings Instance { get; } = new AppSettings();

        /// <summary>竖屏卡片高宽比：2216 / 1080。</summary>
        public const double PortraitRatio = 2216.0 / 1080.0;

        /// <summary>横屏卡片高宽比：1080 / 2216。</summary>
        public const double LandscapeRatio = 1080.0 / 2216.0;

        /// <summary>Number of device cards per row (2-5).</summary>
        [Reactive] public partial int CardsPerRow { get; set; } = 4;

        /// <summary>Device rotation, changes the card aspect ratio.</summary>
        [Reactive] public partial DeviceOrientationOption Orientation { get; set; } = DeviceOrientationOption.Portrait;

        /// <summary>Card height / width ratio derived from the current orientation.</summary>
        public double AspectRatio => Orientation == DeviceOrientationOption.Portrait ? PortraitRatio : LandscapeRatio;

        private AppSettings()
        {
            Load();

            // Persist whenever a setting changes (initial values are not saved again).
            this.WhenAnyValue(x => x.CardsPerRow).Skip(1).Subscribe(_ => Save());
            this.WhenAnyValue(x => x.Orientation).Skip(1).Subscribe(_ => Save());

            // AspectRatio is derived from Orientation.
            this.WhenAnyValue(x => x.Orientation)
                .Subscribe(_ => this.RaisePropertyChanged(nameof(AspectRatio)));
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return;

                var data = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(FilePath));
                if (data == null)
                    return;

                if (data.CardsPerRow is >= 2 and <= 5)
                    CardsPerRow = data.CardsPerRow;

                if (Enum.IsDefined(typeof(DeviceOrientationOption), data.Orientation))
                    Orientation = data.Orientation;

                UiDiagnostics.Log($"Settings loaded from '{FilePath}': CardsPerRow={CardsPerRow}, Orientation={Orientation}");
            }
            catch (Exception ex)
            {
                // A broken settings file must never prevent the app from starting.
                UiDiagnostics.Log($"Failed to load settings from '{FilePath}': {ex.Message}");
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var data = new SettingsData { CardsPerRow = CardsPerRow, Orientation = Orientation };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
                UiDiagnostics.Log($"Settings saved: CardsPerRow={CardsPerRow}, Orientation={Orientation}");
            }
            catch (Exception ex)
            {
                UiDiagnostics.Log($"Failed to save settings: {ex.Message}");
            }
        }

        private sealed class SettingsData
        {
            public int CardsPerRow { get; set; } = 4;
            public DeviceOrientationOption Orientation { get; set; } = DeviceOrientationOption.Portrait;
        }
    }
}
