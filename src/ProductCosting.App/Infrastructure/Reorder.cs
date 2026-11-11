using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace ProductCosting.App.Infrastructure;

/// <summary>A page whose lists can be reordered by dragging; told after each move (to count it as an edit).</summary>
public interface IReorderable
{
    void Reordered();
}

/// <summary>
/// Drag-to-reorder for ItemsControls bound to an ObservableCollection: <c>l:Reorder.Grip="True"</c> on the handle inside
/// each item, <c>l:Reorder.Target="True"</c> on the ItemsControl. A line shows where the row will go.
/// </summary>
public static class Reorder
{
    const string Format = "pc-reorder-row";

    public static readonly DependencyProperty GripProperty = DependencyProperty.RegisterAttached(
        "Grip", typeof(bool), typeof(Reorder), new PropertyMetadata(false, OnGripChanged));
    public static bool GetGrip(DependencyObject d) => (bool)d.GetValue(GripProperty);
    public static void SetGrip(DependencyObject d, bool v) => d.SetValue(GripProperty, v);

    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(bool), typeof(Reorder), new PropertyMetadata(false, OnTargetChanged));
    public static bool GetTarget(DependencyObject d) => (bool)d.GetValue(TargetProperty);
    public static void SetTarget(DependencyObject d, bool v) => d.SetValue(TargetProperty, v);

    static Point _start;
    static object? _pressed;

    static void OnGripChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement grip || e.NewValue is not true) return;
        grip.Cursor = Cursors.SizeNS;
        grip.PreviewMouseLeftButtonDown += (_, a) =>
        {
            _start = a.GetPosition(null);
            _pressed = grip.DataContext;
            a.Handled = true;
        };
        grip.PreviewMouseMove += (_, a) =>
        {
            if (a.LeftButton != MouseButtonState.Pressed || _pressed is null || !ReferenceEquals(_pressed, grip.DataContext)) return;
            var p = a.GetPosition(null);
            if (Math.Abs(p.Y - _start.Y) < 4 && Math.Abs(p.X - _start.X) < 4) return;
            var item = _pressed;
            _pressed = null;
            DragDrop.DoDragDrop(grip, new DataObject(Format, item), DragDropEffects.Move);
        };
        grip.PreviewMouseLeftButtonUp += (_, _) => _pressed = null;
    }

    static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl list || e.NewValue is not true) return;
        list.AllowDrop = true;
        list.DragOver += (_, a) =>
        {
            if (!a.Data.GetDataPresent(Format) || list.ItemsSource is not IList items || !items.Contains(a.Data.GetData(Format)))
            {
                a.Effects = DragDropEffects.None;
                Line(list, null);
            }
            else
            {
                a.Effects = DragDropEffects.Move;
                Line(list, InsertY(list, Index(list, a)));
            }
            a.Handled = true;
        };
        list.DragLeave += (_, _) => Line(list, null);
        list.Drop += (_, a) =>
        {
            Line(list, null);
            if (!a.Data.GetDataPresent(Format) || list.ItemsSource is not IList items) return;
            var item = a.Data.GetData(Format);
            var from = items.IndexOf(item);
            if (from < 0) return;
            var to = Index(list, a);
            if (to > from) to--;
            if (to == from) return;
            // ObservableCollection<T>.Move keeps the row's controls (and their state) while it moves
            var move = items.GetType().GetMethod("Move", [typeof(int), typeof(int)]);
            if (move is null) return;
            move.Invoke(items, [from, to]);
            if (list.DataContext is IReorderable page) page.Reordered();
            a.Handled = true;
        };
    }

    /// <summary>Where the dragged row goes: before the first row whose middle is below the mouse.</summary>
    static int Index(ItemsControl list, DragEventArgs a)
    {
        for (var i = 0; i < list.Items.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
            var top = c.TranslatePoint(new Point(0, 0), list).Y;
            if (a.GetPosition(list).Y < top + c.ActualHeight / 2) return i;
        }
        return list.Items.Count;
    }

    static double InsertY(ItemsControl list, int index)
    {
        if (list.Items.Count == 0) return 0;
        if (index < list.Items.Count && list.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement c)
            return c.TranslatePoint(new Point(0, 0), list).Y - 2;
        return list.ActualHeight - 2;
    }

    static void Line(ItemsControl list, double? y)
    {
        var layer = AdornerLayer.GetAdornerLayer(list);
        if (layer is null) return;
        foreach (var old in layer.GetAdorners(list)?.OfType<LineAdorner>() ?? []) layer.Remove(old);
        if (y is double at) layer.Add(new LineAdorner(list, at));
    }

    sealed class LineAdorner(UIElement owner, double y) : Adorner(owner)
    {
        protected override void OnRender(DrawingContext dc)
        {
            var brush = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
            dc.DrawRoundedRectangle(brush, null, new Rect(0, Math.Max(0, y), AdornedElement.RenderSize.Width, 3), 1.5, 1.5);
        }
    }
}
