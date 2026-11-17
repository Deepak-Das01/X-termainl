using System.Windows;
using XTerminal.App.Helpers;
using XTerminal.Core.Settings;

namespace XTerminal.App.Services;

public sealed class ThemeService
{
    private readonly ResourceDictionary _dark;
    private readonly ResourceDictionary _light;

    public ThemeService()
    {
        _dark = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Themes/Theme.Dark.xaml", UriKind.Absolute)
        };
        _light = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Themes/Theme.Light.xaml", UriKind.Absolute)
        };
    }

    public void Apply(Window window, AppSettings settings)
    {
        var useDark = ResolveDark(settings.ThemeMode);
        var app = Application.Current.Resources.MergedDictionaries;

        // Keep Colors + ScrollBars, swap theme pack.
        for (var i = app.Count - 1; i >= 0; i--)
        {
            var src = app[i].Source?.OriginalString ?? string.Empty;
            if (src.Contains("Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("Colors.xaml", StringComparison.OrdinalIgnoreCase))
            {
                app.RemoveAt(i);
            }
        }

        app.Insert(0, useDark ? _dark : _light);

        switch (settings.WindowMaterial)
        {
            case WindowMaterialMode.Solid:
                window.Background = useDark
                    ? (System.Windows.Media.Brush)window.FindResource("BrushAppBackground")
                    : (System.Windows.Media.Brush)window.FindResource("BrushAppBackground");
                break;
            case WindowMaterialMode.Acrylic:
            case WindowMaterialMode.Mica:
            default:
                WindowBackdrop.ApplyMica(window);
                break;
        }
    }

    public static bool ResolveDark(AppThemeMode mode)
    {
        if (mode == AppThemeMode.Dark)
        {
            return true;
        }

        if (mode == AppThemeMode.Light)
        {
            return false;
        }

        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return true;
        }
    }
}
