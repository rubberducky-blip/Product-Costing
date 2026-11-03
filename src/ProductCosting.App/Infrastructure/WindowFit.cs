using System.Windows;
using System.Windows.Media;

namespace ProductCosting.App.Infrastructure;

/// <summary>
/// Sizes a window's height to its content once it is shown (the custom title bar makes SizeToContent come out short),
/// never taller than the screen: content with a height limit (e.g. a table) scrolls past it.
/// </summary>
public static class WindowFit
{
    public static void HeightToContent(Window w)
    {
        w.MaxHeight = SystemParameters.WorkArea.Height;
        w.ContentRendered += (_, _) => w.Dispatcher.BeginInvoke(() =>
        {
            if (w.Content is not FrameworkElement content || VisualTreeHelper.GetParent(content) is not FrameworkElement host) return;
            content.Measure(new Size(host.ActualWidth, double.PositiveInfinity));
            var extra = content.DesiredSize.Height - host.ActualHeight;
            var height = Math.Min(w.MaxHeight, w.ActualHeight + extra);
            // stay centred where it opened, inside the screen
            var area = SystemParameters.WorkArea;
            w.Top = Math.Clamp(w.Top - (height - w.ActualHeight) / 2, area.Top, Math.Max(area.Top, area.Bottom - height));
            w.Height = height;
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }
}
