using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProductCosting.App.Controls;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.Views;
using ProductCosting.Core.Data;
using ProductCosting.Core.Localization;
using ProductCosting.Core.Model;

namespace ProductCosting.App.ViewModels;

public sealed class SavedOrderRow(PurchaseOrder o)
{
    public PurchaseOrder Model { get; } = o;
    public string DateText => DateTime.TryParse(Model.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("g", Loc.Culture) : Model.UpdatedAt;
    public int LineCount => Model.LineCount;
    public string TotalText => Fmt.Num(Model.TotalQuantity);
}

public sealed partial class SupplierEditorViewModel : PageViewModel
{
    readonly MainViewModel _main;
    Supplier _model;
    string _original = "";
    bool _loading;

    public SupplierEditorViewModel(MainViewModel main, Supplier s, IReadOnlyList<long>? trail = null)
    {
        Trail = trail;
        _main = main;
        _model = s;
        // empty = 0, shown as a "0" placeholder
        Discount = new NumField(null, allowEmpty: true);
        Shipping = new NumField(null, allowEmpty: true);
        Discount.Changed += Edited;
        Shipping.Changed += Edited;
        FolderEntries = PickerEntry.Folders(AppState.Store.GetFolders(ListScope.Suppliers), Loc.T("folder.root"));
        Load(s);
    }

    public override string Section => "suppliers";
    public IReadOnlyList<long>? Trail { get; }
    public long Id => _model.Id;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private long? _folderId;
    [ObservableProperty] private long? _countryId;
    [ObservableProperty] private Currency _currency;
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private bool _dirty;

    public NumField Discount { get; }
    public NumField Shipping { get; }
    public List<PickerEntry> FolderEntries { get; }
    public List<PickerEntry> CountryEntries { get; private set; } = [];
    string? _otherCountry;
    public List<Choice<Currency>> Currencies { get; } = Choices.Currencies();
    public ObservableCollection<SupplierItemRow> Items { get; } = [];
    public ObservableCollection<SavedOrderRow> Orders { get; } = [];
    public bool HasOrders => Orders.Count > 0;

    public bool IsNew => _model.Id == 0;
    public bool HasItems => Items.Count > 0;
    public string Title => IsNew && string.IsNullOrWhiteSpace(Name) ? Loc.T("list.newSupplier") : Name;
    public override bool IsDirty => Dirty;

