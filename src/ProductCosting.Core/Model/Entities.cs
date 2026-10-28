namespace ProductCosting.Core.Model;

public sealed class AppSettings
{
    public decimal LaborRatePerHour { get; set; }
    public decimal OverheadRatePerHour { get; set; }
    public decimal DefaultMarkupPct { get; set; } = 100m;
    public decimal RoundingStep { get; set; } = 0.05m;
    public RoundingMode RoundingMode { get; set; } = RoundingMode.Nearest;
    public string CurrencySymbol { get; set; } = "€";
    public string Language { get; set; } = "el";
    /// <summary>How many US dollars one euro buys (1 € = X $).</summary>
    public decimal UsdPerEur { get; set; } = 1.10m;
    public string BackupFolder { get; set; } = "";
    public bool AutoBackup { get; set; } = true;
    public int BackupKeepCount { get; set; } = 30;
    public string LastAutoBackupDay { get; set; } = "";

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

public sealed class Folder
{
    public long Id { get; set; }
    public ListScope Scope { get; set; }
    public long? ParentId { get; set; }
    public string Name { get; set; } = "";
    public SortMode SortMode { get; set; }
    public int Position { get; set; }
}

public sealed class Supplier
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long? FolderId { get; set; }
    public string? Country { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal ShippingPct { get; set; }
    public Currency Currency { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
}

public sealed class Item
{
    public long Id { get; set; }
    public ItemKind Kind { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool UsableAsMaterial { get; set; }
    public long? FolderId { get; set; }
    /// <summary>Folder in the raw-materials list, for merchandise also used as raw material.</summary>
    public long? MaterialFolderId { get; set; }
    public QtyUnit Unit { get; set; }
    public decimal? PieceLengthCm { get; set; }
    public decimal? PieceAreaCm2 { get; set; }
    public decimal? PieceWeightKg { get; set; }
    public decimal? MarkupPct { get; set; }
    public decimal? RoundingStep { get; set; }
    public string? Notes { get; set; }

    // Cached results of the last calculation (kept up to date by the pricing engine).
    public decimal Cost { get; set; }
    public decimal CostMaterials { get; set; }
    public decimal CostLabor { get; set; }
    public decimal CostOverhead { get; set; }
    public int LaborSeconds { get; set; }
    public decimal EffMarkup { get; set; }
    public decimal EffStep { get; set; }
    public decimal Price { get; set; }
    public CostWarning Warnings { get; set; }
    public bool HasUnseenChange { get; set; }

    public bool IsBought => Kind != ItemKind.Product;
    public bool IsMaterial => Kind == ItemKind.RawMaterial || (Kind == ItemKind.Merchandise && UsableAsMaterial);
}

/// <summary>Where a bought item comes from: one row per supplier, weighted by the share of stock it provides.</summary>
public sealed class ItemSource
{
    public long Id { get; set; }
    public long ItemId { get; set; }
    public long? SupplierId { get; set; }
    public string? SupplierCode { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal PurchaseQty { get; set; } = 1;
    /// <summary>Null = the supplier's currency (euro when there is no supplier).</summary>
    public Currency? Currency { get; set; }
    public decimal? DiscountPct { get; set; }
    public decimal? ShippingPct { get; set; }
    public decimal Weight { get; set; } = 1;
}

public sealed class RecipeLine
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public long? MaterialId { get; set; }
    public decimal Quantity { get; set; }
    public QtyUnit Unit { get; set; }
    public string? Comment { get; set; }
}

public sealed class LaborEntry
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public int Seconds { get; set; }
    public string? Comment { get; set; }
}

/// <summary>A variation of an item (e.g. a colour) with its own supplier code. Only used for orders.</summary>
public sealed class ItemVariant
{
    public long Id { get; set; }
    public long ItemId { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
}

/// <summary>An item with everything that is edited on its form.</summary>
public sealed class ItemDocument
{
    public Item Item { get; set; } = new();
    public List<ItemSource> Sources { get; set; } = [];
    public List<RecipeLine> Lines { get; set; } = [];
    public List<LaborEntry> Labor { get; set; } = [];
    public List<ItemVariant> Variants { get; set; } = [];
}

public sealed class PriceNotification
{
    public long Id { get; set; }
    public long ItemId { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public PriceReason Reasons { get; set; }
    public string UpdatedAt { get; set; } = "";
    public bool Adjusted { get; set; }
    // joined
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public ItemKind Kind { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal Cost { get; set; }
    public decimal EffMarkup { get; set; }

    public bool IsIncrease => NewPrice > OldPrice;
    public decimal ChangePct => OldPrice == 0 ? 0 : (NewPrice - OldPrice) / OldPrice * 100m;
}

public sealed class PriceHistoryEntry
{
    public long ItemId { get; set; }
    public string Day { get; set; } = "";
    public decimal? OldPrice { get; set; }
    public decimal Price { get; set; }
    public PriceReason Reasons { get; set; }

    public DateTime Date => DateTime.ParseExact(Day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Lightweight row for the virtualized lists.</summary>
public sealed class ItemRow : System.ComponentModel.INotifyPropertyChanged
{
    bool _hasUnseenChange;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public long Id { get; set; }
    public ItemKind Kind { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public long? FolderId { get; set; }
    public int Position { get; set; }
    public string? Suppliers { get; set; }
    public string? SupplierCode { get; set; }
    public QtyUnit Unit { get; set; }
    public decimal Cost { get; set; }
    public decimal Price { get; set; }
    public CostWarning Warnings { get; set; }
    public bool UsableAsMaterial { get; set; }
    /// <summary>Set by the list when showing filter results from several folders.</summary>
    public string? FolderPath { get; set; }

    public bool HasUnseenChange
    {
        get => _hasUnseenChange;
        set
        {
            if (_hasUnseenChange == value) return;
            _hasUnseenChange = value;
            PropertyChanged?.Invoke(this, new(nameof(HasUnseenChange)));
        }
    }

    public bool HasWarnings => Warnings != CostWarning.None;
    public bool IsMerchandiseAsMaterial => Kind == ItemKind.Merchandise && UsableAsMaterial;
    public decimal Profit => Price - Cost;
    public decimal MarginPct => Price > 0 ? (Price - Cost) / Price * 100m : 0;
}

public sealed class SupplierRow
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long? FolderId { get; set; }
    public int Position { get; set; }
    public string? Country { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal ShippingPct { get; set; }
    public Currency Currency { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int ItemCount { get; set; }
    public string? FolderPath { get; set; }
}

public sealed class FolderTile
{
    public long Id { get; set; }
    public long? ParentId { get; set; }
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public int ItemCount { get; set; }
    public int FolderCount { get; set; }
}

public sealed class SearchHit
{
    public bool IsSupplier { get; set; }
    public long Id { get; set; }
    public ItemKind Kind { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public decimal Cost { get; set; }
    public QtyUnit Unit { get; set; }
}

/// <summary>A product that uses a material in its recipe.</summary>
public sealed class UsageRow
{
    public long ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Quantity { get; set; }
    public QtyUnit Unit { get; set; }
}

/// <summary>An item bought from a supplier, as listed on the supplier and on its order form.</summary>
public sealed class SupplierItemRow
{
    public long ItemId { get; set; }
    public ItemKind Kind { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? SupplierCode { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal PurchaseQty { get; set; }
    public QtyUnit Unit { get; set; }
    public Currency Currency { get; set; }
    public decimal Weight { get; set; }
}

/// <summary>Cost data of a material, enough to price a recipe line.</summary>
public sealed class MaterialInfo
{
    public long Id { get; set; }
    public ItemKind Kind { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public QtyUnit Unit { get; set; }
    public decimal? PieceLengthCm { get; set; }
    public decimal? PieceAreaCm2 { get; set; }
    public decimal? PieceWeightKg { get; set; }
    /// <summary>Cost of one <see cref="Unit"/>.</summary>
    public decimal Cost { get; set; }
}

public sealed class BackupInfo
{
    public string Path { get; set; } = "";
    public string FileName => System.IO.Path.GetFileName(Path);
    public DateTime CreatedAt { get; set; }
    public string Kind { get; set; } = "manual";
    public long SizeBytes { get; set; }
}
