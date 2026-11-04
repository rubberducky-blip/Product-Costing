using System.Windows;
using ProductCosting.App.Controls;
using ProductCosting.App.Infrastructure;
using ProductCosting.Core.Model;

namespace ProductCosting.App.Views;

/// <summary>An import names a supplier that doesn't exist: create it, or give its codes to an existing supplier.</summary>
public partial class ImportSupplierWindow : Window
{
    /// <summary>The supplier chosen instead; null = create the new one.</summary>
    public long? ExistingId => ExistingOption.IsChecked == true ? Picker.SelectedId : null;

    public ImportSupplierWindow(string name, int codes)
    {
        InitializeComponent();
        NativeWindow.UseDarkTitleBar(this);
        Title = Loc.T("import.title");
        TitleText.Text = Loc.F("import.unknownSupplier", name);
        BodyText.Text = Loc.F("import.unknownSupplierBody", codes);
        CreateOption.Content = Loc.F("import.createSupplier", name);
        var sups = AppState.Store.GetSuppliers();
        Picker.ItemsSource = PickerEntry.Tree(AppState.Store.GetFolders(ListScope.Suppliers),
            sups.Select(s => (s.Id, s.Name, s.Currency == Currency.Usd ? "$" : null, s.FolderId)));
        ExistingOption.IsEnabled = sups.Count > 0;
    }

    void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (Picker is null) return;
        Picker.IsEnabled = ExistingOption.IsChecked == true;
        OkButton.IsEnabled = ExistingOption.IsChecked != true || Picker.SelectedId is not null;
    }

    void Picker_Picked(object? sender, EventArgs e) => OkButton.IsEnabled = Picker.SelectedId is not null || ExistingOption.IsChecked != true;

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
