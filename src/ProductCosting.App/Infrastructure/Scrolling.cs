using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ProductCosting.App.Infrastructure;

/// <summary>
/// App-wide scrolling: the mouse wheel passes on to the page when a table under the cursor has nothing (more) to
/// scroll, and a middle click starts browser-like autoscroll (move the mouse away from the mark to scroll).
/// </summary>
public static class Scrolling
{
    public static void Register()
    {
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnMouseDown), true);
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseUpEvent, new MouseButtonEventHandler(OnMouseUp), true);
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnKeyDown), true);
    }

    static T? Ancestor<T>(DependencyObject? d) where T : DependencyObject
    {
        for (d = d is null ? null : Parent(d); d is not null; d = Parent(d))
            if (d is T t) return t;
        return null;
    }

    static T? VisualAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        for (var p = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
             p is not null; p = p is Visual ? VisualTreeHelper.GetParent(p) : null)
            if (p is T t) return t;
        return null;
    }

    static DependencyObject? Parent(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    // ---------- wheel: a table that cannot scroll that way hands the wheel to the page around it ----------

    static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer sv) return;
        // only the innermost scroll area under the cursor decides (outer ones see the wheel first)
        if (e.OriginalSource is DependencyObject src && !ReferenceEquals(src, sv) && VisualAncestor<ScrollViewer>(src) != sv) return;
        var canScroll = e.Delta > 0 ? sv.VerticalOffset > 0 : sv.VerticalOffset < sv.ScrollableHeight - 0.5;
        if (canScroll && sv.ScrollableHeight > 0) return;
        // within the same window or drop-down only (a short drop-down list must not scroll the page under it)
        if (VisualAncestor<ScrollViewer>(sv) is not { } outer) return;
        e.Handled = true;
        outer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = outer });
    }

    // ---------- middle click: autoscroll ----------

    static ScrollViewer? _target;
    static Window? _window;
    static Point _origin;
    static bool _moved;
    static DispatcherTimer? _timer;
    static Popup? _mark;

    static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_target is not null)
        {
            // any click ends autoscroll (and does nothing else)
            Stop();
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Middle || sender is not Window w) return;
        var sv = Scrollable(e.OriginalSource as DependencyObject);
        if (sv is null) return;
        _target = sv;
        _window = w;
        _origin = e.GetPosition(w);
        _moved = false;
        ShowMark(w, _origin);
        Mouse.OverrideCursor = Cursors.ScrollAll;
        _timer ??= new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
        _timer.Tick -= Tick;
        _timer.Tick += Tick;
        _timer.Start();
        e.Handled = true;
    }

    // held and dragged: scrolls while held, stops on release; a plain click keeps scrolling until the next click
    static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_target is null || e.ChangedButton != MouseButton.Middle) return;
        if (_moved) Stop();
        e.Handled = true;
    }

    static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_target is null) return;
        Stop();
        if (e.Key == Key.Escape) e.Handled = true;
    }

    /// <summary>The nearest scroll area under the cursor that has something to scroll.</summary>
    static ScrollViewer? Scrollable(DependencyObject? d)
    {
        for (var sv = d as ScrollViewer ?? Ancestor<ScrollViewer>(d); sv is not null; sv = Ancestor<ScrollViewer>(sv))
            if (sv.ScrollableHeight > 0 || sv.ScrollableWidth > 0) return sv;
        return null;
    }

    static void Tick(object? sender, EventArgs e)
    {
        if (_target is null || _window is null || !_target.IsVisible) { Stop(); return; }
        var p = Mouse.GetPosition(_window);
        double Speed(double d)
        {
            const double dead = 12;
            var a = Math.Abs(d);
            if (a <= dead) return 0;
            _moved = true;
            // gentle near the mark, faster further away
            return Math.Sign(d) * Math.Min(80, Math.Pow((a - dead) / 10, 1.6));
        }
        var dy = Speed(p.Y - _origin.Y);
        var dx = Speed(p.X - _origin.X);
        if (dy != 0) _target.ScrollToVerticalOffset(_target.VerticalOffset + dy);
        if (dx != 0) _target.ScrollToHorizontalOffset(_target.HorizontalOffset + dx);
    }

    static void Stop()
    {
        _timer?.Stop();
        _target = null;
        _window = null;
        Mouse.OverrideCursor = null;
        if (_mark is not null) _mark.IsOpen = false;
    }

    // the mark where autoscroll started: a small circle with up / down arrows
    static void ShowMark(Window w, Point at)
    {
        _mark ??= new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            IsHitTestVisible = false,
            Child = MarkVisual(),
        };
        _mark.PlacementTarget = w;
        _mark.HorizontalOffset = at.X - 14;
        _mark.VerticalOffset = at.Y - 14;
        _mark.IsOpen = true;
    }

    static UIElement MarkVisual()
    {
        var g = new Grid { Width = 28, Height = 28 };
        var circle = new Ellipse { StrokeThickness = 1.5 };
        circle.SetResourceReference(Shape.FillProperty, "PopupBrush");
        circle.SetResourceReference(Shape.StrokeProperty, "LineStrongBrush");
        g.Children.Add(circle);
        foreach (var up in new[] { true, false })
        {
            var arrow = new Path
            {
                Data = Geometry.Parse(up ? "M 9,11 L 14,6 L 19,11 Z" : "M 9,17 L 14,22 L 19,17 Z"),
            };
            arrow.SetResourceReference(Shape.FillProperty, "TextMutedBrush");
            g.Children.Add(arrow);
        }
        var dot = new Ellipse { Width = 4, Height = 4 };
        dot.SetResourceReference(Shape.FillProperty, "TextMutedBrush");
        g.Children.Add(dot);
        return g;
    }
}
