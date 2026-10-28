using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProductCosting.App.Infrastructure;

/// <summary>
/// Clips a Border's content to its rounded corners (<c>l:RoundClip.Enabled="True"</c>),
/// so square content such as a table doesn't poke out of a card's corners.
/// </summary>
public static class RoundClip
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(RoundClip), new PropertyMetadata(false, OnChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border b || e.NewValue is not true) return;
        b.Loaded += (_, _) => Apply(b);
        b.SizeChanged += (_, _) => Apply(b);
    }

    static void Apply(Border b)
    {
        if (b.Child is not FrameworkElement child || child.ActualWidth <= 0) return;
        var r = Math.Max(0, b.CornerRadius.TopLeft - b.BorderThickness.Left / 2);
        var clip = new RectangleGeometry(new Rect(0, 0, child.ActualWidth, child.ActualHeight), r, r);
        clip.Freeze();
        child.Clip = clip;
        child.SizeChanged -= OnChildSize;
        child.SizeChanged += OnChildSize;
    }

    static void OnChildSize(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement { Parent: Border b }) Apply(b);
    }
}
