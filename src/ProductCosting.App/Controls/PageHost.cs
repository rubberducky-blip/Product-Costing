using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using ProductCosting.App.ViewModels;

namespace ProductCosting.App.Controls;

/// <summary>
/// Shows the current page. List pages keep their view (table, scroll position, realised rows) between visits,
/// so switching lists doesn't rebuild large tables; forms get a fresh view each time.
/// </summary>
public sealed class PageHost : ContentControl
{
    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(object), typeof(PageHost), new PropertyMetadata(null, (d, e) => ((PageHost)d).Show(e.NewValue)));

    public object? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    // the view lives as long as its view model (released together)
    readonly ConditionalWeakTable<object, FrameworkElement> _views = new();

    public PageHost() => Focusable = false;

    void Show(object? page)
    {
        if (page is null)
        {
            Content = null;
            return;
        }
        if (page is ListPageViewModel && _views.TryGetValue(page, out var cached))
        {
            Content = cached;
            return;
        }
        var template = (DataTemplate)FindResource(new DataTemplateKey(page.GetType()));
        var view = (FrameworkElement)template.LoadContent();
        view.DataContext = page;
        if (page is ListPageViewModel) _views.AddOrUpdate(page, view);
        Content = view;
    }
}
