using System.Windows;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.ViewModels;
using ProductCosting.Core.Model;

namespace ProductCosting.App.Views;

/// <summary>A supplier's order PDF options and note; "back to Settings" takes the options of Settings again.</summary>
public partial class SupplierPdfWindow : Window
{
    readonly OrderPdfForm _form = new();

    public OrderPdfOptions Result => _form.ToOptions();
    public string Note => NoteBox.Text.Trim();

    public SupplierPdfWindow(string supplierName, OrderPdfOptions options, string? note)
    {
        InitializeComponent();
        NativeWindow.UseDarkTitleBar(this);
        Title = TitleText.Text = Loc.F("pdfopt.supplierTitle", supplierName);
        _form.Load(options);
        Options.DataContext = _form;
        NoteBox.Text = note ?? "";
    }

    void Reset_Click(object sender, RoutedEventArgs e) => _form.Load(AppState.Settings.OrderPdf);

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
