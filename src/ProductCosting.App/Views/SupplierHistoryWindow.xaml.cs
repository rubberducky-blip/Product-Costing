using System.Windows;
using System.Windows.Media;
using ProductCosting.App.Infrastructure;
using ProductCosting.Core.Model;

namespace ProductCosting.App.Views;

public sealed class ChangeRow(SupplierChangeDay d)
{
    public string DateText => d.Date.ToString("d", Loc.Culture);
    public string ChangedText => Loc.F("supplierHistory.changed", d.Changed, d.Total);
    public string UpDownText => Loc.F("supplierHistory.upDown", d.Increased, d.Decreased);
    public string AvgText => (d.AvgChangePct > 0 ? "+" : "") + d.AvgChangePct.ToString("0.##", Loc.Culture) + "%";
    public Brush AvgBrush => (Brush)Application.Current.FindResource(d.AvgChangePct > 0 ? "DangerTextBrush" : "SuccessTextBrush");
}

/// <summary>A supplier's purchase-price changes: per day, how many of its items changed and by how much on average.</summary>
public partial class SupplierHistoryWindow : Window
{
    public SupplierHistoryWindow(Supplier supplier)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        // fit the window again once the table has its rows (the first sizing happens before they exist)
        ContentRendered += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            SizeToContent = SizeToContent.Manual;
            SizeToContent = SizeToContent.Height;
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        NativeWindow.UseDarkTitleBar(this);
        Title = TitleText.Text = Loc.F("supplierHistory.title", supplier.Name);
        var days = AppState.Store.GetSupplierHistory(supplier.Id);
        Chart.Days = days;
        Table.ItemsSource = days.AsEnumerable().Reverse().Select(d => new ChangeRow(d)).ToList();
        if (days.Count == 0) TableCard.Visibility = Visibility.Collapsed;
    }

    // Esc closes the window (there is no close button)
    void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) Close();
    }
}
