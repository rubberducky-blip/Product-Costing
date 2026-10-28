using System.Windows;

namespace ProductCosting.App.Infrastructure;

/// <summary>Icon glyph (Segoe Fluent Icons) shown before a button's content: <c>l:Btn.Icon="&#xE710;"</c>.</summary>
public static class Btn
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Btn), new PropertyMetadata(null));

    public static string? GetIcon(DependencyObject d) => (string?)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, string? v) => d.SetValue(IconProperty, v);
}

/// <summary>Drop hint shown on rows and tiles while dragging: 0 none, 1 before, 2 after, 3 into.</summary>
public static class DropHint
{
    public static readonly DependencyProperty PositionProperty = DependencyProperty.RegisterAttached(
        "Position", typeof(int), typeof(DropHint), new PropertyMetadata(0));

    public static int GetPosition(DependencyObject d) => (int)d.GetValue(PositionProperty);
    public static void SetPosition(DependencyObject d, int v) => d.SetValue(PositionProperty, v);
}
