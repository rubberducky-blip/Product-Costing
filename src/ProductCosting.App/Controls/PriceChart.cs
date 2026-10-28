using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ProductCosting.App.Infrastructure;
using ProductCosting.Core.Localization;
using ProductCosting.Core.Model;

namespace ProductCosting.App.Controls;

/// <summary>Step chart of an item's price over time; the price holds until the next change. Hover shows the details.</summary>
public sealed class PriceChart : FrameworkElement
{
    public static readonly DependencyProperty EntriesProperty = DependencyProperty.Register(
        nameof(Entries), typeof(IReadOnlyList<PriceHistoryEntry>), typeof(PriceChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<PriceHistoryEntry>? Entries
    {
        get => (IReadOnlyList<PriceHistoryEntry>?)GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    int _hover = -1;

    static Brush B(string key) => (Brush)Application.Current.FindResource(key);

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var i = Nearest(e.GetPosition(this).X);
        if (i != _hover) { _hover = i; InvalidateVisual(); }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = -1;
        InvalidateVisual();
    }

    const double Left = 64, Right = 20, Top = 16, Bottom = 34;

    (DateTime Min, DateTime Max) Range()
    {
        var e = Entries!;
        var min = e[0].Date;
        var max = DateTime.Today > e[^1].Date ? DateTime.Today : e[^1].Date;
        if ((max - min).TotalDays < 7) min = max.AddDays(-7);
        return (min, max);
    }

    double X(DateTime d, DateTime min, DateTime max) =>
        Left + (ActualWidth - Left - Right) * ((d - min).TotalDays / Math.Max(1, (max - min).TotalDays));

    int Nearest(double x)
    {
        if (Entries is not { Count: > 0 } e) return -1;
        var (min, max) = Range();
        var best = -1;
        var bestD = double.MaxValue;
        for (var i = 0; i < e.Count; i++)
        {
            var d = Math.Abs(X(e[i].Date, min, max) - x);
            if (d < bestD) { bestD = d; best = i; }
        }
        return bestD < 40 ? best : -1;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        FormattedText Text(string s, Brush b, double size = 11) => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, b, dpi);

        if (Entries is not { Count: > 0 } e)
        {
            var t = Text(Loc.T("history.empty"), B("TextMutedBrush"), 13);
            dc.DrawText(t, new Point((w - t.Width) / 2, (h - t.Height) / 2));
            return;
        }

        var (min, max) = Range();
        var lo = e.Min(x => x.Price);
        var hi = e.Max(x => x.Price);
        if (hi - lo < 0.01m) { lo -= Math.Max(1, lo * 0.1m); hi += Math.Max(1, hi * 0.1m); }
        var pad = (hi - lo) * 0.12m;
        lo = Math.Max(0, lo - pad);
        hi += pad;
        double Y(decimal p) => Top + (h - Top - Bottom) * (double)((hi - p) / (hi - lo));

        // grid + y labels
        var grid = new Pen(B("LineBrush"), 1);
        grid.Freeze();
        for (var i = 0; i <= 4; i++)
        {
            var v = lo + (hi - lo) * i / 4;
            var y = Y(v);
            dc.DrawLine(grid, new Point(Left, y), new Point(w - Right, y));
            var t = Text(Fmt.Money(v), B("TextFaintBrush"));
            dc.DrawText(t, new Point(Left - t.Width - 8, y - t.Height / 2));
        }
        // x labels
        var days = Math.Max(1, (max - min).TotalDays);
        var ticks = Math.Clamp((int)((w - Left - Right) / 110), 2, 8);
        for (var i = 0; i <= ticks; i++)
        {
            var d = min.AddDays(days * i / ticks);
            var t = Text(d.ToString("d MMM yy", Loc.Culture), B("TextFaintBrush"));
            var x = X(d, min, max);
            dc.DrawText(t, new Point(Math.Clamp(x - t.Width / 2, Left - 10, w - Right - t.Width), h - Bottom + 10));
        }

        // step line + area
        var accent = (Color)Application.Current.FindResource("AccentColor");
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        var baseY = h - Bottom;
        using (var lc = line.Open())
        using (var ac = area.Open())
        {
            var p0 = new Point(X(e[0].Date, min, max), Y(e[0].Price));
            lc.BeginFigure(p0, false, false);
            ac.BeginFigure(new Point(p0.X, baseY), true, true);
            ac.LineTo(p0, false, false);
            for (var i = 1; i < e.Count; i++)
            {
                var x = X(e[i].Date, min, max);
                lc.LineTo(new Point(x, Y(e[i - 1].Price)), true, true);
                lc.LineTo(new Point(x, Y(e[i].Price)), true, true);
                ac.LineTo(new Point(x, Y(e[i - 1].Price)), false, false);
                ac.LineTo(new Point(x, Y(e[i].Price)), false, false);
            }
            var end = new Point(X(max, min, max), Y(e[^1].Price));
            lc.LineTo(end, true, true);
            ac.LineTo(end, false, false);
            ac.LineTo(new Point(end.X, baseY), false, false);
        }
        var fill = new LinearGradientBrush(Color.FromArgb(70, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B), 90);
        fill.Freeze();
        dc.DrawGeometry(fill, null, area);
        var pen = new Pen(new SolidColorBrush(accent), 2) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);

        var up = B("DangerBrush");
        var down = B("SuccessBrush");
        for (var i = 0; i < e.Count; i++)
        {
            var p = new Point(X(e[i].Date, min, max), Y(e[i].Price));
            var dot = e[i].OldPrice is decimal o ? (e[i].Price > o ? up : down) : new SolidColorBrush(accent);
            dc.DrawEllipse(B("CardBrush"), new Pen(dot, 2), p, i == _hover ? 6 : 4, i == _hover ? 6 : 4);
        }

        if (_hover >= 0 && _hover < e.Count)
        {
            var en = e[_hover];
            var p = new Point(X(en.Date, min, max), Y(en.Price));
            var dash = new Pen(B("LineStrongBrush"), 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(dash, new Point(p.X, Top), new Point(p.X, baseY));

            var lines = new List<FormattedText>
            {
                Text(en.Date.ToString("D", Loc.Culture), B("TextMutedBrush")),
                Text(en.OldPrice is decimal old && old != 0
                    ? $"{Fmt.Money(old)} → {Fmt.Money(en.Price)}   ({Fmt.Signed((en.Price - old) / old * 100)})"
                    : Fmt.Money(en.Price), B("TextBrush"), 13),
            };
            foreach (var r in en.Reasons.Split()) lines.Add(Text("• " + Strings.Reason(r), B("TextMutedBrush")));
            var tw = lines.Max(t => t.Width) + 24;
            var th = lines.Sum(t => t.Height + 3) + 18;
            var bx = p.X + 14 + tw > w ? p.X - 14 - tw : p.X + 14;
            var by = Math.Clamp(p.Y - th / 2, 0, Math.Max(0, h - th));
            dc.DrawRoundedRectangle(B("PopupBrush"), new Pen(B("LineStrongBrush"), 1), new Rect(bx, by, tw, th), 8, 8);
            var ty = by + 9;
            foreach (var t in lines)
            {
                dc.DrawText(t, new Point(bx + 12, ty));
                ty += t.Height + 3;
            }
        }
    }
}
