using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VFDProxy.Models;

namespace VFDProxy.Views.Converters;

[ValueConversion(typeof(ProxyState), typeof(Brush))]
public sealed class ProxyStateToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (ProxyState)value switch
        {
            ProxyState.Running      => Brushes.LimeGreen,
            ProxyState.Connecting   => Brushes.Yellow,
            ProxyState.Error        => Brushes.OrangeRed,
            _                       => Brushes.Gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
