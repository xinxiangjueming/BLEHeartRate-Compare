using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using OxyPlot;

namespace HeartRateMonitor
{
    public class OxyColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is OxyColor oxyColor)
            {
                var color = Color.FromArgb(oxyColor.A, oxyColor.R, oxyColor.G, oxyColor.B);
                return new SolidColorBrush(color);
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}