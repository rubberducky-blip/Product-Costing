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

    public static void UseDarkTitleBar(Window w)
    {
        void Apply()
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return;
                var on = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
                var color = 0x001C1714; // COLORREF 0x00BBGGRR → #14171C
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref color, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        if (new WindowInteropHelper(w).Handle != IntPtr.Zero) Apply();
        else w.SourceInitialized += (_, _) => Apply();
    }
}
