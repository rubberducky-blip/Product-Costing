using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.Views;
using ProductCosting.Core.Data;
using ProductCosting.Core.Model;

namespace ProductCosting.App.ViewModels;

public sealed record Crumb(long? Id, string Name, bool IsLast);

/// <summary>One of the four lists: folder path, folder tiles, a filter box and a virtualized table.</summary>
public sealed partial class ListPageViewModel : PageViewModel
{
    readonly MainViewModel _main;
    Dictionary<long, Folder> _folders = [];

    public ListScope Scope { get; }
    public bool IsSuppliers => Scope == ListScope.Suppliers;
    public bool IsProducts => Scope == ListScope.Products;
    public bool IsMerchandise => Scope == ListScope.Merchandise;
    public bool IsRaw => Scope == ListScope.RawMaterials;
    public bool IsBought => IsMerchandise || IsRaw;
    public bool IsStale { get; set; } = true;

    public override string Section => Scope switch
    {
        ListScope.Merchandise => "merchandise",
        ListScope.RawMaterials => "raw",
        ListScope.Suppliers => "suppliers",
        _ => "products",
    };

    public string Title => Loc.T(Scope switch
    {
        ListScope.Merchandise => "nav.merchandise",
        ListScope.RawMaterials => "nav.raw",
        ListScope.Suppliers => "nav.suppliers",
        _ => "nav.products",
    });

    public string NewText => Loc.T(Scope switch
    {
        ListScope.Merchandise => "list.newMerchandise",
        ListScope.RawMaterials => "list.newRaw",
        ListScope.Suppliers => "list.newSupplier",
        _ => "list.newProduct",
    });

    public string CodeSortText => Loc.T(IsSuppliers ? "sort.name" : "sort.code");

    public ObservableCollection<FolderTile> Tiles { get; } = [];
    public ObservableCollection<Crumb> Crumbs { get; } = [];

    [ObservableProperty] private IList _rows = new List<object>();
    [ObservableProperty] private long? _folderId;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private SortMode _sortMode;
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _hasRows;

    public bool IsFiltering => !string.IsNullOrWhiteSpace(Filter);
    public bool InFolder => FolderId is not null && !IsFiltering;
    public bool CanReorder => SortMode == SortMode.Manual && !IsFiltering;
    public bool ShowTiles => !IsFiltering && Tiles.Count > 0;

    /// <summary>Raised before the rows are replaced so the view can keep its scroll position.</summary>
    public event Action? RowsReplacing;

    public ListPageViewModel(MainViewModel main, ListScope scope)
    {
        _main = main;
        Scope = scope;
    }

    public override void OnShown()
    {
        if (IsStale) Refresh();
    }

    public override void OnDataChanged(DataChange change) => Refresh();

    partial void OnFilterChanged(string value)
    {
        Refresh();
        OnPropertyChanged(nameof(IsFiltering));
    }

    partial void OnSortModeChanged(SortMode value) => OnPropertyChanged(nameof(CanReorder));

    public void Refresh()
    {
        IsStale = false;
        var store = AppState.Store;
        _folders = store.GetFolders(Scope).ToDictionary(f => f.Id);
        if (FolderId is long id && !_folders.ContainsKey(id)) FolderId = null;

        RowsReplacing?.Invoke();
        if (IsFiltering)
        {
            Tiles.Clear();
            if (IsSuppliers)
            {
                var rows = store.FilterSupplierRows(Filter);
                foreach (var r in rows) r.FolderPath = PathOf(r.FolderId);
                Rows = rows;
            }
            else
            {
                var rows = store.FilterItemRows(Scope, Filter);
                foreach (var r in rows) r.FolderPath = PathOf(r.FolderId);
                Rows = rows;
            }
        }
        else
        {
            Tiles.Clear();
            foreach (var t in store.GetFolderTiles(Scope, FolderId)) Tiles.Add(t);
            Rows = IsSuppliers ? store.GetSupplierRows(FolderId) : store.GetItemRows(Scope, FolderId);
        }
        _sortMode = store.GetSortMode(Scope, FolderId);
        OnPropertyChanged(nameof(SortMode));

        Crumbs.Clear();
        var chain = new List<Folder>();
        for (var f = FolderId is long fid ? _folders.GetValueOrDefault(fid) : null; f is not null; f = f.ParentId is long p ? _folders.GetValueOrDefault(p) : null)
            chain.Insert(0, f);
        Crumbs.Add(new Crumb(null, Title, chain.Count == 0));
        for (var i = 0; i < chain.Count; i++) Crumbs.Add(new Crumb(chain[i].Id, chain[i].Name, i == chain.Count - 1));

        CountText = Loc.F("list.count", Rows.Count);
        HasRows = Rows.Count > 0;
        IsEmpty = Rows.Count == 0 && (IsFiltering || Tiles.Count == 0);
        OnPropertyChanged(nameof(InFolder));
        OnPropertyChanged(nameof(CanReorder));
        OnPropertyChanged(nameof(ShowTiles));
        OnPropertyChanged(nameof(CurrentFolderName));
    }

