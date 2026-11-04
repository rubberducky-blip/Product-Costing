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
    /// <summary>The unit the item is ordered in (m, m², kg, pieces…): quantity, MOQ, pack and unit price are all in it.</summary>
    public MeasureUnit Entry { get; init; }
    public decimal? Moq { get; init; }
    public decimal? Pack { get; init; }
    /// <summary>Purchase price of one unit, in <see cref="Currency"/>.</summary>
    public decimal UnitPrice { get; init; }
    public Currency Currency { get; init; }
    public decimal DiscountPct { get; init; }
    public decimal ShippingPct { get; init; }
    public bool IsReadOnly { get; init; }
    /// <summary>Other suppliers that sell this item for less (with their discount and shipping), for the hover text.</summary>
    public string? CheaperText { get; init; }
    public bool HasCheaper => CheaperText is not null;
    public NumField Qty { get; } = new(null, allowEmpty: true);

    public string UnitText => Strings.Measure(Entry);
    public string MoqText => Moq is decimal m ? Fmt.Num(m) : "—";
    public string PackText => Pack is decimal p ? Fmt.Num(p) : "—";
    public string UnitPriceText => Fmt.Exact(UnitPrice, Currency);
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
        ItemId = ItemId == 0 ? null : ItemId, Variant = Variant, Code = Code, SupplierCode = SupplierCode, Name = Name, Unit = Unit, EntryUnit = Entry,
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
    /// <param name="copyOf">A new order starts with the quantities of this saved order (at today's prices).</param>
    public OrderWindow(Supplier supplier, long? orderId = null, long? copyOf = null)
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
        var savedLines = (orderId ?? copyOf) is long oid ? AppState.Store.GetOrderLines(oid) : [];

        if (!readOnly)
        {
            // every item of the supplier at today's prices
            var items = AppState.Store.GetSupplierItems(supplier.Id);
            var variants = AppState.Store.GetVariants(items.Select(i => i.ItemId));
            var costs = AppState.Store.GetSupplierCosts(items.Select(i => i.ItemId));
            foreach (var i in items)
            {
                // ordered in the unit it is bought in (e.g. m²), at the price of one of it
                OrderRow Row(string? variant, string? code) => new()
                {
                    ItemId = i.ItemId, Code = i.Code, SupplierCode = code, Name = i.Name, Variant = variant, Unit = i.Unit, Entry = i.EntryUnit,
                    Moq = i.EntryMoq, Pack = i.EntryPack, UnitPrice = i.EntryUnitPrice, Currency = i.Currency, DiscountPct = i.DiscountPct, ShippingPct = i.ShippingPct,
                    CheaperText = Cheaper(costs.GetValueOrDefault(i.ItemId), supplier.Id, i.EntryUnit),
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
                    Entry = line.ShownUnit,
                    UnitPrice = line.UnitPrice, Currency = line.Currency, DiscountPct = line.DiscountPct, ShippingPct = line.ShippingPct, IsReadOnly = readOnly,
                };
                _rows.Add(row);
            }
            // a draft saved in another unit (or before units) keeps its amount: 15000 cm² becomes 1.5 m²
            row.Qty.Set(line.ShownUnit == row.Entry ? line.Quantity
                : line.ShownUnit.Dimension() == row.Entry.Dimension() ? row.Entry.FromBase(line.ShownUnit.ToBase(line.Quantity)) : line.Quantity);
        }
        _savedKey = orderId is null ? "" : Key();

        // a copy is a new order of its own: it neither loads nor overwrites the unsaved quantities kept for the supplier
        if (orderId is null && copyOf is null)
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

    /// <summary>"Cheaper from: …" when another supplier's cost per unit (discount and shipping included) is lower, else null.</summary>
    static string? Cheaper(List<SupplierCost>? costs, long supplierId, MeasureUnit unit)
    {
        if (costs is null || costs.FirstOrDefault(c => c.SupplierId == supplierId) is not { } own) return null;
        var cheaper = costs.Where(c => c.SupplierId != supplierId && c.UnitCostEur < own.UnitCostEur - 0.000001m)
            .OrderBy(c => c.UnitCostEur).ToList();
        if (cheaper.Count == 0) return null;
        string Per(SupplierCost c) => $"{Fmt.Money4(c.UnitCostEur * unit.Factor())} / {Strings.Measure(unit)}";
        return Loc.T("order.cheaper") + "\n"
            + string.Join("\n", cheaper.Select(c => $"• {c.SupplierName}: {Per(c)}"))
            + "\n" + Loc.F("order.cheaperOwn", Per(own));
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
            new($"{Loc.T("order.vat")} {Fmt.Pct(t.VatPct)}", "+" + M(t.Vat)),
            new(Loc.T("order.total"), M(t.Total), "big"),
            // shipping is only an estimate: shown after the total, in small print
            new(Loc.T("order.shippingEst"), "+" + M(t.Shipping), "sub"),
            new(Loc.T("order.withShipping"), M(t.TotalWithShipping), "sub"),
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
        var pdf = Pdf(_supplier,
            lines.Select(r => new OrderLine(r.Code, r.SupplierCode, r.Name, r.Variant, r.Qty.Value!.Value, r.Entry, r.UnitPrice, r.Currency)).ToList(), DateTime.Now);
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
        var pdf = Pdf(supplier,
            lines.Select(l => new OrderLine(l.Code, l.SupplierCode, l.Name, l.Variant, l.Quantity, l.ShownUnit, l.UnitPrice, l.Currency)).ToList(), date);
        File.WriteAllBytes(dlg.FileName, pdf);
        NotificationsViewModel.OpenFile(dlg.FileName);
    }

    /// <summary>The order PDF with the supplier's own options, or those of Settings; our details and logo when they are shown.</summary>
    static byte[] Pdf(Supplier supplier, List<OrderLine> lines, DateTime date)
    {
        var options = OrderPdfOptions.Parse(supplier.OrderPdf) ?? AppState.Settings.OrderPdf;
        var ours = options.Header is OrderPdfHeader.Company or OrderPdfHeader.Both;
        return PdfReports.SupplierOrder(supplier, lines, date, options, AppState.Settings.Company, ours ? AppState.Store.GetLogo() : null);
    }

    static string Sanitize(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
