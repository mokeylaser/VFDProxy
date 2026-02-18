using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VFDProxy.Models;

namespace VFDProxy.Views.Converters;

[ValueConversion(typeof(LogLevel), typeof(Brush))]
public sealed class LogLevelToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (LogLevel)value switch
        {
            LogLevel.Error    => Brushes.OrangeRed,
            LogLevel.Warning  => Brushes.Gold,
            LogLevel.Sent     => Brushes.CornflowerBlue,
            LogLevel.Received => Brushes.MediumSeaGreen,
            LogLevel.Debug    => Brushes.DimGray,
            _                 => Brushes.White
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
