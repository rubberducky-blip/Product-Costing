using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.ViewModels;
using ProductCosting.Core.Calc;
using ProductCosting.Core.Localization;
using ProductCosting.Core.Model;
using ProductCosting.Core.Pdf;

namespace ProductCosting.App.Views;

public sealed class OrderRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public OrderRow() => Qty.Changed += () =>
    {
        OnPropertyChanged(nameof(Warning));
        OnPropertyChanged(nameof(ValueText));
    };

    public long ItemId { get; init; }
    public string Code { get; init; } = "";
    public string? SupplierCode { get; init; }
    public string Name { get; init; } = "";
    public string? Variant { get; init; }
    public QtyUnit Unit { get; init; }
    public decimal? Moq { get; init; }
    public decimal? Pack { get; init; }
    /// <summary>Purchase price of one unit, in <see cref="Currency"/>.</summary>
    public decimal UnitPrice { get; init; }
    public Currency Currency { get; init; }
    public decimal DiscountPct { get; init; }
    public decimal ShippingPct { get; init; }
    public bool IsReadOnly { get; init; }
    public NumField Qty { get; } = new(null, allowEmpty: true);

    public string UnitText => Strings.Unit(Unit);
    public string MoqText => Moq is decimal m ? Fmt.Num(m) : "—";
    public string PackText => Pack is decimal p ? Fmt.Num(p) : "—";
    public string UnitPriceText => Fmt.InCurrency(UnitPrice, Currency);
    public string ValueText => Qty.Value is > 0 ? Fmt.InCurrency(UnitPrice * Qty.Value.Value, Currency) : "";
    internal string Key => $"{ItemId}|{Variant}";
    internal string Search => TextNorm.Join(Code, SupplierCode, Name, Variant);

    /// <summary>Why the quantity doesn't suit the supplier (below MOQ or not whole packs), or null.</summary>
    public string? Warning => Qty.Value is not > 0 ? null
        : Moq is decimal m && Qty.Value < m ? Loc.F("order.belowMoq", Fmt.Num(m))
        : Pack is decimal p && p > 0 && Qty.Value % p != 0 ? Loc.F("order.notPacks", Fmt.Num(p))
        : null;

    /// <summary>The nearest valid quantity at or above the typed one.</summary>
    public decimal? Fixed()
    {
        if (Qty.Value is not decimal q || q <= 0) return null;
        if (Moq is decimal m && q < m) q = m;
        if (Pack is decimal p && p > 0) q = Math.Ceiling(q / p) * p;
        return q;
    }

    public PurchaseOrderLine ToLine() => new()
    {
        ItemId = ItemId == 0 ? null : ItemId, Variant = Variant, Code = Code, SupplierCode = SupplierCode, Name = Name, Unit = Unit,
        Quantity = Qty.Value ?? 0, UnitPrice = UnitPrice, Currency = Currency, DiscountPct = DiscountPct, ShippingPct = ShippingPct,
    };
}

/// <summary>
/// Order form for a supplier. Drafts can be saved and edited again; "Execute" places the order (it becomes read-only)
/// and offers the PDF. Executed and archived orders open read-only.
/// </summary>
public partial class OrderWindow : Window
{
    // quantities of a new, not yet saved order stay while the app is open, so closing the form by mistake loses nothing
    static readonly Dictionary<long, Dictionary<string, string>> Drafts = [];

    readonly Supplier _supplier;
    readonly List<OrderRow> _rows = [];
    long? _orderId;
    OrderStatus _status = OrderStatus.Draft;
    decimal _vatPct;
    string _savedKey = "";

