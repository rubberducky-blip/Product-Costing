using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProductCosting.App.Controls;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.Views;
using ProductCosting.Core.Calc;
using ProductCosting.Core.Data;
using ProductCosting.Core.Localization;
using ProductCosting.Core.Model;

namespace ProductCosting.App.ViewModels;

/// <summary>Form for a product, merchandise or raw material with a live cost breakdown.</summary>
public sealed partial class ItemEditorViewModel : PageViewModel
{
    readonly MainViewModel _main;
    readonly ListScope _from;
    ItemDocument _doc;
    string _original = "";
    bool _loading;
    Dictionary<long, Supplier> _suppliers = [];
    Dictionary<long, MaterialInfo> _materials = [];

    /// <param name="trail">Ids of the table the item was opened from, in its order (for the ← / → keys).</param>
    public ItemEditorViewModel(MainViewModel main, ItemDocument doc, ListScope from, IReadOnlyList<long>? trail = null)
    {
        Trail = trail;
        _main = main;
        _from = from;
        _doc = doc;
        Markup = TrackNum(null, allowEmpty: true);
        Step = TrackNum(null, allowEmpty: true);
        PieceLength = TrackNum(null, allowEmpty: true);
        PieceArea = TrackNum(null, allowEmpty: true);
        PieceWeight = TrackNum(null, allowEmpty: true);
        WastePct = TrackNum(null, allowEmpty: true);
        LoadLookups();
        Load(doc);
    }

    public IReadOnlyList<long>? Trail { get; }
    public ListScope From => _from;

    public override string Section => _from switch
    {
        ListScope.Merchandise => "merchandise",
        ListScope.RawMaterials => "raw",
        _ => "products",
    };

    // ---------- fields ----------

    public ItemKind Kind => _doc.Item.Kind;
    public bool IsProduct => Kind == ItemKind.Product;
    public bool IsBought => Kind != ItemKind.Product;
    public bool IsMerchandise => Kind == ItemKind.Merchandise;
    public bool IsRaw => Kind == ItemKind.RawMaterial;
    public bool IsMaterial => Kind == ItemKind.RawMaterial || (IsMerchandise && UsableAsMaterial);
    public bool IsNew => _doc.Item.Id == 0;
    public long Id => _doc.Item.Id;
    public string KindText => Strings.Kind(Kind);

    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private long? _folderId;
    [ObservableProperty] private bool _usableAsMaterial;
    [ObservableProperty] private long? _materialFolderId;
    [ObservableProperty] private QtyUnit _unit;
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private bool _hasWaste;
    public NumField WastePct { get; }

    public NumField Markup { get; }
    public NumField Step { get; }
    public NumField PieceLength { get; }
    public NumField PieceArea { get; }
    public NumField PieceWeight { get; }

    public ObservableCollection<SourceRow> Sources { get; } = [];
    public ObservableCollection<LineRow> Lines { get; } = [];
    public ObservableCollection<LaborRow> Labor { get; } = [];
    public ObservableCollection<VariantRow> Variants { get; } = [];
    public ObservableCollection<BreakdownLine> Breakdown { get; } = [];
    public ObservableCollection<UsageRow> Usage { get; } = [];

    public List<PickerEntry> FolderEntries { get; private set; } = [];
    public List<PickerEntry> MaterialFolderEntries { get; private set; } = [];
    public List<PickerEntry> SupplierEntries { get; private set; } = [];
    public List<PickerEntry> MaterialEntries { get; private set; } = [];
    public List<PickerEntry> ProductEntries { get; private set; } = [];
    /// <summary>Supplier choices for variations: every supplier of the item, plus "all" (0).</summary>
    [ObservableProperty] private List<Choice<long>> _variantSupplierChoices = [];
    /// <summary>Waste column of the recipe: only shown when some line uses a material with waste.</summary>
    [ObservableProperty] private System.Windows.GridLength _wasteWidth = new(0);
    public bool HasWasteColumn => WasteWidth.Value > 0;

