using System.Windows;

namespace ScrcpyNet.Sample.Wpf
{
    /// <summary>
    /// Minimal styled confirmation dialog (MessageBox replacement matching the app
    /// theme). <see cref="Show"/> returns true when the destructive action is confirmed.
    /// </summary>
    public partial class ConfirmWindow : Window
    {
        private ConfirmWindow()
        {
            InitializeComponent();
        }

        private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

        private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

        /// <summary>Shows the confirmation dialog; true = confirmed.</summary>
        public static bool Show(Window owner, string title, string message)
        {
            var window = new ConfirmWindow
            {
                Title = title,
                Owner = owner,
            };
            window.MessageText.Text = message;
            return window.ShowDialog() == true;
        }
    }
}
