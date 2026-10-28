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
    }

    void MaterialPicker_Picked(object? sender, EventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LineRow row } && DataContext is ItemEditorViewModel vm) vm.MaterialPicked(row);
    }

    void SupplierPicker_Picked(object? sender, EventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SourceRow row } && DataContext is ItemEditorViewModel vm) vm.SupplierPicked(row);
    }
}
