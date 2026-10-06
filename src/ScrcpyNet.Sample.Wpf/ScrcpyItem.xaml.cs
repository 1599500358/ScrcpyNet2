using ReactiveUI.Reactive;
using ScrcpyNet.Sample.ViewModels;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ScrcpyNet.Sample.Wpf
{
    /// <summary>
    /// ScrcpyItem.xaml 的交互逻辑
    /// </summary>
    public partial class ScrcpyItem : ReactiveUserControl<ScrcpyViewModel>
    {
        const string CopyGlyph = "\uE8C8";
        const string CheckGlyph = "\uE73E";

        public ScrcpyItem()
        {
            InitializeComponent();
        }

        /// <summary>The card header doubles as the drag handle for manual card
        /// ordering; the video area is excluded so touch injection keeps working.</summary>
        internal Grid DragHandle => CardHeader;

        /// <summary>Copies the device's adb serial to the clipboard and flashes
        /// a checkmark on the button as confirmation. Reads the model from
        /// DataContext: the card is hosted in a DataTemplate, which sets
        /// DataContext but never the ReactiveUserControl.ViewModel property.</summary>
        private void CopySerial_Click(object sender, RoutedEventArgs e)
        {
            string? serial = (DataContext as ScrcpyViewModel)?.Serial;
            if (string.IsNullOrEmpty(serial) || sender is not Button button)
                return;

            if (!TrySetClipboard(serial))
                return;

            UiDiagnostics.Log($"Copied adb serial '{serial}' to the clipboard");

            button.Content = CheckGlyph;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                button.Content = CopyGlyph;
            };
            timer.Start();
        }

        /// <summary>The clipboard can be transiently locked by another process; retry once.</summary>
        private static bool TrySetClipboard(string text)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return true;
                }
                catch (COMException)
                {
                    Thread.Sleep(50);
                }
            }
            return false;
        }
    }
}