    partial void OnWasteWidthChanged(System.Windows.GridLength value) => OnPropertyChanged(nameof(HasWasteColumn));
    public List<Choice<QtyUnit>> Units { get; } = Choices.Units();

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _priceText = "";
    [ObservableProperty] private string _costText = "";
    [ObservableProperty] private string _marginText = "";
    [ObservableProperty] private decimal _marginPct;
    [ObservableProperty] private string _laborTotalText = "";
    [ObservableProperty] private string _materialsTotalText = "";
    [ObservableProperty] private string _markupHint = "";
    [ObservableProperty] private string _stepHint = "";
    [ObservableProperty] private bool _dirty;
    [ObservableProperty] private bool _hasUnseenChange;

    public bool MultipleSources => Sources.Count > 1;
    public bool HasVariants => Variants.Count > 0;
    public System.Windows.GridLength VariantSupplierWidth => new(MultipleSources ? 1 : 0, System.Windows.GridUnitType.Star);
    public bool HasUsage => Usage.Count > 0;
    public bool ShowUsage => IsMaterial || HasUsage;
    public override bool IsDirty => Dirty;

    NumField TrackNum(decimal? v, bool allowEmpty = false)
    {
        var f = new NumField(v, allowEmpty);
        f.Changed += OnEdited;
        return f;
    }

    // ---------- loading ----------

    void LoadLookups()
    {
        var store = AppState.Store;
        var scopeFolders = store.GetFolders(Kind.ToScope());
        FolderEntries = PickerEntry.Folders(scopeFolders, Loc.T("folder.root"));
        var rawFolders = Kind == ItemKind.RawMaterial ? scopeFolders : store.GetFolders(ListScope.RawMaterials);
        MaterialFolderEntries = PickerEntry.Folders(rawFolders, Loc.T("folder.root"));

        var sups = store.GetSuppliers();
        _suppliers = sups.ToDictionary(s => s.Id);
        SupplierEntries = PickerEntry.Tree(store.GetFolders(ListScope.Suppliers),
            sups.Select(s => (s.Id, s.Name, s.Currency == Currency.Usd ? "$" : null, s.FolderId)), Loc.T("item.noSupplier"));

        if (Kind == ItemKind.Product)
        {
            // raw materials (with merchandise marked as raw material) for "add raw material",
            // products and merchandise for "add product" (never the product itself)
            var mats = store.GetMaterials().Where(m => m.Info.Id != _doc.Item.Id).ToList();
            _materials = mats.ToDictionary(m => m.Info.Id, m => m.Info);
            (long, string, string?, long?) Entry(MaterialInfo m, long? folder) =>
                (m.Id, $"{m.Code} · {m.Name}", $"{Fmt.Money4(m.Cost)}/{Strings.Unit(m.Unit)}", folder);
            MaterialEntries = PickerEntry.Tree(rawFolders, mats
                .Where(m => m.Info.Kind == ItemKind.RawMaterial || (m.Info.Kind == ItemKind.Merchandise && m.Info.UsableAsMaterial))
                .Select(m => Entry(m.Info, m.Info.Kind == ItemKind.RawMaterial ? m.FolderId : m.MaterialFolderId)));
            ProductEntries =
            [
                .. PickerEntry.Tree(scopeFolders, mats.Where(m => m.Info.Kind == ItemKind.Product).Select(m => Entry(m.Info, m.FolderId)), section: Loc.T("nav.products")),
                .. PickerEntry.Tree(store.GetFolders(ListScope.Merchandise), mats.Where(m => m.Info.Kind == ItemKind.Merchandise).Select(m => Entry(m.Info, m.FolderId)), section: Loc.T("nav.merchandise")),
            ];
        }
        OnPropertyChanged(nameof(FolderEntries));
        OnPropertyChanged(nameof(MaterialFolderEntries));
        OnPropertyChanged(nameof(SupplierEntries));
        OnPropertyChanged(nameof(MaterialEntries));
        OnPropertyChanged(nameof(ProductEntries));
    }