    /// <param name="orderId">A saved order to continue (draft) or look at (executed / archived); null starts a new order.</param>
    public OrderWindow(Supplier supplier, long? orderId = null)
    {
        InitializeComponent();
        NativeWindow.UseDarkTitleBar(this);
        _supplier = supplier;
        _orderId = orderId;
        _vatPct = supplier.VatPct;
        var saved = orderId is long id ? AppState.Store.GetOrder(id) : null;
        if (saved is not null)
        {
            _status = saved.Status;
            if (_status != OrderStatus.Draft) _vatPct = saved.VatPct;
        }
        var readOnly = _status != OrderStatus.Draft;
        var savedLines = orderId is long oid ? AppState.Store.GetOrderLines(oid) : [];

        if (!readOnly)
        {
            // every item of the supplier at today's prices
            var items = AppState.Store.GetSupplierItems(supplier.Id);
            var variants = AppState.Store.GetVariants(items.Select(i => i.ItemId));
            foreach (var i in items)
            {
                var unitPrice = i.PurchaseQty > 0 ? i.PurchasePrice / i.PurchaseQty : i.PurchasePrice;
                OrderRow Row(string? variant, string? code) => new()
                {
                    ItemId = i.ItemId, Code = i.Code, SupplierCode = code, Name = i.Name, Variant = variant, Unit = i.Unit, Moq = i.Moq, Pack = i.PackQty,
                    UnitPrice = unitPrice, Currency = i.Currency, DiscountPct = i.DiscountPct, ShippingPct = i.ShippingPct,
                };
                var vs = variants.TryGetValue(i.ItemId, out var all) ? all.Where(v => v.SupplierId is null || v.SupplierId == supplier.Id).ToList() : [];
                if (vs.Count > 0) foreach (var v in vs) _rows.Add(Row(v.Name, v.Code ?? i.SupplierCode));
                else _rows.Add(Row(null, i.SupplierCode));
            }
        }
        // saved quantities; executed / archived orders show exactly what was ordered, at the prices of that day
        foreach (var line in savedLines)
        {
            var row = readOnly ? null : _rows.FirstOrDefault(r => r.ItemId == (line.ItemId ?? -1) && r.Variant == line.Variant);
            if (row is null)
            {
                row = new OrderRow
                {
                    ItemId = line.ItemId ?? 0, Code = line.Code, SupplierCode = line.SupplierCode, Name = line.Name, Variant = line.Variant, Unit = line.Unit,
                    UnitPrice = line.UnitPrice, Currency = line.Currency, DiscountPct = line.DiscountPct, ShippingPct = line.ShippingPct, IsReadOnly = readOnly,
                };
                _rows.Add(row);
            }
            row.Qty.Set(line.Quantity);
        }
        _savedKey = orderId is null ? "" : Key();

        if (orderId is null)
        {
            var draft = Drafts.TryGetValue(supplier.Id, out var d) ? d : Drafts[supplier.Id] = [];
            foreach (var r in _rows)
                if (draft.TryGetValue(r.Key, out var q)) r.Qty.Text = q;
            foreach (var r in _rows)
                r.Qty.Changed += () => { if (_orderId is null) draft[r.Key] = r.Qty.Text; Update(); };
        }
        else
            foreach (var r in _rows) r.Qty.Changed += Update;

        // only offer "fix quantities" when some item has a minimum order or a pack size
        var hasRules = _rows.Any(r => r.Moq is not null || r.Pack is not null);
        FixButton.Visibility = hasRules && !readOnly ? Visibility.Visible : Visibility.Collapsed;
        MoqCol.Visibility = PackCol.Visibility = hasRules ? Visibility.Visible : Visibility.Collapsed;
        Grid.ItemsSource = _rows;
        Update();
    }

    List<OrderRow> Filled => _rows.Where(r => r.Qty.Value is > 0).ToList();

    /// <summary>The order's content as text: equal when nothing changed since it was saved.</summary>
    string Key() => string.Join(";", Filled.Select(r => r.Key + "=" + r.Qty.Value!.Value.ToString(CultureInfo.InvariantCulture)));

