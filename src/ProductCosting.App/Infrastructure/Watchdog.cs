using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace ProductCosting.App.Infrastructure;

/// <summary>
/// Notes in app.log when the whole app is paused (e.g. by an antivirus scan) or the window stops responding,
/// so a window that briefly disappears can be explained.
/// </summary>
internal static class Watchdog
{
    static long _lastUiTick;

    public static void Start(Dispatcher ui)
    {
        _lastUiTick = Stopwatch.GetTimestamp();
        var thread = new Thread(() => Run(ui)) { IsBackground = true, Name = "watchdog", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    static void Run(Dispatcher ui)
    {
        var frozenSince = 0L;
        while (true)
        {
            var before = Stopwatch.GetTimestamp();
            Thread.Sleep(500);
            var slept = Stopwatch.GetElapsedTime(before);
            // the watchdog itself didn't run: every thread of the app was stopped from outside
            if (slept > TimeSpan.FromSeconds(1.5))
                AppState.LogEvent($"the whole app was paused for {slept.TotalSeconds:0.0} s (stopped from outside, e.g. antivirus or Windows)");

            ui.BeginInvoke(() => Interlocked.Exchange(ref _lastUiTick, Stopwatch.GetTimestamp()), DispatcherPriority.Send);
            var uiSilent = Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastUiTick));
            if (uiSilent > TimeSpan.FromSeconds(1.2) && frozenSince == 0)
            {
                frozenSince = Stopwatch.GetTimestamp();
                AppState.LogEvent("window not responding (busy)…");
            }
            else if (uiSilent < TimeSpan.FromSeconds(1) && frozenSince != 0)
            {
                AppState.LogEvent($"window responding again after {Stopwatch.GetElapsedTime(frozenSince).TotalSeconds + 1.2:0.0} s");
                frozenSince = 0;
            }
        }
    }

    public static void Watch(Window w)
    {
        // messages Windows sends to the window: shown or hidden from outside, minimised, display / theme / DPI changes
        w.SourceInitialized += (_, _) =>
        {
            var src = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(w)!;
            src.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                switch (msg)
                {
                    case 0x0018: AppState.LogEvent($"WM_SHOWWINDOW {(wParam != IntPtr.Zero ? "show" : "hide")} (reason {lParam})"); break;
                    case 0x0047:
                        var flags = System.Runtime.InteropServices.Marshal.ReadInt32(lParam, IntPtr.Size * 2 + 16);
                        if ((flags & 0x0080) != 0) AppState.LogEvent("window hidden (SWP_HIDEWINDOW)");
                        if ((flags & 0x0040) != 0) AppState.LogEvent("window shown (SWP_SHOWWINDOW)");
                        break;
                    case 0x0005 when wParam == (IntPtr)1: AppState.LogEvent("WM_SIZE minimized"); break;
                    case 0x007E: AppState.LogEvent("WM_DISPLAYCHANGE"); break;
                    case 0x031E: AppState.LogEvent("WM_DWMCOMPOSITIONCHANGED"); break;
                    case 0x031A: AppState.LogEvent("WM_THEMECHANGED"); break;
                    case 0x02E0: AppState.LogEvent("WM_DPICHANGED"); break;
                    case 0x001A: AppState.LogEvent($"WM_SETTINGCHANGE {System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam)}"); break;
                    case 0x0010: AppState.LogEvent("WM_CLOSE"); break;
                    case 0x0002: AppState.LogEvent("WM_DESTROY"); break;
                }
                return IntPtr.Zero;
            });
        };
        w.StateChanged += (_, _) => AppState.LogEvent($"window state: {w.WindowState}");
        w.IsVisibleChanged += (_, e) => AppState.LogEvent($"window visible: {e.NewValue}");
        w.Closing += (_, e) => AppState.LogEvent("window closing");
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => AppState.LogEvent("display settings changed");
        System.Windows.Media.RenderCapability.TierChanged += (_, _) =>
            AppState.LogEvent($"graphics tier changed: {System.Windows.Media.RenderCapability.Tier >> 16}");
    }
}
