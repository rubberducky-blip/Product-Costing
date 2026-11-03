namespace ProductCosting.Core.Model;

public enum ItemKind { Product = 1, Merchandise = 2, RawMaterial = 3 }

/// <summary>The four lists of the app. Item scopes share their numeric value with <see cref="ItemKind"/>.</summary>
public enum ListScope { Products = 1, Merchandise = 2, RawMaterials = 3, Suppliers = 4 }

/// <summary>
/// What an item is measured by: pieces, length, area or weight. Quantities are stored in the base unit of each
/// (cm, cm², kg); <see cref="MeasureUnit"/> is the unit a quantity is typed in.
/// </summary>
public enum QtyUnit { Pcs = 0, Cm = 1, Cm2 = 2, Kg = 3 }

/// <summary>The unit a quantity is entered and shown in; its value is stored in the base unit of its <see cref="QtyUnit"/>.</summary>
public enum MeasureUnit { Pcs = 0, Cm = 1, M = 2, Cm2 = 3, M2 = 4, G = 5, Kg = 6 }

public enum RoundingMode { Nearest = 0, Up = 1, Down = 2 }

public enum Currency { Eur = 0, Usd = 1 }

public enum SortMode { Code = 0, Manual = 1 }

/// <summary>Draft (saved, editable) → Executed (placed with the supplier, read-only) → Archived.</summary>
public enum OrderStatus { Draft = 0, Executed = 1, Archived = 2 }

/// <summary>Why a price changed. Stored as a bit mask, so one change can carry several reasons.</summary>
[Flags]
public enum PriceReason
{
    None = 0,
    Created = 1,
    SupplierCostUp = 2,
    SupplierCostDown = 4,
    MaterialCostUp = 8,
    MaterialCostDown = 16,
    LaborUp = 32,
    LaborDown = 64,
    OverheadUp = 128,
    OverheadDown = 256,
    ExchangeRate = 512,
    MarginAdjustment = 1024,
    Rounding = 2048,
    Other = 4096,
    /// <summary>The item is now priced per another unit (e.g. per m² instead of per cm²).</summary>
    PriceUnit = 8192,
}

[Flags]
public enum CostWarning
{
    None = 0,
    MissingMaterial = 1,
    NoConversion = 2,
    NoPrice = 4,
}

public static class EnumExtensions
{
    public static QtyUnit Dimension(this MeasureUnit u) => u switch
    {
        MeasureUnit.Cm or MeasureUnit.M => QtyUnit.Cm,
        MeasureUnit.Cm2 or MeasureUnit.M2 => QtyUnit.Cm2,
        MeasureUnit.G or MeasureUnit.Kg => QtyUnit.Kg,
        _ => QtyUnit.Pcs,
    };

    /// <summary>How many base units (cm, cm², kg, pieces) one of this unit is.</summary>
    public static decimal Factor(this MeasureUnit u) => u switch
    {
        MeasureUnit.M => 100m,
        MeasureUnit.M2 => 10000m,
        MeasureUnit.G => 0.001m,
        _ => 1m,
    };

    /// <summary>The units a quantity of this dimension can be typed in, base unit first.</summary>
    public static MeasureUnit[] EntryUnits(this QtyUnit d) => d switch
    {
        QtyUnit.Cm => [MeasureUnit.Cm, MeasureUnit.M],
        QtyUnit.Cm2 => [MeasureUnit.Cm2, MeasureUnit.M2],
        QtyUnit.Kg => [MeasureUnit.G, MeasureUnit.Kg],
        _ => [MeasureUnit.Pcs],
    };

    /// <summary>The entry unit to use: the stored one when it fits the dimension, else the dimension's base unit.</summary>
    public static MeasureUnit EntryUnit(this QtyUnit d, MeasureUnit? stored) =>
        stored is MeasureUnit u && u.Dimension() == d ? u : d switch
        {
            QtyUnit.Cm => MeasureUnit.Cm,
            QtyUnit.Cm2 => MeasureUnit.Cm2,
            QtyUnit.Kg => MeasureUnit.Kg,
            _ => MeasureUnit.Pcs,
        };

    /// <summary>The unit new purchase rows start in: metres, square metres, kilograms (items are bought in larger units).</summary>
    public static MeasureUnit PurchaseDefault(this QtyUnit d) => d switch
    {
        QtyUnit.Cm => MeasureUnit.M,
        QtyUnit.Cm2 => MeasureUnit.M2,
        QtyUnit.Kg => MeasureUnit.Kg,
        _ => MeasureUnit.Pcs,
    };

    /// <summary>SQL for the factor of an items.price_unit column (cost per base unit × factor = cost per price unit).</summary>
    public const string FactorSql = "CASE {0} WHEN 2 THEN 100.0 WHEN 4 THEN 10000.0 WHEN 5 THEN 0.001 ELSE 1.0 END";

    /// <summary>A stored base quantity in the entry unit (rounded off the binary noise of the database).</summary>
    public static decimal FromBase(this MeasureUnit u, decimal baseQty) => Math.Round(baseQty / u.Factor(), 6);

    public static decimal ToBase(this MeasureUnit u, decimal qty) => qty * u.Factor();

    public static ListScope ToScope(this ItemKind kind) => (ListScope)(int)kind;

    public static IEnumerable<PriceReason> Split(this PriceReason reasons)
    {
        foreach (PriceReason r in Enum.GetValues<PriceReason>())
            if (r != PriceReason.None && reasons.HasFlag(r)) yield return r;
    }
}