    /// <summary>Lines with a finished product or (plain) merchandise: counted in pieces, no unit or waste fields.</summary>
    bool IsProductComponent(long? id) => id is long i && _materials.TryGetValue(i, out var m)
        && (m.Kind == ItemKind.Product || (m.Kind == ItemKind.Merchandise && !m.UsableAsMaterial));

    void Load(ItemDocument doc)
    {
        _doc = doc;
        Fill(doc, fresh: true);
        _original = Snapshot();
        Recalc();
        ResetUndo();
    }

    /// <summary>Puts a document's values in the form. <paramref name="fresh"/>: just loaded (sort variations, reload usage).</summary>
    void Fill(ItemDocument doc, bool fresh)
    {
        _loading = true;
        var it = doc.Item;
        Code = it.Code;
        Name = it.Name;
        FolderId = it.FolderId;
        UsableAsMaterial = it.UsableAsMaterial;
        MaterialFolderId = it.MaterialFolderId;
        Unit = it.Unit;
        Notes = it.Notes ?? "";
        Markup.Set(it.MarkupPct);
        Step.Set(it.RoundingStep);
        PieceLength.Set(it.PieceLengthCm);
        PieceArea.Set(it.PieceAreaCm2);
        PieceWeight.Set(it.PieceWeightKg);
        HasWaste = it.HasWaste;
        WastePct.Set(it.WastePct);
        HasUnseenChange = it.HasUnseenChange;

        Reset(Sources, doc.Sources.Select(s => new SourceRow(s)));
        Reset(Lines, doc.Lines.Select((l, i) => new LineRow(l,
            _productLines is { } flags && i < flags.Length ? flags[i] : IsProductComponent(l.MaterialId))));
        Reset(Labor, doc.Labor.Select(l => new LaborRow(l)));
        // variations sorted by supplier name, then supplier code (when loading; undo keeps the order as it was)
        string SupplierName(long? id) => id is long i && _suppliers.TryGetValue(i, out var sup) ? sup.Name : "";
        var variants = fresh
            ? doc.Variants.OrderBy(v => SupplierName(v.SupplierId), NaturalComparer.Instance).ThenBy(v => v.Code ?? "", NaturalComparer.Instance)
            : doc.Variants.AsEnumerable();
        Reset(Variants, variants.Select(v => new VariantRow(v)));

        if (fresh)
        {
            Usage.Clear();
            if (!IsNew) foreach (var u in AppState.Store.GetUsage(it.Id)) Usage.Add(u);
            OnPropertyChanged(nameof(HasUsage));
            OnPropertyChanged(nameof(ShowUsage));
            OnPropertyChanged(nameof(IsNew));
            OnPropertyChanged(nameof(Id));
        }
        _loading = false;
    }

    // undo keeps empty new rows and which recipe lines are product lines
    sealed record FormState(ItemDocument Doc, bool[] ProductLines);

    protected override object? CaptureState() => new FormState(BuildDocument(forSave: false), Lines.Select(l => l.IsProductLine).ToArray());

    protected override void RestoreState(object state)
    {
        var f = (FormState)state;
        _productLines = f.ProductLines;
        Fill(f.Doc, fresh: false);
        _productLines = null;
        Recalc();
    }

    bool[]? _productLines;

    protected override string StateKey() => Snapshot();

    void Reset<T>(ObservableCollection<T> list, IEnumerable<T> rows) where T : EditRow
    {
        list.Clear();
        foreach (var r in rows) Attach(list, r);
    }

