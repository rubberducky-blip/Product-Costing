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

    void ItemsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsGrid.SelectedItem is SupplierItemRow row && DataContext is SupplierEditorViewModel vm) vm.OpenItemCommand.Execute(row);
    }
}
