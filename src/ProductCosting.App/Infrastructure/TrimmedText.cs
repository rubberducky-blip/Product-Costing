using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ProductCosting.App.Infrastructure;

/// <summary>Table cells whose text doesn't fit show the whole of it on hover.</summary>
public static class TrimmedText
{
    // marks a tooltip this class set (one the cell had of its own is never replaced)
    static readonly DependencyProperty OursProperty = DependencyProperty.RegisterAttached(
        "Ours", typeof(bool), typeof(TrimmedText), new PropertyMetadata(false));

    public static void Register() =>
        EventManager.RegisterClassHandler(typeof(TextBlock), UIElement.MouseEnterEvent, new MouseEventHandler(OnEnter));

    static void OnEnter(object sender, MouseEventArgs e)
    {
        if (sender is not TextBlock tb || string.IsNullOrEmpty(tb.Text)) return;
        var ours = (bool)tb.GetValue(OursProperty);
        if (tb.ToolTip is not null && !ours) return;
        if (!InCell(tb)) return;
        if (IsCut(tb))
        {
            tb.ToolTip = tb.Text;
            tb.SetValue(OursProperty, true);
        }
        else if (ours)
        {
            tb.ClearValue(FrameworkElement.ToolTipProperty);
            tb.SetValue(OursProperty, false);
        }
    }

    static bool InCell(DependencyObject d)
    {
        for (var p = VisualTreeHelper.GetParent(d); p is not null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is DataGridCell) return true;
            if (p is DataGrid) return false;
        }
        return false;
    }

    /// <summary>The text is wider than the room it has (trimmed with "…" or clipped by its cell).</summary>
    static bool IsCut(TextBlock tb)
    {
        var ft = new FormattedText(tb.Text, CultureInfo.CurrentUICulture, tb.FlowDirection,
            new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch), tb.FontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(tb).PixelsPerDip);
        var room = tb.ActualWidth - tb.Padding.Left - tb.Padding.Right;
        if (VisualTreeHelper.GetParent(tb) is FrameworkElement parent && parent.ActualWidth > 0)
            room = Math.Min(room, parent.ActualWidth - tb.Margin.Left - tb.Margin.Right);
        return ft.WidthIncludingTrailingWhitespace > room + 0.5;
    }
}
