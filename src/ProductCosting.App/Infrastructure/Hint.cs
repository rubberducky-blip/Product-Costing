using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ProductCosting.App.Infrastructure;

/// <summary>Placeholder text for a TextBox, drawn while it is empty: <c>l:Hint.Text="…"</c>.</summary>
public static class Hint
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Hint), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string? v) => d.SetValue(TextProperty, v);

    static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        if (e.OldValue is null)
        {
            tb.Loaded += (_, _) => Attach(tb);
            tb.TextChanged += (_, _) => Refresh(tb);
            tb.SizeChanged += (_, _) => Refresh(tb);
            tb.IsVisibleChanged += (_, _) => Refresh(tb);
            tb.GotKeyboardFocus += (_, _) => Refresh(tb);
            if (tb.IsLoaded) Attach(tb);
        }
        Refresh(tb);
    }

    static void Attach(TextBox tb)
    {
        var layer = AdornerLayer.GetAdornerLayer(tb);
        if (layer is null) return;
        if (layer.GetAdorners(tb)?.OfType<HintAdorner>().Any() == true) return;
        layer.Add(new HintAdorner(tb));
    }

    static void Refresh(TextBox tb)
    {
        var layer = AdornerLayer.GetAdornerLayer(tb);
        foreach (var a in layer?.GetAdorners(tb)?.OfType<HintAdorner>() ?? []) a.InvalidateVisual();
    }

    sealed class HintAdorner : Adorner
    {
        readonly TextBox _tb;
        int _retries;

        public HintAdorner(TextBox tb) : base(tb)
        {
            _tb = tb;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var text = GetText(_tb);
            // hidden boxes (e.g. a field shown only for some item types) draw nothing
            if (string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(_tb.Text) || !_tb.IsVisible) return;
            var brush = Application.Current.TryFindResource("TextFaintBrush") as Brush ?? Brushes.Gray;
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, _tb.FlowDirection,
                new Typeface(_tb.FontFamily, _tb.FontStyle, _tb.FontWeight, _tb.FontStretch), _tb.FontSize, brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, _tb.ActualWidth - 24),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            // start (or, for right-aligned boxes, end) the hint exactly where the text cursor is
            var right = _tb.TextAlignment == TextAlignment.Right;
            var caret = Rect.Empty;
            try { caret = _tb.GetRectFromCharacterIndex(0); } catch (ArgumentOutOfRangeException) { }
            double x, y;
            // a small gap from the cursor, so the blinking cursor never covers the first letter
            const double gap = 3;
            if (!caret.IsEmpty && caret.Height > 0 && !double.IsNaN(caret.X) && !double.IsInfinity(caret.X))
            {
                x = right ? caret.X - ft.Width - gap : caret.X + gap;
                y = caret.Top + (caret.Height - ft.Height) / 2;
            }
            else
            {
                // the text area isn't laid out yet (e.g. a dropdown opening for the first time): draw again once it is
                if (_retries++ < 5)
                    Dispatcher.BeginInvoke(InvalidateVisual, System.Windows.Threading.DispatcherPriority.Loaded);
                var box = new Rect(_tb.Padding.Left + _tb.BorderThickness.Left, 0,
                    Math.Max(0, _tb.ActualWidth - _tb.Padding.Left - _tb.Padding.Right), _tb.ActualHeight);
                if (_tb.Template?.FindName("PART_ContentHost", _tb) is FrameworkElement host && host.IsVisible && host.ActualHeight > 0)
                    box = host.TransformToAncestor(_tb).TransformBounds(new Rect(0, 0, host.ActualWidth, host.ActualHeight));
                x = right ? box.Right - ft.Width - 4 - gap : box.Left + 4 + gap;
                y = box.Top + (box.Height - ft.Height) / 2;
            }
            dc.DrawText(ft, new Point(Math.Max(2, x), y));
        }
    }
}