    void Update()
    {
        var readOnly = _status != OrderStatus.Draft;
        Title = TitleText.Text = Loc.F("order.title", _supplier.Name);
        StatusText.Text = Loc.T(_orderId is null ? "order.status.new" : "order.status." + _status);
        HelpText.Visibility = readOnly ? Visibility.Collapsed : Visibility.Visible;
        var filled = Filled;
        CountText.Text = Loc.F("order.lines", filled.Count);
        FixButton.IsEnabled = _rows.Any(r => r.Warning is not null);
        SaveButton.IsEnabled = !readOnly && filled.Count > 0 && Key() != _savedKey;
        SaveButton.Visibility = ExecuteButton.Visibility = ClearButton.Visibility = readOnly ? Visibility.Collapsed : Visibility.Visible;
        ExecuteButton.IsEnabled = filled.Count > 0;

        var t = Costing.Order(filled.Select(r => r.ToLine()), _vatPct, _supplier.Currency, AppState.Settings.UsdPerEur);
        string M(decimal v) => Fmt.InCurrency(v, _supplier.Currency);
        TotalsList.ItemsSource = new List<BreakdownLine>
        {
            new(Loc.T("order.goods"), M(t.Goods)),
            new(Loc.T("cost.discount"), "−" + M(t.Discount)),
            new(Loc.T("order.shippingEst"), "+" + M(t.Shipping)),
            new($"{Loc.T("order.vat")} {Fmt.Pct(t.VatPct)}", "+" + M(t.Vat)),
            new(Loc.T("order.total"), M(t.Total), "big"),
        };
    }

    void Fix_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows)
            if (r.Warning is not null && r.Fixed() is decimal q) r.Qty.Set(q);
    }

    void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = TextNorm.Normalize(FilterBox.Text).Trim();
        Grid.ItemsSource = q.Length == 0 ? _rows : _rows.Where(r => r.Search.Contains(q)).ToList();
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows) r.Qty.Text = "";
    }

    bool SaveDraft()
    {
        var lines = Filled.Select(r => r.ToLine()).ToList();
        if (lines.Count == 0)
        {
            Dialogs.Info(Loc.T("order.nothing"));
            return false;
        }
        var isNew = _orderId is null;
        _orderId = AppState.Store.SaveOrder(_orderId, _supplier.Id, lines);
        if (isNew) Drafts.Remove(_supplier.Id);
        _savedKey = Key();
        return true;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveDraft()) return;
        Update();
        MainViewModel.Instance.ShowToast(Loc.T("order.savedToast"));
    }

    void Execute_Click(object sender, RoutedEventArgs e)
    {
        if (!Dialogs.Confirm(Loc.T("order.executeConfirm"), Loc.T("order.execute"))) return;
        if ((_orderId is null || Key() != _savedKey) && !SaveDraft()) return;
        AppState.Store.SetOrderStatus(_orderId!.Value, OrderStatus.Executed);
        _status = OrderStatus.Executed;
        _vatPct = _supplier.VatPct;
        foreach (var r in _rows) r.Qty.Changed -= Update;
        Grid.IsEnabled = true;
        Update();
        if (Dialogs.Confirm(Loc.T("order.executedPdf"), Loc.T("order.export"))) Export_Click(sender, e);
        Close();
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var lines = Filled;
        if (lines.Count == 0)
        {
            Dialogs.Info(Loc.T("order.nothing"));
            return;
        }
        var dlg = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"{Loc.T("order.pdfTitle")} {Sanitize(_supplier.Name)} {DateTime.Now:yyyy-MM-dd}.pdf",
        };
        if (dlg.ShowDialog(this) != true) return;
        var pdf = PdfReports.SupplierOrder(_supplier,
            lines.Select(r => new OrderLine(r.Code, r.SupplierCode, r.Name, r.Variant, r.Qty.Value!.Value, r.Unit)).ToList(), DateTime.Now);
        File.WriteAllBytes(dlg.FileName, pdf);
        NotificationsViewModel.OpenFile(dlg.FileName);
    }

    /// <summary>Exports a saved order without opening it.</summary>
    public static void ExportSaved(Supplier supplier, PurchaseOrder order, Window? owner)
    {
        var lines = AppState.Store.GetOrderLines(order.Id);
        var date = DateTime.TryParse(order.ExecutedAt ?? order.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.Now;
        var dlg = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"{Loc.T("order.pdfTitle")} {Sanitize(supplier.Name)} {date:yyyy-MM-dd}.pdf",
        };
        if (dlg.ShowDialog(owner) != true) return;
        var pdf = PdfReports.SupplierOrder(supplier,
            lines.Select(l => new OrderLine(l.Code, l.SupplierCode, l.Name, l.Variant, l.Quantity, l.Unit)).ToList(), date);
        File.WriteAllBytes(dlg.FileName, pdf);
        NotificationsViewModel.OpenFile(dlg.FileName);
    }

    static string Sanitize(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
