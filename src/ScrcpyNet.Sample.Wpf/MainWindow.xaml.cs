using ReactiveUI;
using ReactiveUI.Reactive;
using ScrcpyNet.Sample.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;

namespace ScrcpyNet.Sample.Wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            if (DataContext is MainWindowViewModel vm)
                RegisterInteractionHandlers(vm);
        }

        /// <summary>
        /// Bridges the view-model's interactions to modal dialogs. ReactiveUI marshals
        /// Handle calls to the context the handlers were registered on (the UI thread).
        /// </summary>
        private void RegisterInteractionHandlers(MainWindowViewModel vm)
        {
            vm.AddDeviceInteraction.RegisterHandler(ctx =>
            {
                ctx.SetOutput(DeviceEditWindow.ShowAdd(this));
            });

            vm.RenameDeviceInteraction.RegisterHandler(ctx =>
            {
                ctx.SetOutput(DeviceEditWindow.ShowRename(this, ctx.Input));
            });

            vm.RemoveDeviceInteraction.RegisterHandler(ctx =>
            {
                ctx.SetOutput(ConfirmWindow.Show(this, "移除设备",
                    $"确定从设备列表移除“{ctx.Input}”吗？\n该设备将断开连接，且不会再自动加载。"));
            });
        }

        private void TextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = !double.TryParse(e.Text, out _);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                foreach (var item in vm.Scrcpys)
                {
                    item.DisconnectCommand.Execute().Subscribe();
                };
            base.OnClosing(e);
        }
    }
}
