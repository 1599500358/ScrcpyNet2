using System;
using System.Globalization;
using System.Windows.Data;

namespace ScrcpyNet.Sample.Wpf
{
    public class IntegerValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // DoNothing while the binding still delivers null (e.g. no selection yet).
            return value?.ToString() ?? Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str && targetType == typeof(double) && double.TryParse(str, out var i))
            {
                return i;
            }

            throw new NotSupportedException();
        }
    }
}
