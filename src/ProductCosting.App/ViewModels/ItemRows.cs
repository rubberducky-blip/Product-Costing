using CommunityToolkit.Mvvm.ComponentModel;
using ProductCosting.App.Infrastructure;
using ProductCosting.Core.Calc;
using ProductCosting.Core.Model;

namespace ProductCosting.App.ViewModels;

public sealed partial class SourceRow : EditRow
{
    public SourceRow(ItemSource s)
    {
        _supplierId = s.SupplierId;
        _supplierCode = s.SupplierCode ?? "";
        _currency = s.Currency is null ? -1 : (int)s.Currency;
        Price = Num(s.PurchasePrice);
        Qty = Num(s.PurchaseQty, min: 0.000001m);
        Discount = Num(s.DiscountPct, allowEmpty: true);
        Shipping = Num(s.ShippingPct, allowEmpty: true);
        Weight = Num(s.Weight);
        Moq = Num(s.Moq, allowEmpty: true);
        Pack = Num(s.PackQty, allowEmpty: true);
    }

    [ObservableProperty] private long? _supplierId;
    [ObservableProperty] private string _supplierCode;
    /// <summary>-1 = the supplier's currency, 0 = euro, 1 = dollar.</summary>
    [ObservableProperty] private int _currency;

    public NumField Price { get; }
    public NumField Qty { get; }
    public NumField Discount { get; }
    public NumField Shipping { get; }
    public NumField Weight { get; }
    public NumField Moq { get; }
    public NumField Pack { get; }

    [ObservableProperty] private string _discountHint = "";
    [ObservableProperty] private string _shippingHint = "";
    [ObservableProperty] private List<Choice<int>> _currencyChoices = [];
    [ObservableProperty] private string _shareText = "";

    public bool IsValid => Price.IsValid && Qty.IsValid && Discount.IsValid && Shipping.IsValid && Weight.IsValid && Moq.IsValid && Pack.IsValid;

    public void UpdateHints(Supplier? s)
    {
        DiscountHint = Loc.F("common.default", Fmt.Pct(s?.DiscountPct ?? 0));
        ShippingHint = Loc.F("common.default", Fmt.Pct(s?.ShippingPct ?? 0));
        var supCur = s?.Currency ?? Core.Model.Currency.Eur;
        var label = Loc.F("currency.supplier", supCur == Core.Model.Currency.Usd ? "$" : "€");
        if (CurrencyChoices.Count == 0 || CurrencyChoices[0].Label != label)
        {
            var keep = Currency;
            Quiet = true;
            try
            {
                CurrencyChoices =
                [
                    new(-1, label),
                    new(0, Loc.T("currency.eur")),
                    new(1, Loc.T("currency.usd")),
                ];
                _currency = keep;
                OnPropertyChanged(nameof(Currency));
            }
            finally
            {
                Quiet = false;
            }
        }
    }

    public ItemSource ToModel() => new()
    {
        SupplierId = SupplierId,
        SupplierCode = string.IsNullOrWhiteSpace(SupplierCode) ? null : SupplierCode.Trim(),
        PurchasePrice = Price.Or0,
        PurchaseQty = Qty.Value ?? 1,
        Currency = Currency < 0 ? null : (Currency)Currency,
        DiscountPct = Discount.Value,
        ShippingPct = Shipping.Value,
        Weight = Weight.Value ?? 1,
        Moq = Moq.Value is > 0 ? Moq.Value : null,
        PackQty = Pack.Value is > 0 ? Pack.Value : null,
    };
}

public sealed partial class LineRow : EditRow
{
    /// <param name="isProduct">A finished product or merchandise: always counted in pieces, never with waste.</param>
    public LineRow(RecipeLine l, bool isProduct = false)
    {
        IsProductLine = isProduct;
        _materialId = l.MaterialId;
        _unit = l.Unit;
        _comment = l.Comment ?? "";
        Quantity = Num(l.Quantity);
        Waste = Num(l.WastePct, allowEmpty: true);
    }

    [ObservableProperty] private long? _materialId;
    [ObservableProperty] private QtyUnit _unit;
    [ObservableProperty] private string _comment;
    public NumField Quantity { get; }
    /// <summary>Extra waste on this line, only for materials that have waste.</summary>
    public NumField Waste { get; }
    [ObservableProperty] private bool _isWasteMaterial;
    public bool IsProductLine { get; }
    /// <summary>Width of the waste column: zero when no line of the recipe uses a material with waste.</summary>
    [ObservableProperty] private System.Windows.GridLength _wasteWidth = new(0);

    /// <summary>Layout only: not an edit.</summary>
    public void SetWasteWidth(System.Windows.GridLength width)
    {
        Quiet = true;
        WasteWidth = width;
        Quiet = false;
    }

    [ObservableProperty] private string _costText = "";
    [ObservableProperty] private string _unitCostText = "";
    [ObservableProperty] private string _warningText = "";

    public bool IsValid => Quantity.IsValid && Waste.IsValid;

    public RecipeLine ToModel() => new()
    {
        MaterialId = MaterialId,
        Quantity = Quantity.Or0,
        Unit = IsProductLine ? QtyUnit.Pcs : Unit,
        WastePct = !IsProductLine && IsWasteMaterial && Waste.Value is > 0 ? Waste.Value : null,
        Comment = string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim(),
    };
}

public sealed partial class LaborRow : EditRow
{
    public LaborRow(LaborEntry l)
    {
        _comment = l.Comment ?? "";
        Seconds = Num(l.Seconds);
        Seconds.Changed += () => OnPropertyChanged(nameof(DurationText));
    }

    [ObservableProperty] private string _comment;
    public NumField Seconds { get; }

    public string DurationText => Costing.FormatDuration(Secs);
    public int Secs => (int)Math.Round(Seconds.Or0);
    public bool IsValid => Seconds.IsValid;

    public LaborEntry ToModel() => new() { Seconds = Secs, Comment = string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim() };
}

public sealed partial class VariantRow(ItemVariant v) : EditRow
{
    [ObservableProperty] private string _name = v.Name;
    [ObservableProperty] private string _code = v.Code ?? "";
    /// <summary>Supplier of the variation; 0 = every supplier of the item.</summary>
    [ObservableProperty] private long _supplierKey = v.SupplierId ?? 0;

    public ItemVariant ToModel() => new()
    {
        Name = Name.Trim(),
        Code = string.IsNullOrWhiteSpace(Code) ? null : Code.Trim(),
        SupplierId = SupplierKey == 0 ? null : SupplierKey,
    };
}
