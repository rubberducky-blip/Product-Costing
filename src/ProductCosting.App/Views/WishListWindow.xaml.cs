using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using ProductCosting.App.Controls;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.ViewModels;
using ProductCosting.Core.Calc;
using ProductCosting.Core.Data;
using ProductCosting.Core.Localization;
using ProductCosting.Core.Model;

namespace ProductCosting.App.Views;

/// <summary>An item on the shopping list: ticked for the next orders, its quantity saved as it is typed.</summary>
public sealed partial class WishRow : ObservableObject
{
    public WishItem Model { get; }
    public string Code => Model.Code;
    public string Name => Model.Name;
    public string? Suppliers => Model.Suppliers;
    public string UnitText => Strings.Measure(Model.ShownUnit);
    public NumField Qty { get; }
    [ObservableProperty] private bool _selected = true;

    /// <summary>The open orders (draft or executed) the item is already on, for the mark beside its quantity.</summary>
    public List<string>? Mentions { get; init; }
    public bool HasMentions => Mentions is { Count: > 0 };
    public string MentionsText => Mentions is null ? "" : Loc.T("order.alreadyOn") + "\n" + string.Join("\n", Mentions.Select(m => "• " + m));

    public WishRow(WishItem w)
    {
        Model = w;
        Qty = new NumField(w.ShownUnit.FromBase(w.Quantity));
        Qty.Changed += () =>
        {
            if (Qty.Value is decimal q && q > 0) AppState.Store.SetWish(Model.ItemId, Model.ShownUnit.ToBase(q));
        };
    }
}

/// <summary>
/// Orders page → shopping list: items wanted, kept until taken off. The ticked ones become draft orders — each item
/// from the cheapest supplier, or from as few suppliers as possible — optionally only from Greek or European suppliers.
/// </summary>
public partial class WishListWindow : Window
{
    List<WishRow> _rows = [];
    Dictionary<long, (QtyUnit Unit, MeasureUnit? PriceUnit)> _units = [];

    public WishListWindow()
    {
        InitializeComponent();
        NativeWindow.UseDarkTitleBar(this);
        Title = Loc.T("wish.title");
        RegionBox.ItemsSource = new List<Choice<SupplierRegion>>
        {
            new(SupplierRegion.Any, Loc.T("wish.region.Any")),
            new(SupplierRegion.Greece, Loc.T("wish.region.Greece")),
            new(SupplierRegion.Europe, Loc.T("wish.region.Europe")),
        };
        RegionBox.SelectedValue = SupplierRegion.Any;
        AddQty.Field = new NumField(null, allowEmpty: true);
        var items = AppState.Store.GetMaterials().Select(m => m.Info).Where(m => m.Kind != ItemKind.Product).ToList();
        _units = items.ToDictionary(m => m.Id, m => (m.Unit, m.PriceUnit));
        ItemPicker.ItemsSource = PickerEntry.Flat(items.Select(m => (m.Id, $"{m.Code} · {m.Name}", m.Code)));
        Reload();
    }

    void Reload()
    {
        var mentions = AppState.Store.GetOpenOrderMentions(null);
        _rows = AppState.Store.GetWishList().Select(w => new WishRow(w) { Mentions = mentions.GetValueOrDefault(w.ItemId) }).ToList();
        Table.ItemsSource = _rows;
        Table.Visibility = _rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = _rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        CreateButton.IsEnabled = _rows.Count > 0;
        AllBox.IsChecked = true;
    }

    MeasureUnit UnitOf(long id) => _units.TryGetValue(id, out var u) ? u.Unit.EntryUnit(u.PriceUnit) : MeasureUnit.Pcs;

    void ItemPicker_Picked(object? sender, EventArgs e)
    {
        if (ItemPicker.SelectedId is not long id) return;
        AddUnit.Text = Strings.Measure(UnitOf(id));
        AddQty.Focus();
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        if (ItemPicker.SelectedId is not long id) return;
        var field = AddQty.Field;
        var qty = field?.Value is decimal q && q > 0 ? q : 1;
        // an item already on the list gets the new quantity added
        var had = _rows.FirstOrDefault(r => r.Model.ItemId == id)?.Model.Quantity ?? 0;
        AppState.Store.SetWish(id, had + UnitOf(id).ToBase(qty));
        ItemPicker.SelectedId = null;
        AddUnit.Text = "";
        if (field is not null) field.Text = "";
        Reload();
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WishRow row })
        {
            AppState.Store.RemoveWish([row.Model.ItemId]);
            Reload();
        }
    }

    void All_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows) r.Selected = AllBox.IsChecked == true;
    }

    void Create_Click(object sender, RoutedEventArgs e)
    {
        var ids = _rows.Where(r => r.Selected).Select(r => r.Model.ItemId).ToList();
        if (ids.Count == 0)
        {
            Dialogs.Info(Loc.T("wish.noneTicked"));
            return;
        }
        var mode = FewestOption.IsChecked == true ? PlanMode.FewestOrders : PlanMode.Cheapest;
        var region = RegionBox.SelectedValue is SupplierRegion r ? r : SupplierRegion.Any;
        var result = AppState.Store.OrderWishes(ids, mode, region);
        var text = Loc.F("wish.done", result.Items, result.Created, result.Extended);
        if (result.Unplaced.Count > 0)
        {
            var codes = _rows.Where(x => result.Unplaced.Contains(x.Model.ItemId)).Select(x => x.Code);
            text += "\n\n" + Loc.F("wish.unplaced", string.Join(", ", codes));
        }
        Dialogs.Info(text, Loc.T("wish.title"));
        Reload();
    }
}
