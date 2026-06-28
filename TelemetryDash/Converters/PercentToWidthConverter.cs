using System.Globalization;
using System.Windows.Data;

namespace TelemetryDash.Converters;

public class PercentToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double percent)
            return Math.Max(0, percent * 2.0); // 200px max width for bar
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
