using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ScrcpyNet.Sample.Wpf
{
    /// <summary>
    /// Computes a height from (width, deviceRatio, fallbackRatio) so each card's video
    /// area keeps the REAL screen ratio of its own device (from the stream size), while
    /// unconnected cards fall back to the default ratio (1080x2216 portrait /
    /// 2216x1080 landscape, see AppSettings).
    /// </summary>
    public class AspectRatioHeightConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2
                || values[0] is not double width || width <= 0)
                return DependencyProperty.UnsetValue;

            // Device ratio wins when known (> 0); otherwise the app-wide default.
            double ratio = values.Length >= 2 && values[1] is double device && device > 0
                ? device
                : values.Length >= 3 && values[2] is double fallback && fallback > 0
                    ? fallback
                    : 0;

            return ratio > 0 ? width * ratio : DependencyProperty.UnsetValue;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
