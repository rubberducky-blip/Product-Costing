using System.Windows;
using System.Windows.Controls;
using ProductCosting.App.Controls;
using ProductCosting.App.ViewModels;

namespace ProductCosting.App.Views;

public partial class ItemEditor : UserControl
{
    public ItemEditor()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is ItemEditorViewModel { IsNew: true }) CodeBox.Focus();
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ItemEditorViewModel old) old.Lines.CollectionChanged -= Lines_Changed;
            if (e.NewValue is ItemEditorViewModel vm) vm.Lines.CollectionChanged += Lines_Changed;
        };
    }

    // a line just added opens its list, so typing filters it straight away
    void Lines_Changed(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems?.OfType<LineRow>().FirstOrDefault(r => r.OpenPicker) is not { } row) return;
        row.OpenPicker = false;
        Dispatcher.BeginInvoke(() =>
        {
            if (FindVisible<SearchPicker>(RecipeList.ItemContainerGenerator.ContainerFromItem(row)) is { } picker) picker.OpenDropDown();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    static T? FindVisible<T>(DependencyObject? root) where T : FrameworkElement
    {
        if (root is null) return null;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t && t.IsVisible) return t;
            if (FindVisible<T>(child) is { } found) return found;
        }
        return null;
    }

    void MaterialPicker_Picked(object? sender, EventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LineRow row } && DataContext is ItemEditorViewModel vm) vm.MaterialPicked(row);
    }

    void SupplierPicker_Picked(object? sender, EventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SourceRow row } && DataContext is ItemEditorViewModel vm) vm.SupplierPicked(row);
    }

    void Crumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Crumb c } && DataContext is ItemEditorViewModel vm) vm.OpenCrumb(c);
    }
}