    public string CurrentFolderName => FolderId is long id && _folders.TryGetValue(id, out var f) ? f.Name : "";

    string? PathOf(long? folderId)
    {
        var parts = new List<string>();
        for (var f = folderId is long id ? _folders.GetValueOrDefault(id) : null; f is not null; f = f.ParentId is long p ? _folders.GetValueOrDefault(p) : null)
            parts.Insert(0, f.Name);
        return parts.Count == 0 ? null : string.Join(" › ", parts);
    }

    [RelayCommand]
    public void OpenFolder(long? id)
    {
        Filter = "";
        FolderId = id;
        Refresh();
    }

    [RelayCommand]
    void SetSort(SortMode mode)
    {
        if (mode == SortMode) return;
        AppState.Store.SetSortMode(Scope, FolderId, mode);
    }

    [RelayCommand]
    void New()
    {
        if (IsSuppliers) _main.NewSupplier(FolderId);
        else _main.NewItem((ItemKind)(int)Scope, FolderId);
    }

    [RelayCommand]
    void NewFolder()
    {
        var name = Dialogs.Prompt(Loc.T("folder.new"));
        if (name is not null) AppState.Store.CreateFolder(Scope, FolderId, name);
    }

    [RelayCommand]
    void RenameFolder(long? id)
    {
        id ??= FolderId;
        if (id is not long fid || !_folders.TryGetValue(fid, out var f)) return;
        var name = Dialogs.Prompt(Loc.T("folder.rename"), f.Name);
        if (name is not null) AppState.Store.RenameFolder(fid, name);
    }

    [RelayCommand]
    void DeleteFolder(long? id)
    {
        id ??= FolderId;
        if (id is not long fid || !_folders.TryGetValue(fid, out var f)) return;
        if (!Dialogs.Confirm(Loc.F("folder.confirmDelete", f.Name), Loc.T("common.delete"), danger: true)) return;
        if (FolderId == fid) FolderId = f.ParentId;
        AppState.Store.DeleteFolder(fid);
    }

    /// <param name="order">Ids of the rows as shown (after any column sort), for the ← / → keys in the form.</param>
    public void OpenRow(object? row, IReadOnlyList<long>? order = null)
    {
        order ??= Rows.Cast<object>().Select(IdOf).ToList();
        switch (row)
        {
            case ItemRow r: _main.OpenItem(r.Id, Scope, order); break;
            case SupplierRow s: _main.OpenSupplier(s.Id, order); break;
        }
    }

    public void DeleteRows(IReadOnlyList<object> rows)
    {
        if (rows.Count == 0) return;
        if (IsSuppliers)
        {
            var sups = rows.OfType<SupplierRow>().ToList();
            var items = sups.Sum(s => s.ItemCount);
            var name = sups.Count == 1 ? sups[0].Name : $"{sups.Count}";
            if (!Dialogs.ConfirmDelete(name, items > 0 ? Loc.F("supplier.deleteUsed", items) : null)) return;
            foreach (var s in sups) AppState.Store.DeleteSupplier(s.Id);
        }
        else
        {
            var its = rows.OfType<ItemRow>().ToList();
            var used = its.Sum(i => AppState.Store.CountUsage(i.Id));
            var name = its.Count == 1 ? $"{its[0].Code} · {its[0].Name}" : $"{its.Count}";
            if (!Dialogs.ConfirmDelete(name, used > 0 ? Loc.F("item.deleteUsed", used) : null)) return;
            foreach (var i in its) AppState.Store.DeleteItem(i.Id);
        }
    }

