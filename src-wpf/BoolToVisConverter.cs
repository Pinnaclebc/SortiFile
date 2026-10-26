using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SortiFileWpf
{
    public class BoolToVisConverter : IValueConverter
    {
        public static readonly BoolToVisConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

