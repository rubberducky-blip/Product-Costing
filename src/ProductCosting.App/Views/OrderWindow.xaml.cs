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
    public OrderRow() => Qty.Changed += () => OnPropertyChanged(nameof(Warning));

    public long ItemId { get; init; }
    public string Code { get; init; } = "";
    public string? SupplierCode { get; init; }
    public string Name { get; init; } = "";
    public string? Variant { get; init; }
    public QtyUnit Unit { get; init; }
    public decimal? Moq { get; init; }
    public decimal? Pack { get; init; }
    public string UnitText => Strings.Unit(Unit);
    public string MoqText => Moq is decimal m ? Fmt.Num(m) : "—";
    public string PackText => Pack is decimal p ? Fmt.Num(p) : "—";

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
    public NumField Qty { get; } = new(null, allowEmpty: true);
    internal string Key => $"{ItemId}|{Variant}";
    internal string Search => TextNorm.Join(Code, SupplierCode, Name, Variant);
}

/// <summary>Order form for a supplier: one line per item (or per variation), exported to PDF.</summary>
public partial class OrderWindow : Window
{
    // quantities stay while the app is open, so closing the form by mistake loses nothing
    static readonly Dictionary<long, Dictionary<string, string>> Drafts = [];

    readonly Supplier _supplier;
    readonly List<OrderRow> _rows = [];

    public OrderWindow(Supplier supplier)
    {
        InitializeComponent();
        NativeWindow.UseDarkTitleBar(this);
        _supplier = supplier;
        Title = TitleText.Text = Loc.F("order.title", supplier.Name);

        var items = AppState.Store.GetSupplierItems(supplier.Id);
        var variants = AppState.Store.GetVariants(items.Select(i => i.ItemId));
        var draft = Drafts.TryGetValue(supplier.Id, out var d) ? d : Drafts[supplier.Id] = [];
        foreach (var i in items)
        {
            if (variants.TryGetValue(i.ItemId, out var vs) && vs.Count > 0)
                foreach (var v in vs)
                    _rows.Add(new OrderRow { ItemId = i.ItemId, Code = i.Code, SupplierCode = v.Code ?? i.SupplierCode, Name = i.Name, Variant = v.Name, Unit = i.Unit, Moq = i.Moq, Pack = i.PackQty });
            else
                _rows.Add(new OrderRow { ItemId = i.ItemId, Code = i.Code, SupplierCode = i.SupplierCode, Name = i.Name, Unit = i.Unit, Moq = i.Moq, Pack = i.PackQty });
        }
        foreach (var r in _rows)
        {
            if (draft.TryGetValue(r.Key, out var q)) r.Qty.Text = q;
            r.Qty.Changed += () => { draft[r.Key] = r.Qty.Text; UpdateCount(); };
        }
        Grid.ItemsSource = _rows;
        UpdateCount();
    }

    List<OrderRow> Filled => _rows.Where(r => r.Qty.Value is > 0).ToList();

    void UpdateCount()
    {
        CountText.Text = Loc.F("order.lines", Filled.Count);
        FixButton.IsEnabled = _rows.Any(r => r.Warning is not null);
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

    static string Sanitize(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
