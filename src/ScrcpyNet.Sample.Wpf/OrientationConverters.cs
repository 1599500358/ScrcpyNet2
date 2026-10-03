using System;
using System.Globalization;
using System.Windows.Data;
using ScrcpyNet.Sample.ViewModels;

namespace ScrcpyNet.Sample.Wpf
{
    /// <summary>
    /// DeviceOrientationOption -> bool: true when the card should present the video in
    /// landscape. Feeds ScrcpyDisplay.DesiredLandscape, which combines it with the
    /// actual stream orientation to decide the rotation (see ScrcpyDisplay.UpdateRotation).
    /// </summary>
    public class OrientationToLandscapeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is DeviceOrientationOption.Landscape;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? DeviceOrientationOption.Landscape : DeviceOrientationOption.Portrait;
    }
}