    void Attach<T>(ObservableCollection<T> list, T row) where T : EditRow
    {
        row.Changed += OnEdited;
        list.Add(row);
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Code) or nameof(Name) or nameof(FolderId) or nameof(UsableAsMaterial)
            or nameof(MaterialFolderId) or nameof(Unit) or nameof(Notes) or nameof(HasWaste))
        {
            if (e.PropertyName == nameof(UsableAsMaterial))
            {
                OnPropertyChanged(nameof(IsMaterial));
                OnPropertyChanged(nameof(ShowUsage));
            }
            OnEdited();
        }
    }

    void OnEdited()
    {
        if (_loading) return;
        Recalc();
        NoteEdit();
    }

    // ---------- live calculation ----------

    void Recalc()
    {
        var s = AppState.Settings;
        Breakdown.Clear();
        decimal cost;
        var warn = CostWarning.None;

        if (IsProduct)
        {
            decimal materials = 0;
            foreach (var line in Lines)
            {
                var m = line.MaterialId is long mid ? _materials.GetValueOrDefault(mid) : null;
                if (m is null)
                {
                    warn |= CostWarning.MissingMaterial;
                    line.CostText = "—";
                    line.UnitCostText = "";
                    line.WarningText = Loc.T("cost.warnMissing");
                    continue;
                }
                line.IsWasteMaterial = m.HasWaste && !line.IsProductLine;
                var unitCost = Costing.CostPerUnit(m, line.Unit);
                if (unitCost is null)
                {
                    warn |= CostWarning.NoConversion;
                    line.CostText = "—";
                    line.UnitCostText = "";
                    line.WarningText = Loc.T("cost.warnConversion");
                    continue;
                }
                var c = unitCost.Value * line.Quantity.Or0 * (1 + (line.IsWasteMaterial ? line.Waste.Or0 : 0) / 100m);
                materials += c;
                line.CostText = Fmt.Money4(c);
                line.UnitCostText = $"{Fmt.Money4(unitCost.Value)}/{Strings.Unit(line.Unit)}";
                line.WarningText = "";
            }
            var width = new System.Windows.GridLength(Lines.Any(l => l.IsWasteMaterial) ? 96 : 0);
            WasteWidth = width;
            foreach (var line in Lines) line.SetWasteWidth(width);
            var seconds = Labor.Sum(l => l.Secs);
            var hours = seconds / 3600m;
            var labor = hours * s.LaborRatePerHour;
            var overhead = hours * s.OverheadRatePerHour;
            cost = materials + labor + overhead;
            LaborTotalText = Loc.F("item.laborTotal", Costing.FormatDuration(seconds));
            MaterialsTotalText = Fmt.Money(materials);

            Breakdown.Add(new(Loc.T("cost.materials"), Fmt.Money4(materials)));
            Breakdown.Add(new(Loc.T("cost.labor"), Fmt.Money4(labor)));
            Breakdown.Add(new(Loc.F("cost.laborLine", Costing.FormatDuration(seconds), Fmt.Money(s.LaborRatePerHour)), "", "sub"));
            Breakdown.Add(new(Loc.T("cost.overhead"), Fmt.Money4(overhead)));
            Breakdown.Add(new(Loc.F("cost.laborLine", Costing.FormatDuration(seconds), Fmt.Money(s.OverheadRatePerHour)), "", "sub"));
        }
        else
        {
            var models = Sources.Select(r => r.ToModel()).ToList();
            var choices = new List<Choice<long>> { new(0, Loc.T("item.allSuppliers")) };
            foreach (var sid in models.Select(m => m.SupplierId).OfType<long>().Distinct())
                if (_suppliers.TryGetValue(sid, out var sup)) choices.Add(new(sid, sup.Name));
            if (!choices.Select(c => c.Value).SequenceEqual(VariantSupplierChoices.Select(c => c.Value)))
                VariantSupplierChoices = choices;
            foreach (var r in Sources) r.UpdateHints(r.SupplierId is long sid ? _suppliers.GetValueOrDefault(sid) : null);
            var bc = Costing.ComputeBought(models, id => _suppliers.GetValueOrDefault(id), s.UsdPerEur);
            warn |= bc.Warnings;
            var waste = IsRaw && HasWaste ? WastePct.Value : null;
            cost = Costing.WithWaste(bc.UnitCost, waste);
            var multi = bc.Sources.Count > 1;
            for (var i = 0; i < bc.Sources.Count; i++)
            {
                var sc = bc.Sources[i];
                Sources[i].ShareText = Fmt.Pct(Math.Round(sc.Share * 100, 1));
                if (multi)
                {
                    var name = sc.Source.SupplierId is long sid && _suppliers.TryGetValue(sid, out var sup) ? sup.Name : Loc.T("item.noSupplier");
                    Breakdown.Add(new($"{name} · {Fmt.Pct(Math.Round(sc.Share * 100, 1))}", "", "header"));
                }
                Breakdown.Add(new(Loc.T("cost.listPrice"), Fmt.InCurrency(sc.ListPrice, sc.Currency)));
                if (sc.Currency == Currency.Usd)
                    Breakdown.Add(new($"{Loc.T("cost.inEur")} (1 € = {Fmt.Num(s.UsdPerEur)} $)", Fmt.Money4(sc.ListPriceEur), "sub"));
                if (sc.DiscountPct != 0)
                    Breakdown.Add(new($"{Loc.T("cost.discount")} {Fmt.Pct(sc.DiscountPct)}", "−" + Fmt.Money4(sc.ListPriceEur - sc.Net), "sub"));
                if (sc.ShippingPct != 0)
                    Breakdown.Add(new($"{Loc.T("cost.shipping")} {Fmt.Pct(sc.ShippingPct)}", "+" + Fmt.Money4(sc.Shipping), "sub"));
                if (sc.Source.PurchaseQty != 1)
                    Breakdown.Add(new($"÷ {Fmt.Num(sc.Source.PurchaseQty)} {Strings.Unit(Unit)}", "", "sub"));
                Breakdown.Add(new(Loc.F("cost.perUnit", Strings.Unit(Unit)), Fmt.Money4(sc.UnitCost)));
                if (multi && i < bc.Sources.Count - 1) Breakdown.Add(new("", "", "sep"));
            }
            if (multi) Breakdown.Add(new(Loc.T("cost.weighted"), Fmt.Money4(bc.UnitCost), "total"));
            if (waste is > 0 and < 100)
                Breakdown.Add(new(Loc.F("cost.waste", Fmt.Pct(waste.Value)), "+" + Fmt.Money4(cost - bc.UnitCost)));
        }

        var markup = Markup.Value ?? s.DefaultMarkupPct;
        var step = Step.Value ?? s.RoundingStep;
        var price = Costing.Price(cost, markup, step, s.RoundingMode);
        MarkupHint = Loc.F("common.default", Fmt.Pct(s.DefaultMarkupPct));
        StepHint = Loc.F("common.default", Fmt.Num(s.RoundingStep));

        Breakdown.Add(new("", "", "sep"));
        Breakdown.Add(new(Loc.T("cost.total"), Fmt.Money4(cost), "total"));
        Breakdown.Add(new($"{Loc.T("cost.markup")} {Fmt.Pct(markup)}", "+" + Fmt.Money4(price.RawPrice - cost)));
        Breakdown.Add(new(Loc.T("cost.rawPrice"), Fmt.Money4(price.RawPrice), "sub"));
        Breakdown.Add(new(Loc.T("cost.price"), Fmt.Money(price.Price), "big"));
        Breakdown.Add(new(Loc.T("cost.profit"), Fmt.Money(price.Profit)));
        Breakdown.Add(new(Loc.T("cost.margin"), Fmt.Pct(Math.Round(price.MarginPct, 1)), "row", MarginBrushConverter.Tone(price.MarginPct)));
        if (warn.HasFlag(CostWarning.MissingMaterial)) Breakdown.Add(new(Loc.T("cost.warnMissing"), "", "warn"));
        if (warn.HasFlag(CostWarning.NoConversion)) Breakdown.Add(new(Loc.T("cost.warnConversion"), "", "warn"));
        if (warn.HasFlag(CostWarning.NoPrice)) Breakdown.Add(new(Loc.T("cost.warnNoPrice"), "", "warn"));

        PriceText = Fmt.Money(price.Price);
        CostText = Fmt.Money4(cost);
        MarginText = Fmt.Pct(Math.Round(price.MarginPct, 1));
        MarginPct = price.MarginPct;
        Title = IsNew && string.IsNullOrWhiteSpace(Code) ? Loc.F("item.newTitle", KindText.ToLower(Loc.Culture)) : $"{Code}";
        OnPropertyChanged(nameof(MultipleSources));
        OnPropertyChanged(nameof(HasVariants));
        OnPropertyChanged(nameof(VariantSupplierWidth));
        Dirty = Snapshot() != _original;
    }

    /// <summary>Canonical text of everything editable: equal snapshots mean "no change", even after edits were undone.</summary>
    string Snapshot()
    {
        var sb = new StringBuilder();
        void N(NumField f) => sb.Append(f.IsValid ? f.Value?.ToString(CultureInfo.InvariantCulture) ?? "∅" : "!" + f.Text).Append('|');
        void S(object? o) => sb.Append(o is string str ? str.Trim() : o).Append('|');
        S(Code); S(Name); S(FolderId); S(UsableAsMaterial); S(UsableAsMaterial ? MaterialFolderId : null); S(Unit); S(Notes);
        N(Markup); N(Step); N(PieceLength); N(PieceArea); N(PieceWeight); S(HasWaste); N(WastePct);
        foreach (var r in Sources) { sb.Append("S:"); S(r.SupplierId); S(r.SupplierCode); S(r.Currency); N(r.Price); N(r.Qty); N(r.Discount); N(r.Shipping); N(r.Weight); N(r.Moq); N(r.Pack); }
        foreach (var r in Lines) { sb.Append("L:"); S(r.MaterialId); S(r.Unit); S(r.Comment); N(r.Quantity); N(r.Waste); }
        foreach (var r in Labor) { sb.Append("T:"); S(r.Comment); N(r.Seconds); }
        foreach (var r in Variants) { sb.Append("V:"); S(r.Name); S(r.Code); S(r.SupplierKey); }
        return sb.ToString();
    }

    // ---------- commands ----------

    [RelayCommand]
    void AddSource()
    {
        Attach(Sources, new SourceRow(new ItemSource { PurchaseQty = Sources.FirstOrDefault()?.Qty.Value ?? 1, Weight = 1 }));
        OnEdited();
    }

    [RelayCommand]
    void RemoveSource(SourceRow row)
    {
        Sources.Remove(row);
        OnEdited();
    }

    [RelayCommand]
    void AddLine()
    {
        Attach(Lines, new LineRow(new RecipeLine { Quantity = 1 }));
        OnEdited();
    }

    [RelayCommand]
    void AddProduct()
    {
        Attach(Lines, new LineRow(new RecipeLine { Quantity = 1, Unit = QtyUnit.Pcs }, isProduct: true));
        OnEdited();
    }

    [RelayCommand]
    void RemoveLine(LineRow row)
    {
        Lines.Remove(row);
        OnEdited();
    }

    /// <summary>A newly picked material starts in its own unit.</summary>
    public void MaterialPicked(LineRow row)
    {
        if (row.MaterialId is long id && _materials.TryGetValue(id, out var m)) row.Unit = row.IsProductLine ? QtyUnit.Pcs : m.Unit;
    }

    /// <summary>Picking a supplier fills the supplier code field focus and resets the currency to the supplier's.</summary>
    public void SupplierPicked(SourceRow row)
    {
        row.Currency = -1;
        Recalc();
    }

    [RelayCommand]
    void AddLabor()
    {
        Attach(Labor, new LaborRow(new LaborEntry { Seconds = 0 }));
        OnEdited();
    }

    [RelayCommand]
    void RemoveLabor(LaborRow row)
    {
        Labor.Remove(row);
        OnEdited();
    }

    [RelayCommand]
    void AddVariant()
    {
        Attach(Variants, new VariantRow(new ItemVariant()));
        OnEdited();
    }

    [RelayCommand]
    void RemoveVariant(VariantRow row)
    {
        Variants.Remove(row);
        OnEdited();
    }

    [RelayCommand]
    void OpenUsage(UsageRow row) => _main.OpenItem(row.ProductId, trail: Usage.Select(u => u.ProductId).ToList());

    [RelayCommand]
    void Back() => _main.GoBack(_from);

    public override bool IsGone => !IsNew && AppState.Store.GetItem(Id) is null;

    /// <summary>Coming back to this page: show what changed meanwhile (unless there are unsaved edits).</summary>
    public override void OnShown() => OnDataChanged(DataChange.All);

    [RelayCommand]
    void History()
    {
        if (IsNew) return;
        new PriceHistoryWindow(Id, $"{_doc.Item.Code} · {_doc.Item.Name}") { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
    }

    [RelayCommand]
    void Cancel()
    {
        Load(_doc);
    }

    [RelayCommand]
    void Delete()
    {
        if (IsNew) { Discard(); Back(); return; }
        var used = IsMaterial ? AppState.Store.CountUsage(Id) : 0;
        if (!Dialogs.ConfirmDelete($"{_doc.Item.Code} · {_doc.Item.Name}", used > 0 ? Loc.F("item.deleteUsed", used) : null)) return;
        AppState.Store.DeleteItem(Id);
        Dirty = false;
        Back();
    }

    [RelayCommand]
    void Save() => TrySave();

    public override void Discard() => Load(_doc);

    public override bool TrySave()
    {
        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name))
        {
            Dialogs.Error(Loc.T("common.required"));
            return false;
        }
        var numbersOk = Markup.IsValid && Step.IsValid && PieceLength.IsValid && PieceArea.IsValid && PieceWeight.IsValid && WastePct.IsValid
            && Sources.All(s => s.IsValid) && Lines.All(l => l.IsValid) && Labor.All(l => l.IsValid);
        if (!numbersOk)
        {
            Dialogs.Error(Loc.T("common.invalidNumber"));
            return false;
        }

        var doc = BuildDocument();
        try
        {
            var id = AppState.Store.SaveItem(doc);
            Load(AppState.Store.LoadItem(id)!);
            _main.ShowToast(Loc.T("common.saved"));
            return true;
        }
        catch (DuplicateCodeException ex)
        {
            Dialogs.Error(Loc.F("item.duplicateCode", ex.Code));
            return false;
        }
        catch (RecipeCycleException)
        {
            Dialogs.Error(Loc.T("item.recipeCycle"));
            return false;
        }
    }

    /// <summary>The form's values as a document (what Save stores).</summary>
    ItemDocument BuildDocument(bool forSave = true)
    {
        var item = new Item
        {
            Id = _doc.Item.Id,
            Kind = Kind,
            Code = Code.Trim(),
            Name = Name.Trim(),
            FolderId = FolderId,
            UsableAsMaterial = IsMerchandise && UsableAsMaterial,
            MaterialFolderId = IsMerchandise && UsableAsMaterial ? MaterialFolderId : null,
            Unit = IsProduct ? QtyUnit.Pcs : Unit,
            PieceLengthCm = PieceLength.Value,
            PieceAreaCm2 = PieceArea.Value,
            PieceWeightKg = PieceWeight.Value,
            MarkupPct = Markup.Value,
            RoundingStep = Step.Value,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            HasUnseenChange = _doc.Item.HasUnseenChange,
            HasWaste = IsRaw && HasWaste,
            WastePct = IsRaw && HasWaste ? WastePct.Value : null,
        };
        return new ItemDocument
        {
            Item = item,
            Sources = IsBought ? Sources.Select(s => s.ToModel()).ToList() : [],
            Lines = IsProduct ? Lines.Select(l => l.ToModel()).ToList() : [],
            Labor = IsProduct ? Labor.Select(l => l.ToModel()).ToList() : [],
            Variants = IsBought ? Variants.Select(v => v.ToModel()).Where(v => !forSave || v.Name.Length > 0 || v.Code is not null).ToList() : [],
        };
    }

    public override void OnDataChanged(DataChange change)
    {
        // keep the user's edits; otherwise follow changes made elsewhere (bell, supplier, settings…)
        LoadLookups();
        if (!Dirty && !IsNew && AppState.Store.LoadItem(Id) is { } fresh) Load(fresh);
        else Recalc();
    }
}
