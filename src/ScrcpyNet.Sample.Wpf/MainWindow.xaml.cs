using ReactiveUI;
using ReactiveUI.Reactive;
using ScrcpyNet.Sample.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

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

        private void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !double.TryParse(e.Text, out _);
        }

        // ============  Manual card ordering (drag & drop)  ============
        // Dragging starts only from a card header (ScrcpyItem.DragHandle) so the
        // video area keeps its mouse events for touch injection; the drop target
        // can be any part of a card. Insert before/after by the drop's half.

        private ScrcpyViewModel? dragItem;
        private System.Windows.Point dragStart;

        private void Cards_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            dragStart = e.GetPosition(null);
            dragItem = DraggableCardAt(e.OriginalSource as DependencyObject)?.DataContext as ScrcpyViewModel;
        }

        private void Cards_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                dragItem = null;
                return;
            }
            if (dragItem == null)
                return;

            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(dragItem), DragDropEffects.Move);
            dragItem = null; // the modal drag loop has ended (drop or cancel)
        }

        private void Cards_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(ScrcpyViewModel))
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void Cards_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(ScrcpyViewModel)))
                return;
            if (e.OriginalSource is not DependencyObject source)
                return;

            var targetCard = FindAncestor<ScrcpyItem>(source);
            if (targetCard?.DataContext is not ScrcpyViewModel target)
                return;

            bool insertAfter = e.GetPosition(targetCard).Y > targetCard.ActualHeight / 2;
            (DataContext as MainWindowViewModel)?.ReorderCard(
                (ScrcpyViewModel)e.Data.GetData(typeof(ScrcpyViewModel))!, target, insertAfter);
            e.Handled = true;
        }

        /// <summary>The card whose header (drag handle) contains the visual
        /// source; null when the press did not land on any card header.</summary>
        private static ScrcpyItem? DraggableCardAt(DependencyObject? source)
        {
            var card = FindAncestor<ScrcpyItem>(source);
            if (card == null)
                return null;

            var handle = card.DragHandle;
            for (var node = source; node != null; node = VisualTreeHelper.GetParent(node))
            {
                if (ReferenceEquals(node, handle))
                    return card;
                if (ReferenceEquals(node, card))
                    return null; // left the header before reaching it
            }
            return null;
        }

        private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
        {
            while (node is not T && node != null)
                node = VisualTreeHelper.GetParent(node);
            return (T?)node;
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
