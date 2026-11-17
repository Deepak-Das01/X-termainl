using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace XTerminal.App.Converters;

/// <summary>
/// ConverterParameter selects which settings pane to show.
/// </summary>
public sealed class SettingsCategoryVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var category = value?.ToString() ?? string.Empty;
        var mode = parameter?.ToString() ?? "appearance";
        var show = mode.Equals(category, StringComparison.OrdinalIgnoreCase);
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
