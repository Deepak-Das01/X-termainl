using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace XTerminal.App.Converters;

public sealed class StringToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            try
            {
                return (SolidColorBrush)new BrushConverter().ConvertFromString(s)!;
            }
            catch
            {
                // fall through
            }
        }

        return new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
