namespace ProductCosting.Core.Model;

public enum ItemKind { Product = 1, Merchandise = 2, RawMaterial = 3 }

/// <summary>The four lists of the app. Item scopes share their numeric value with <see cref="ItemKind"/>.</summary>
public enum ListScope { Products = 1, Merchandise = 2, RawMaterials = 3, Suppliers = 4 }

public enum QtyUnit { Pcs = 0, Cm = 1, Cm2 = 2, Kg = 3 }

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
    public static ListScope ToScope(this ItemKind kind) => (ListScope)(int)kind;

    public static IEnumerable<PriceReason> Split(this PriceReason reasons)
    {
        foreach (PriceReason r in Enum.GetValues<PriceReason>())
            if (r != PriceReason.None && reasons.HasFlag(r)) yield return r;
    }
}
