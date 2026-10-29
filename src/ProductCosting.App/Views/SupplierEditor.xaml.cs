using System.Windows.Controls;
using System.Windows.Input;
using ProductCosting.App.ViewModels;
using ProductCosting.Core.Model;

namespace ProductCosting.App.Views;

public partial class SupplierEditor : UserControl
{
    public SupplierEditor()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is SupplierEditorViewModel { IsNew: true }) NameBox.Focus();
        };
    }

    SupplierEditorViewModel? Vm => DataContext as SupplierEditorViewModel;

    void OrdersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindRow(e.OriginalSource) is { DataContext: SavedOrderRow row }) Vm?.OpenOrder(row);
    }

    void OrdersGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // only on a saved order, not on the header
        if (FindRow(e.OriginalSource) is not { } row) { e.Handled = true; return; }
        OrdersGrid.SelectedItem = row.DataContext;
    }

    void Order_Open(object sender, System.Windows.RoutedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is SavedOrderRow row) Vm?.OpenOrder(row);
    }

    void Order_Export(object sender, System.Windows.RoutedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is SavedOrderRow row) Vm?.ExportOrder(row);
    }

    void Order_Delete(object sender, System.Windows.RoutedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is SavedOrderRow row) Vm?.DeleteOrder(row);
    }

    static DataGridRow? FindRow(object source)
    {
        var d = source as System.Windows.DependencyObject;
        while (d is not null and not DataGridRow)
            d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : System.Windows.LogicalTreeHelper.GetParent(d);
        return d as DataGridRow;
    }

    void ItemsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsGrid.SelectedItem is SupplierItemRow row && DataContext is SupplierEditorViewModel vm) vm.OpenItemCommand.Execute(row);
    }
}