    /// <summary>Asks for a selling price and sets the markup to reach it (the price is rounded to the item's step first).</summary>
    public void SetPrice(ItemRow row)
    {
        var text = Dialogs.Prompt(Loc.F("menu.setPriceTitle", row.Code, row.Name), Fmt.Num(row.Price));
        if (text is null) return;
        var field = new NumField(null);
        field.Text = text;
        if (field.Value is not decimal price || price <= 0) { Dialogs.Error(Loc.T("common.invalidNumber")); return; }
        if (AppState.Store.SetPrice(row.Id, price) is decimal applied) _main.ShowToast(Loc.F("menu.priceSet", row.Code, Fmt.Money(applied)));
        else Dialogs.Error(Loc.T("menu.noCost"));
    }

    /// <summary>Quick change of the purchase price (items with a single supplier).</summary>
    public void SetPurchasePrice(ItemRow row)
    {
        var text = Dialogs.Prompt(Loc.F("menu.setPurchaseTitle", row.Code, row.Name), Fmt.Num(row.PurchasePrice));
        if (text is null) return;
        var field = new NumField(null);
        field.Text = text;
        if (field.Value is not decimal price) { Dialogs.Error(Loc.T("common.invalidNumber")); return; }
        if (AppState.Store.SetPurchasePrice(row.Id, price)) _main.ShowToast(Loc.F("menu.purchaseSet", row.Code, Fmt.InCurrency(price, row.PurchaseCurrency)));
    }

    public void DuplicateRows(IReadOnlyList<object> rows)
    {
        string? last = null;
        foreach (var r in rows)
        {
            switch (r)
            {
                case ItemRow i: last = AppState.Store.GetItem(AppState.Store.DuplicateItem(i.Id))?.Code; break;
                case SupplierRow s: last = AppState.Store.GetSupplier(AppState.Store.DuplicateSupplier(s.Id))?.Name; break;
            }
        }
        if (last is not null) _main.ShowToast(Loc.F("common.duplicated", last));
    }

    public static long IdOf(object row) => row switch
    {
        ItemRow r => r.Id,
        SupplierRow s => s.Id,
        _ => 0,
    };

    // ---------- drag and drop ----------

    public void MoveRows(IReadOnlyList<long> ids, long? folderId)
    {
        if (ids.Count == 0 || (folderId == FolderId && !IsFiltering)) return;
        AppState.Store.MoveItems(Scope, ids, folderId);
    }

    public void MoveFolder(long id, long? parentId)
    {
        if (id == parentId) return;
        if (_folders.TryGetValue(id, out var f) && f.ParentId == parentId) return;
        if (!AppState.Store.MoveFolder(id, parentId)) Dialogs.Info(Loc.T("folder.cantMove"));
    }

    /// <summary>Manual sort: puts the dragged rows before/after the target row.</summary>
    public void ReorderRows(IReadOnlyList<long> dragged, long targetId, bool after)
    {
        if (!CanReorder || dragged.Contains(targetId)) return;
        var order = Rows.Cast<object>().Select(IdOf).Where(id => !dragged.Contains(id)).ToList();
        var at = order.IndexOf(targetId);
        if (at < 0) return;
        order.InsertRange(after ? at + 1 : at, dragged.Where(d => Rows.Cast<object>().Any(r => IdOf(r) == d)));
        AppState.Store.ReorderRows(Scope, FolderId, order);
    }

    public void ReorderFolders(long dragged, long targetId, bool after)
    {
        if (!CanReorder || dragged == targetId) return;
        var order = Tiles.Select(t => t.Id).Where(id => id != dragged).ToList();
        var at = order.IndexOf(targetId);
        if (at < 0) return;
        order.Insert(after ? at + 1 : at, dragged);
        AppState.Store.ReorderFolders(order);
    }

    public void MarkSeen(ItemRow row)
    {
        if (!row.HasUnseenChange) return;
        row.HasUnseenChange = false;
        AppState.Store.MarkSeen(row.Id);
    }
}
