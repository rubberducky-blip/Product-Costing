using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ProductCosting.App.Infrastructure;
using ProductCosting.Core.Model;

namespace ProductCosting.App.Controls;

/// <summary>Bars of a supplier's average purchase-price change per day (up red, down green); hover for details.</summary>
public sealed class ChangeChart : FrameworkElement
{
    public static readonly DependencyProperty DaysProperty = DependencyProperty.Register(
        nameof(Days), typeof(IReadOnlyList<SupplierChangeDay>), typeof(ChangeChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<SupplierChangeDay>? Days
    {
        get => (IReadOnlyList<SupplierChangeDay>?)GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    int _hover = -1;
    const double Left = 56, Right = 16, Top = 16, Bottom = 34;

    static Brush B(string key) => (Brush)Application.Current.FindResource(key);

    double SlotWidth => Days is { Count: > 0 } d ? (ActualWidth - Left - Right) / d.Count : 0;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var i = SlotWidth > 0 ? (int)((e.GetPosition(this).X - Left) / SlotWidth) : -1;
        if (Days is null || i < 0 || i >= Days.Count) i = -1;
        if (i != _hover) { _hover = i; InvalidateVisual(); }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface("Segoe UI");
        FormattedText T(string s, Brush b, double size = 11) => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, b, dpi);

        if (Days is not { Count: > 0 } days)
        {
            var t = T(Loc.T("history.empty"), B("TextMutedBrush"), 13);
            dc.DrawText(t, new Point((w - t.Width) / 2, (h - t.Height) / 2));
            return;
        }

        var max = Math.Max(1, (double)days.Max(d => Math.Abs(d.AvgChangePct))) * 1.15;
        var mid = Top + (h - Top - Bottom) / 2;
        double Y(decimal pct) => mid - (double)pct / max * ((h - Top - Bottom) / 2);
        var grid = new Pen(B("LineBrush"), 1);
        foreach (var v in new[] { max, max / 2, 0, -max / 2, -max })
        {
            var y = Y((decimal)v);
            dc.DrawLine(grid, new Point(Left, y), new Point(w - Right, y));
            var t = T((v > 0 ? "+" : "") + v.ToString("0.#", Loc.Culture) + "%", B("TextFaintBrush"));
            dc.DrawText(t, new Point(Left - t.Width - 8, y - t.Height / 2));
        }

        var slot = SlotWidth;
        var bar = Math.Clamp(slot * 0.6, 3, 48);
        for (var i = 0; i < days.Count; i++)
        {
            var d = days[i];
            var x = Left + slot * i + (slot - bar) / 2;
            var y = Y(d.AvgChangePct);
            var rect = new Rect(x, Math.Min(y, mid), bar, Math.Max(1, Math.Abs(y - mid)));
            var brush = d.AvgChangePct >= 0 ? B("DangerBrush") : B("SuccessBrush");
            dc.PushOpacity(i == _hover || _hover < 0 ? 1 : 0.55);
            dc.DrawRoundedRectangle(brush, null, rect, 3, 3);
            dc.Pop();
            if (days.Count <= 16 || i % Math.Max(1, days.Count / 8) == 0)
            {
                var t = T(d.Date.ToString("d MMM yy", Loc.Culture), B("TextFaintBrush"));
                dc.DrawText(t, new Point(Math.Clamp(x + bar / 2 - t.Width / 2, 0, w - t.Width), h - Bottom + 10));
            }
        }

        if (_hover >= 0)
        {
            var d = days[_hover];
            var lines = new[]
            {
                T(d.Date.ToString("D", Loc.Culture), B("TextMutedBrush")),
                T((d.AvgChangePct > 0 ? "+" : "") + d.AvgChangePct.ToString("0.##", Loc.Culture) + "%", B("TextBrush"), 13),
                T(Loc.F("supplierHistory.changed", d.Changed, d.Total), B("TextMutedBrush")),
                T(Loc.F("supplierHistory.upDown", d.Increased, d.Decreased), B("TextMutedBrush")),
            };
            var tw = lines.Max(t => t.Width) + 24;
            var th = lines.Sum(t => t.Height + 3) + 18;
            var bx = Left + slot * _hover + slot / 2;
            bx = bx + 14 + tw > w ? bx - 14 - tw : bx + 14;
            var by = Top;
            dc.DrawRoundedRectangle(B("PopupBrush"), new Pen(B("LineStrongBrush"), 1), new Rect(bx, by, tw, th), 8, 8);
            var ty = by + 9;
            foreach (var t in lines) { dc.DrawText(t, new Point(bx + 12, ty)); ty += t.Height + 3; }
        }
    }
}
