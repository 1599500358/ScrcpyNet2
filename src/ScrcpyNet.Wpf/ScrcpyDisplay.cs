using Serilog;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ScrcpyNet.Wpf
{
    /// <summary>
    /// Follow steps 1a or 1b and then 2 to use this custom control in a XAML file.
    ///
    /// Step 1a) Using this custom control in a XAML file that exists in the current project.
    /// Add this XmlNamespace attribute to the root element of the markup file where it is 
    /// to be used:
    ///
    ///     xmlns:MyNamespace="clr-namespace:ScrcpyNet.Wpf"
    ///
    ///
    /// Step 1b) Using this custom control in a XAML file that exists in a different project.
    /// Add this XmlNamespace attribute to the root element of the markup file where it is 
    /// to be used:
    ///
    ///     xmlns:MyNamespace="clr-namespace:ScrcpyNet.Wpf;assembly=ScrcpyNet.Wpf"
    ///
    /// You will also need to add a project reference from the project where the XAML file lives
    /// to this project and Rebuild to avoid compilation errors:
    ///
    ///     Right click on the target project in the Solution Explorer and
    ///     "Add Reference"->"Projects"->[Select this project]
    ///
    ///
    /// Step 2)
    /// Go ahead and use your control in the XAML file.
    ///
    ///     <MyNamespace:ScrcpyDisplay/>
    ///
    /// </summary>
    public class ScrcpyDisplay : Control
    {
        private static readonly ILogger log = Log.ForContext<ScrcpyDisplay>();

        public static readonly DependencyProperty ScrcpyProperty = DependencyProperty.Register(
            nameof(Scrcpy),
            typeof(Scrcpy),
            typeof(ScrcpyDisplay),
            new PropertyMetadata(OnScrcpyChanged));

        private static readonly DependencyPropertyKey StreamIsLandscapePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StreamIsLandscape),
            typeof(bool),
            typeof(ScrcpyDisplay),
            new PropertyMetadata(false));

        public static readonly DependencyProperty StreamIsLandscapeProperty = StreamIsLandscapePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey DisplayedAspectRatioPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(DisplayedAspectRatio),
            typeof(double),
            typeof(ScrcpyDisplay),
            new PropertyMetadata(0.0));

        public static readonly DependencyProperty DisplayedAspectRatioProperty = DisplayedAspectRatioPropertyKey.DependencyProperty;

        /// <summary>
        /// Whether the card wants a landscape presentation (the user's 设备方向 setting).
        /// Together with <see cref="StreamIsLandscape"/> this decides the rotation in
        /// <see cref="UpdateRotation"/>: the element size and the rotation are applied
        /// atomically in one place, so they can never disagree (which showed the video
        /// clipped to a middle strip).
        /// </summary>
        public static readonly DependencyProperty DesiredLandscapeProperty = DependencyProperty.Register(
            nameof(DesiredLandscape),
            typeof(bool),
            typeof(ScrcpyDisplay),
            new PropertyMetadata(false, OnRotationInputChanged));

        private Image? renderTarget;
        private WriteableBitmap? bmp;

        static ScrcpyDisplay()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ScrcpyDisplay), new FrameworkPropertyMetadata(typeof(ScrcpyDisplay)));
        }

        public Scrcpy? Scrcpy
        {
            get => (Scrcpy)GetValue(ScrcpyProperty);
            set => SetValue(ScrcpyProperty, value);
        }

        /// <summary>
        /// Whether the current video stream is landscape (frame width &gt; height). The card
        /// layout combines this with the presentation orientation setting to rotate the
        /// video only when the two orientations don't match (see ScrcpyItem.xaml).
        /// </summary>
        public bool StreamIsLandscape
        {
            get => (bool)GetValue(StreamIsLandscapeProperty);
            private set => SetValue(StreamIsLandscapePropertyKey, value);
        }

        public bool DesiredLandscape
        {
            get => (bool)GetValue(DesiredLandscapeProperty);
            set => SetValue(DesiredLandscapeProperty, value);
        }

        /// <summary>
        /// Height/width ratio the video area should have so the device's real screen
        /// ratio shows without letterboxing: derived from the actual stream size and the
        /// rotation state. 0 while no stream is connected (the card falls back to the
        /// default ratio from AppSettings). Updated whenever the stream size or the
        /// presentation orientation changes.
        /// </summary>
        public double DisplayedAspectRatio
        {
            get => (double)GetValue(DisplayedAspectRatioProperty);
            private set => SetValue(DisplayedAspectRatioPropertyKey, value);
        }

        /// <summary>
        /// Single source of truth for the client-side rotation: rotates the element -90°
        /// only when the stream orientation doesn't match the presentation orientation.
        /// Uses a LayoutTransform (not a RenderTransform) so the change goes through the
        /// layout path and reliably reaches the screen composition — a RenderTransform
        /// here left the on-screen composition showing the unrotated middle strip.
        /// </summary>
        private void UpdateRotation()
        {
            bool rotate = DesiredLandscape != StreamIsLandscape;

            UpdateDisplayedAspectRatio(rotate);

            if (rotate == (LayoutTransform is System.Windows.Media.RotateTransform))
                return;

            LayoutTransform = rotate ? new System.Windows.Media.RotateTransform(-90) : null;

            log.Information("Rotation update: streamLandscape={Stream} desiredLandscape={Desired} -> rotate={Rotate}",
                StreamIsLandscape, DesiredLandscape, rotate);
        }

        /// <summary>
        /// Video-area height/width for the device's real screen ratio: the stream shown
        /// as-is needs height/width = h/w; when rotated the displayed box swaps, so the
        /// ratio becomes w/h. 0 = unknown (no stream yet), the card then uses its default.
        /// </summary>
        private void UpdateDisplayedAspectRatio(bool rotate)
        {
            double ratio = streamWidth > 0 && streamHeight > 0
                ? (rotate ? streamWidth / (double)streamHeight : streamHeight / (double)streamWidth)
                : 0.0;

            DisplayedAspectRatio = ratio;
        }

        private static void OnRotationInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrcpyDisplay display)
                display.UpdateRotation();
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (GetTemplateChild("PART_RenderTargetImage") is Image img)
                renderTarget = img;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            // For some reason WPF doesn't focus the control on click??
            Focus();

            if (Scrcpy != null)
            {
                if (e.RightButton == MouseButtonState.Pressed)
                {
                    e.Handled = true;
                    Scrcpy.SendControlCommand(new BackOrScreenOnControlMessage() { Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN });
                    Scrcpy.SendControlCommand(new BackOrScreenOnControlMessage() { Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP });
                }
                else if (e.LeftButton == MouseButtonState.Pressed)
                {
                    e.Handled = true;
                    // Capture so dragging out of the element keeps delivering MOVE and
                    // (crucially) the final UP — otherwise the device would think the
                    // finger is still pressed at the last position.
                    CaptureMouse();
                    SendTouchCommand(AndroidMotionEventAction.AMOTION_EVENT_ACTION_DOWN, e);
                }
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (IsMouseCaptured)
                ReleaseMouseCapture();

            if (Scrcpy != null)
            {
                e.Handled = true;
                SendTouchCommand(AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP, e);
            }

            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (Scrcpy != null && renderTarget != null)
            {
                var point = e.GetPosition(renderTarget);

                if (e.LeftButton == MouseButtonState.Pressed && point.X >= 0 && point.Y >= 0)
                {
                    // Do we need to set e.Handled?
                    SendTouchCommand(AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE, e);
                }
            }

            base.OnMouseMove(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Scrcpy != null)
            {
                e.Handled = true;

                var msg = new KeycodeControlMessage();
                msg.Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN;
                msg.KeyCode = KeycodeHelper.ConvertKey(e.Key);
                msg.Metastate = KeycodeHelper.ConvertModifiers(e.KeyboardDevice.Modifiers);
                Scrcpy.SendControlCommand(msg);
            }

            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (Scrcpy != null)
            {
                e.Handled = true;

                var msg = new KeycodeControlMessage();
                msg.Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP;
                msg.KeyCode = KeycodeHelper.ConvertKey(e.Key);
                msg.Metastate = KeycodeHelper.ConvertModifiers(e.KeyboardDevice.Modifiers);
                Scrcpy.SendControlCommand(msg);
            }

            base.OnKeyUp(e);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            var pos = GetScrcpyMousePosition(e);
            if (Scrcpy != null && pos != null)
            {
                e.Handled = true;

                var msg = new ScrollEventControlMessage();
                msg.Position = pos;
                msg.VerticalScroll = e.Delta / 120; // Random guess
                msg.HorizontalScroll = 0; // TODO: Can we implement this?
                Scrcpy.SendControlCommand(msg);
            }

            base.OnMouseWheel(e);
        }

        protected void SendTouchCommand(AndroidMotionEventAction action, MouseEventArgs e)
        {
            var pos = GetScrcpyMousePosition(e);
            if (Scrcpy != null && pos != null)
            {
                var msg = new TouchEventControlMessage();
                msg.Action = action;
                msg.Position = pos;
                Scrcpy.SendControlCommand(msg);

                log.Debug("Sending {Action} for position {PositionX}, {PositionY}", action, msg.Position.Point.X, msg.Position.Point.Y);
            }
        }

        private Position? GetScrcpyMousePosition(MouseEventArgs e)
        {
            if (Scrcpy == null || renderTarget == null) return null;

            var point = e.GetPosition(renderTarget);

            var pos = new Position();
            pos.Point = new Point { X = (int)point.X, Y = (int)point.Y };
            pos.ScreenSize.Width = (ushort)renderTarget.ActualWidth;
            pos.ScreenSize.Height = (ushort)renderTarget.ActualHeight;
            // Null when the element has no size yet or the stream size is unknown.
            return TouchHelper.TryScaleToScreenSize(pos, Scrcpy.Width, Scrcpy.Height)
                ? pos
                : null;
        }

        private unsafe void OnFrame(object? sender, FrameData frameData)
        {
            if (renderTarget != null)
            {
                // This probably isn't the best way to do this.
                try
                {
                    // The timeout is required. Otherwise this will block forever when the application is about to exit but the videoThread sends a last frame.
                    // The DispatcherPriority has been randomly selected, so it might not be the optimal value.
                    Dispatcher.Invoke(() =>
                    {
                        // The decoder is another source of video size changes (physical
                        // rotation), keep the stream orientation in sync on every frame.
                        UpdateStreamOrientation(frameData.Width, frameData.Height);

                        if (bmp == null || bmp.Width != frameData.Width || bmp.Height != frameData.Height)
                        {
                            bmp = new WriteableBitmap(frameData.Width, frameData.Height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
                            renderTarget.Source = bmp;
                        }

                        try
                        {
                            bmp.Lock();
                            bmp.WritePixels(new Int32Rect(0, 0, frameData.Width, frameData.Height), frameData.Ptr,frameData.DataSize, frameData.Width * 4);
                        }
                        finally
                        {
                            bmp.Unlock();
                        }
                    }, DispatcherPriority.Send, default, TimeSpan.FromMilliseconds(200));
                }
                catch (TimeoutException)
                {
                    log.Debug("Ignoring TimeoutException inside OnFrame.");
                }
                catch (TaskCanceledException)
                {
                    log.Debug("Ignoring TaskCanceledException inside OnFrame.");
                }
            }
        }

        /// <summary>
        /// Keeps <see cref="StreamIsLandscape"/> and the stream size in sync with the
        /// announced video size. Must run on the UI thread (int captures make the
        /// cross-thread hop safe).
        /// </summary>
        private void UpdateStreamOrientation(int width, int height)
        {
            if (width <= 0 || height <= 0)
                return;

            streamWidth = width;
            streamHeight = height;
            StreamIsLandscape = width > height;
            UpdateRotation();
        }

        /// <summary>Current stream size in pixels (0 = no stream), drives DisplayedAspectRatio.</summary>
        private int streamWidth, streamHeight;

        private void OnVideoSizeChanged()
        {
            // Fires on the video thread whenever the server (re)announces the video
            // size, e.g. after the device was physically rotated.
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => OnVideoSizeChanged());
                return;
            }

            var scrcpy = Scrcpy;
            if (scrcpy == null) return;

            int width = scrcpy.Width, height = scrcpy.Height;
            UpdateStreamOrientation(width, height);
        }

        private static void OnScrcpyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is ScrcpyDisplay display)
            {
                // Unsubscribe on the old scrcpy
                if (e.OldValue is Scrcpy old && old != null)
                {
                    old.VideoStreamDecoder.OnFrame -= display.OnFrame;
                    old.VideoSizeChanged -= display.OnVideoSizeChanged;
                }

                // Subscribe on the new scrcpy
                if (e.NewValue is Scrcpy value && value != null)
                {
                    value.VideoStreamDecoder.OnFrame += display.OnFrame;
                    value.VideoSizeChanged += display.OnVideoSizeChanged;

                    // The initial size was announced inside Start(), before we could
                    // subscribe, so seed the orientation from the current dimensions.
                    display.UpdateStreamOrientation(value.Width, value.Height);
                }
            }
        }
    }
}
