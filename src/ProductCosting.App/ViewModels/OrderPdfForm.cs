using CommunityToolkit.Mvvm.ComponentModel;
using ProductCosting.App.Infrastructure;
using ProductCosting.Core.Model;

namespace ProductCosting.App.ViewModels;

/// <summary>The order PDF options as a form (Settings, and a supplier's own options).</summary>
public sealed partial class OrderPdfForm : ObservableObject
{
    public event Action? Changed;
    bool _loading;

    [ObservableProperty] private bool _number;
    [ObservableProperty] private bool _supplierCode;
    [ObservableProperty] private bool _ourCode;
    [ObservableProperty] private bool _name;
    [ObservableProperty] private bool _variant;
    [ObservableProperty] private bool _quantity;
    [ObservableProperty] private bool _unitPrice;
    [ObservableProperty] private bool _value;
    [ObservableProperty] private string _language = "el";
    [ObservableProperty] private int _fontSize = 9;
    [ObservableProperty] private OrderPdfHeader _header;

    public List<Choice<string>> Languages { get; } = [new("el", "Ελληνικά"), new("en", "English")];
    public List<Choice<int>> FontSizes { get; } = OrderPdfOptions.FontSizes.Select(s => new Choice<int>(s, $"{s} pt")).ToList();
    public List<Choice<OrderPdfHeader>> Headers { get; } =
        Enum.GetValues<OrderPdfHeader>().Select(h => new Choice<OrderPdfHeader>(h, Loc.T("pdfopt.header." + h))).ToList();

    public void Load(OrderPdfOptions o)
    {
        _loading = true;
        Number = o.Columns.HasFlag(OrderPdfColumns.Number);
        SupplierCode = o.Columns.HasFlag(OrderPdfColumns.SupplierCode);
        OurCode = o.Columns.HasFlag(OrderPdfColumns.OurCode);
        Name = o.Columns.HasFlag(OrderPdfColumns.Name);
        Variant = o.Columns.HasFlag(OrderPdfColumns.Variant);
        Quantity = o.Columns.HasFlag(OrderPdfColumns.Quantity);
        UnitPrice = o.Columns.HasFlag(OrderPdfColumns.UnitPrice);
        Value = o.Columns.HasFlag(OrderPdfColumns.Value);
        Language = o.Language;
        FontSize = o.FontSize;
        Header = o.Header;
        _loading = false;
    }

    public OrderPdfOptions ToOptions()
    {
        var cols = OrderPdfColumns.None;
        if (Number) cols |= OrderPdfColumns.Number;
        if (SupplierCode) cols |= OrderPdfColumns.SupplierCode;
        if (OurCode) cols |= OrderPdfColumns.OurCode;
        if (Name) cols |= OrderPdfColumns.Name;
        if (Variant) cols |= OrderPdfColumns.Variant;
        if (Quantity) cols |= OrderPdfColumns.Quantity;
        if (UnitPrice) cols |= OrderPdfColumns.UnitPrice;
        if (Value) cols |= OrderPdfColumns.Value;
        return new OrderPdfOptions(cols, Language, FontSize, Header);
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading) Changed?.Invoke();
    }
}
