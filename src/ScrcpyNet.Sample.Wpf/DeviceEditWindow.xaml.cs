using ScrcpyNet.Sample.ViewModels;
using System;
using System.Windows;

namespace ScrcpyNet.Sample.Wpf
{
    /// <summary>
    /// Small modal dialog for registering a device (serial + name) or renaming one
    /// (serial read-only). Opened through the static helpers; DialogResult carries
    /// the outcome back to the interaction handlers in MainWindow.
    /// </summary>
    public partial class DeviceEditWindow : Window
    {
        private DeviceEditWindow()
        {
            InitializeComponent();
            SerialBox.TextChanged += (_, _) => Validate();
            NameBox.TextChanged += (_, _) => Validate();
            Validate();
        }

        /// <summary>Serial as entered (empty in rename mode).</summary>
        public string Serial => SerialBox.Text.Trim();

        /// <summary>Device name as entered.</summary>
        public string EnteredName => NameBox.Text.Trim();

        private void Validate()
        {
            // In rename mode the serial section is collapsed; only the name is required.
            bool serialOk = SerialSection.Visibility != Visibility.Visible || Serial.Length > 0;
            OkButton.IsEnabled = serialOk && EnteredName.Length > 0;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (!OkButton.IsEnabled)
                return;
            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        /// <summary>Dialog for registering a new device; null when cancelled.</summary>
        public static DeviceRecord? ShowAdd(Window owner)
        {
            var window = new DeviceEditWindow
            {
                Title = "添加设备",
                Owner = owner,
            };
            return window.ShowDialog() == true ? new DeviceRecord(window.Serial, window.EnteredName) : null;
        }

        /// <summary>Dialog for renaming an existing device; null when cancelled.</summary>
        public static string? ShowRename(Window owner, string currentName)
        {
            var window = new DeviceEditWindow
            {
                Title = "重命名设备",
                Owner = owner,
            };
            // Rename only edits the name; hide the serial section.
            window.TitleText.Text = "重命名设备";
            window.SerialSection.Visibility = Visibility.Collapsed;
            window.NameBox.Text = currentName;
            window.NameBox.SelectAll();
            return window.ShowDialog() == true ? window.EnteredName : null;
        }
    }
}