    void Load(Supplier s)
    {
        _loading = true;
        _model = s;
        Name = s.Name;
        FolderId = s.FolderId;
        // countries are stored by ISO code; anything else (older free text) is kept as an extra choice
        var code = string.IsNullOrWhiteSpace(s.Country) ? (s.Id == 0 ? Countries.Default : null) : s.Country.Trim();
        var all = Countries.All;
        _otherCountry = code is not null && Countries.Find(code) is null ? code : null;
        CountryEntries = PickerEntry.Flat(all.Select((c, i) => ((long)i, c.Name, c.El + " " + c.En + " " + c.Code))
            .Concat(_otherCountry is null ? [] : [((long)all.Count, _otherCountry, _otherCountry)]));
        OnPropertyChanged(nameof(CountryEntries));
        CountryId = code is null ? null
            : Countries.Find(code) is { } found ? all.ToList().FindIndex(c => c.Code == found.Code) : all.Count;
        Currency = s.Currency;
        Email = s.Email ?? "";
        Phone = s.Phone ?? "";
        Notes = s.Notes ?? "";
        Discount.Set(s.DiscountPct == 0 ? null : s.DiscountPct);
        Shipping.Set(s.ShippingPct == 0 ? null : s.ShippingPct);
        Items.Clear();
        if (!IsNew) foreach (var i in AppState.Store.GetSupplierItems(s.Id)) Items.Add(i);
        LoadOrders();
        _loading = false;
        _original = Snapshot();
        Dirty = false;
        ResetUndo();
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Title));
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Name) or nameof(FolderId) or nameof(CountryId) or nameof(Currency) or nameof(Email) or nameof(Phone) or nameof(Notes))
            Edited();
    }

    void Edited()
    {
        if (_loading) return;
        Dirty = Snapshot() != _original;
        OnPropertyChanged(nameof(Title));
        NoteEdit();
    }

    sealed record FormState(string Name, long? FolderId, long? CountryId, Currency Currency, string Email, string Phone, string Notes,
        string Discount, string Shipping);

    protected override object? CaptureState() =>
        new FormState(Name, FolderId, CountryId, Currency, Email, Phone, Notes, Discount.Text, Shipping.Text);

    protected override void RestoreState(object state)
    {
        var f = (FormState)state;
        _loading = true;
        Name = f.Name; FolderId = f.FolderId; CountryId = f.CountryId; Currency = f.Currency;
        Email = f.Email; Phone = f.Phone; Notes = f.Notes; Discount.Text = f.Discount; Shipping.Text = f.Shipping;
        _loading = false;
        Edited();
    }

    protected override string StateKey() => Snapshot();

    string? CountryCode => CountryId is long i
        ? i < Countries.All.Count ? Countries.All[(int)i].Code : _otherCountry
        : null;

    string Snapshot()
    {
        string N(NumField f) => f.IsValid ? (f.Value ?? 0).ToString(CultureInfo.InvariantCulture) : "!" + f.Text;
        return string.Join("|", Name.Trim(), FolderId, CountryCode, Currency, Email.Trim(), Phone.Trim(), Notes.Trim(), N(Discount), N(Shipping));
    }

    [RelayCommand]
    void Back() => _main.GoBack(ListScope.Suppliers);

    public override bool IsGone => !IsNew && AppState.Store.GetSupplier(_model.Id) is null;

    public override void OnShown() => OnDataChanged(DataChange.All);

    [RelayCommand]
    void Save() => TrySave();

    [RelayCommand]
    void Cancel() => Load(_model);

    public override void Discard() => Load(_model);

    public override bool TrySave()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Dialogs.Error(Loc.T("common.required"));
            return false;
        }
        if (!Discount.IsValid || !Shipping.IsValid)
        {
            Dialogs.Error(Loc.T("common.invalidNumber"));
            return false;
        }
        static string? Opt(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var s = new Supplier
        {
            Id = _model.Id, Name = Name.Trim(), FolderId = FolderId, Country = CountryCode, Currency = Currency,
            Email = Opt(Email), Phone = Opt(Phone), Notes = Opt(Notes), DiscountPct = Discount.Or0, ShippingPct = Shipping.Or0,
        };
        var id = AppState.Store.SaveSupplier(s);
        Load(AppState.Store.GetSupplier(id)!);
        _main.ShowToast(Loc.T("common.saved"));
        return true;
    }

    [RelayCommand]
    void Delete()
    {
        if (IsNew) { Discard(); Back(); return; }
        if (!Dialogs.ConfirmDelete(_model.Name, Items.Count > 0 ? Loc.F("supplier.deleteUsed", Items.Count) : null)) return;
        AppState.Store.DeleteSupplier(_model.Id);
        Dirty = false;
        Back();
    }

    [RelayCommand]
    void OpenItem(SupplierItemRow row) => _main.OpenItem(row.ItemId, trail: Items.Select(i => i.ItemId).ToList());

    void LoadOrders()
    {
        Orders.Clear();
        if (!IsNew) foreach (var o in AppState.Store.GetOrders(_model.Id)) Orders.Add(new SavedOrderRow(o));
        OnPropertyChanged(nameof(HasOrders));
    }

    public void OpenOrder(SavedOrderRow row)
    {
        new OrderWindow(_model, row.Model.Id) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
        LoadOrders();
    }

    public void ExportOrder(SavedOrderRow row) => OrderWindow.ExportSaved(_model, row.Model, System.Windows.Application.Current.MainWindow);

    public void DeleteOrder(SavedOrderRow row)
    {
        if (!Dialogs.Confirm(Loc.F("order.confirmDelete", row.DateText), Loc.T("common.delete"), danger: true)) return;
        AppState.Store.DeleteOrder(row.Model.Id);
        LoadOrders();
    }

    [RelayCommand]
    void NewOrder()
    {
        if (IsNew) return;
        new OrderWindow(_model) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
        LoadOrders();
    }

    public override void OnDataChanged(DataChange change)
    {
        if (IsNew) return;
        if (!Dirty && AppState.Store.GetSupplier(_model.Id) is { } fresh) Load(fresh);
        else
        {
            Items.Clear();
            foreach (var i in AppState.Store.GetSupplierItems(_model.Id)) Items.Add(i);
            OnPropertyChanged(nameof(HasItems));
            LoadOrders();
        }
    }
}
