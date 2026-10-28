using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProductCosting.App.Controls;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.Views;
using ProductCosting.Core.Data;
using ProductCosting.Core.Model;

namespace ProductCosting.App.ViewModels;

public sealed partial class SupplierEditorViewModel : PageViewModel
{
    readonly MainViewModel _main;
    Supplier _model;
    string _original = "";
    bool _loading;

    public SupplierEditorViewModel(MainViewModel main, Supplier s)
    {
        _main = main;
        _model = s;
        Discount = new NumField(0);
        Shipping = new NumField(0);
        Discount.Changed += Edited;
        Shipping.Changed += Edited;
        FolderEntries = PickerEntry.Folders(AppState.Store.GetFolders(ListScope.Suppliers), Loc.T("folder.root"));
        Load(s);
    }

    public override string Section => "suppliers";

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private long? _folderId;
    [ObservableProperty] private string _country = "";
    [ObservableProperty] private Currency _currency;
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private bool _dirty;

    public NumField Discount { get; }
    public NumField Shipping { get; }
    public List<PickerEntry> FolderEntries { get; }
    public List<Choice<Currency>> Currencies { get; } = Choices.Currencies();
    public ObservableCollection<SupplierItemRow> Items { get; } = [];

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
        Country = s.Country ?? "";
        Currency = s.Currency;
        Email = s.Email ?? "";
        Phone = s.Phone ?? "";
        Notes = s.Notes ?? "";
        Discount.Set(s.DiscountPct);
        Shipping.Set(s.ShippingPct);
        Items.Clear();
        if (!IsNew) foreach (var i in AppState.Store.GetSupplierItems(s.Id)) Items.Add(i);
        _loading = false;
        _original = Snapshot();
        Dirty = false;
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Title));
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Name) or nameof(FolderId) or nameof(Country) or nameof(Currency) or nameof(Email) or nameof(Phone) or nameof(Notes))
            Edited();
    }

    void Edited()
    {
        if (_loading) return;
        Dirty = Snapshot() != _original;
        OnPropertyChanged(nameof(Title));
    }

    string Snapshot()
    {
        string N(NumField f) => f.IsValid ? f.Value?.ToString(CultureInfo.InvariantCulture) ?? "" : "!" + f.Text;
        return string.Join("|", Name.Trim(), FolderId, Country.Trim(), Currency, Email.Trim(), Phone.Trim(), Notes.Trim(), N(Discount), N(Shipping));
    }

    [RelayCommand]
    void Back() => _main.GoToList(ListScope.Suppliers);

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
            Id = _model.Id, Name = Name.Trim(), FolderId = FolderId, Country = Opt(Country), Currency = Currency,
            Email = Opt(Email), Phone = Opt(Phone), Notes = Opt(Notes), DiscountPct = Discount.Or0, ShippingPct = Shipping.Or0,
        };
        var id = AppState.Store.SaveSupplier(s);
        Load(AppState.Store.GetSupplier(id)!);
        _main.ShowToast(Loc.T("common.save") + " ✓");
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
    void OpenItem(SupplierItemRow row) => _main.OpenItem(row.ItemId);

    [RelayCommand]
    void NewOrder()
    {
        if (IsNew) return;
        new OrderWindow(_model) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
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
        }
    }
}
