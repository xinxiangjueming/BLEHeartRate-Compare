using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HeartRateMonitor
{
    /// <summary>
    /// Bool → GridLength 转换器。
    /// true 时返回参数指定的宽度（如 "60"），false 时返回 0。
    /// 用于动态隐藏 Grid 列。
    /// </summary>
    public sealed class BoolToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            double width = parameter is string s && double.TryParse(s, out var w) ? w : 0;
            return flag ? new GridLength(width) : new GridLength(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
