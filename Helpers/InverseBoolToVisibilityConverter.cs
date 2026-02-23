using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Wolpope.Helpers
{
    /// <summary>
    /// Конвертер: true → Collapsed, false → Visible.
    /// Используется для показа элементов, когда условие НЕ выполнено.
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility.Collapsed;
    }
}
