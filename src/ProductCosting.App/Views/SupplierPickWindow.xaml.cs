using System.Windows;
using ProductCosting.App.Controls;
using ProductCosting.App.Infrastructure;
using ProductCosting.Core.Model;

namespace ProductCosting.App.Views;

/// <summary>Asks which supplier a new order is for.</summary>
public partial class SupplierPickWindow : Window
{
    public long? SupplierId => Picker.SelectedId;

    public SupplierPickWindow()
    {
        InitializeComponent();
        NativeWindow.UseDarkTitleBar(this);
        Title = Loc.T("order.newPick");
        var sups = AppState.Store.GetSuppliers();
        Picker.ItemsSource = PickerEntry.Tree(AppState.Store.GetFolders(ListScope.Suppliers),
            sups.Select(s => (s.Id, s.Name, s.Currency == Currency.Usd ? "$" : null, s.FolderId)));
    }

    void Picker_Picked(object? sender, EventArgs e) => OkButton.IsEnabled = Picker.SelectedId is not null;

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
