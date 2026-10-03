using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace ScrcpyNet.Sample.Wpf
{
    /// <summary>
    /// Multi-boolean visibility converter: returns Visible when every bound
    /// value is false (e.g. "not connected AND not connecting"), Collapsed
    /// otherwise. Pass ConverterParameter=True to invert the result.
    /// </summary>
    public class AllFalseToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool allFalse = values.All(v => v is bool b && !b);

            if (parameter is bool invert && invert)
                allFalse = !allFalse;

            return allFalse ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
