using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace XTerminal.App.Helpers;

public static class WindowBackdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    private const int DwmsbtMainWindow = 2; // Mica
    private const int DwmwcpRound = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    public static void ApplyMica(Window window)
    {
        void Apply()
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            window.Background = Brushes.Transparent;

            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            var corner = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));

            // Extend frame so Mica paints the whole client area.
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            var mica = DwmsbtMainWindow;
            if (DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref mica, sizeof(int)) != 0)
            {
                window.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20));
            }

            // Transparent caption/border so system chrome blends with Mica.
            var none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref none, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref none, sizeof(int));

            var text = unchecked((int)0x00FFFFFF);
            DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(int));
        }

        if (window.IsLoaded)
        {
            Apply();
        }
        else
        {
            window.SourceInitialized += (_, _) => Apply();
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins pMarInset);
}
