using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ProductCosting.App.Infrastructure;

/// <summary>Dark title bar on Windows 10 (20H1+) and 11, with a caption colour matching the app on Windows 11.</summary>
public static class NativeWindow
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_CAPTION_COLOR = 35;

    /// <summary>Title bar in the theme's colours: dark or light, and on Windows 11 the sidebar colour.</summary>
    public static void UseDarkTitleBar(Window w)
    {
        void Apply()
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return;
                var p = ThemeManager.Current;
                var on = p.Dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(p.Sidebar);
                var color = c.R | (c.G << 8) | (c.B << 16);   // COLORREF 0x00BBGGRR
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref color, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        if (new WindowInteropHelper(w).Handle != IntPtr.Zero) Apply();
        else w.SourceInitialized += (_, _) => Apply();
    }
}
