using System;

namespace ScrcpyNet
{
    public static class TouchHelper
    {
        /// <summary>
        /// Scales a position from the on-screen element's coordinates to the device's
        /// video coordinates. Returns false when the inputs are unusable (element not
        /// laid out yet / no stream size), in which case the caller must not send the
        /// message. Coordinates are clamped to the screen — the raw mouse position can
        /// go negative or past the edge while the pointer leaves the element mid-gesture.
        /// </summary>
        public static bool TryScaleToScreenSize(Position position, int width, int height)
        {
            if (width <= 0 || height <= 0 || position.ScreenSize.Width == 0 || position.ScreenSize.Height == 0)
                return false;

            // The card keeps the video area at the stream's real aspect ratio,
            // so one scale factor serves both axes.
            double scale = (double)width / position.ScreenSize.Width;

            position.ScreenSize.Width = (ushort)width;
            position.ScreenSize.Height = (ushort)height;
            position.Point.X = Math.Clamp((int)(scale * position.Point.X), 0, width - 1);
            position.Point.Y = Math.Clamp((int)(scale * position.Point.Y), 0, height - 1);
            return true;
        }
    }
}
