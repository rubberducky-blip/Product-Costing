using ProductCosting.Core.Model;

namespace ProductCosting.Core.Calc;

public sealed record SourceCost(
    ItemSource Source,
    Currency Currency,
    decimal ListPrice,      // in the source currency
    decimal ListPriceEur,
    decimal DiscountPct,
    decimal ShippingPct,
    decimal Net,            // after discount, euro
    decimal Shipping,       // on the net price, euro
    decimal Total,          // one purchase, euro
    decimal UnitCost,       // per item unit, euro
    decimal Share);         // 0..1, from the weights

public sealed record BoughtCost(IReadOnlyList<SourceCost> Sources, decimal UnitCost, CostWarning Warnings);

public sealed record LineCost(RecipeLine Line, MaterialInfo? Material, decimal? UnitCost, decimal? Cost);

public sealed record ProductCost(
    IReadOnlyList<LineCost> Lines,
    decimal Materials,
    int LaborSeconds,
    decimal Labor,
    decimal Overhead,
    CostWarning Warnings)
{
    public decimal Total => Materials + Labor + Overhead;
}

public sealed record PriceResult(decimal Cost, decimal MarkupPct, decimal Step, decimal RawPrice, decimal Price)
{
    public decimal Profit => Price - Cost;
    public decimal MarginPct => Price > 0 ? Profit / Price * 100m : 0;
}

/// <summary>All cost, price and rounding rules. Pure functions, no database access.</summary>
public static class Costing
{
    public static decimal ToEur(decimal amount, Currency currency, decimal usdPerEur) =>
        currency == Currency.Usd && usdPerEur > 0 ? amount / usdPerEur : amount;

    public static Currency EffectiveCurrency(ItemSource s, Supplier? supplier) =>
        s.Currency ?? supplier?.Currency ?? Currency.Eur;

    /// <summary>Cost of one purchase from one source: (price → €) − discount + shipping, ÷ quantity.</summary>
    public static SourceCost ComputeSource(ItemSource s, Supplier? supplier, decimal usdPerEur, decimal share = 1)
    {
        var currency = EffectiveCurrency(s, supplier);
        var discount = s.DiscountPct ?? supplier?.DiscountPct ?? 0;
        var shipping = s.ShippingPct ?? supplier?.ShippingPct ?? 0;
        var eur = ToEur(s.PurchasePrice, currency, usdPerEur);
        var net = eur * (1 - discount / 100m);
        var ship = net * shipping / 100m;
        var total = net + ship;
        var qty = s.PurchaseQty > 0 ? s.PurchaseQty : 1;
        return new SourceCost(s, currency, s.PurchasePrice, eur, discount, shipping, net, ship, total, total / qty, share);
    }

    /// <summary>Weighted cost of a bought item over all its sources.</summary>
    public static BoughtCost ComputeBought(IReadOnlyList<ItemSource> sources, Func<long, Supplier?> supplier, decimal usdPerEur)
    {
        if (sources.Count == 0) return new BoughtCost([], 0, CostWarning.NoPrice);
        var totalWeight = sources.Sum(s => s.Weight > 0 ? s.Weight : 0);
        var list = new List<SourceCost>(sources.Count);
        decimal unit = 0;
        foreach (var s in sources)
        {
            var w = s.Weight > 0 ? s.Weight : 0;
            var share = totalWeight > 0 ? w / totalWeight : 1m / sources.Count;
            var sc = ComputeSource(s, s.SupplierId is long id ? supplier(id) : null, usdPerEur, share);
            list.Add(sc);
            unit += sc.UnitCost * share;
        }
        var warn = sources.All(s => s.PurchasePrice <= 0) ? CostWarning.NoPrice : CostWarning.None;
        return new BoughtCost(list, unit, warn);
    }

    static decimal? Size(MaterialInfo m, QtyUnit unit) => unit switch
    {
        QtyUnit.Cm => m.PieceLengthCm is > 0 ? m.PieceLengthCm : null,
        QtyUnit.Cm2 => m.PieceAreaCm2 is > 0 ? m.PieceAreaCm2 : null,
        QtyUnit.Kg => m.PieceWeightKg is > 0 ? m.PieceWeightKg : null,
        _ => 1m,
    };

    /// <summary>
    /// Cost of one <paramref name="target"/> unit of a material whose cost is known per its own unit.
    /// Converting goes through one piece, so it needs the piece length / area / weight. Null when it can't convert.
    /// </summary>
    public static decimal? CostPerUnit(MaterialInfo m, QtyUnit target)
    {
        if (target == m.Unit) return m.Cost;
        var from = Size(m, m.Unit);
        var to = Size(m, target);
        if (from is null || to is null) return null;
        var perPiece = m.Cost * from.Value;
        return perPiece / to.Value;
    }

    public static ProductCost ComputeProduct(IEnumerable<RecipeLine> lines, Func<long, MaterialInfo?> material,
        int laborSeconds, decimal laborRate, decimal overheadRate)
    {
        var list = new List<LineCost>();
        decimal materials = 0;
        var warn = CostWarning.None;
        foreach (var line in lines)
        {
            var m = line.MaterialId is long id ? material(id) : null;
            if (m is null)
            {
                warn |= CostWarning.MissingMaterial;
                list.Add(new LineCost(line, null, null, null));
                continue;
            }
            var unit = CostPerUnit(m, line.Unit);
            if (unit is null) warn |= CostWarning.NoConversion;
            var cost = unit * line.Quantity;
            materials += cost ?? 0;
            list.Add(new LineCost(line, m, unit, cost));
        }
        var hours = laborSeconds / 3600m;
        return new ProductCost(list, materials, laborSeconds, hours * laborRate, hours * overheadRate, warn);
    }

    public static decimal RoundPrice(decimal value, decimal step, RoundingMode mode)
    {
        if (step <= 0) return Math.Round(value, 4);
        var q = value / step;
        var n = mode switch
        {
            RoundingMode.Up => Math.Ceiling(Math.Round(q, 9)),
            RoundingMode.Down => Math.Floor(Math.Round(q, 9)),
            _ => Math.Round(q, MidpointRounding.AwayFromZero),
        };
        return n * step;
    }

    public static PriceResult Price(decimal cost, decimal markupPct, decimal step, RoundingMode mode)
    {
        var raw = cost * (1 + markupPct / 100m);
        return new PriceResult(cost, markupPct, step, raw, RoundPrice(raw, step, mode));
    }

    /// <summary>
    /// Markup % (two decimals) that gives <paramref name="target"/> as final price for an item costing <paramref name="cost"/>.
    /// Prefers the markup closest to the exact one that hits the target after rounding. Null when cost or target is 0.
    /// </summary>
    public static decimal? MarkupForPrice(decimal cost, decimal target, decimal step, RoundingMode mode)
    {
        if (cost <= 0 || target <= 0) return null;
        var exact = (target / cost - 1) * 100m;
        var basis = Math.Round(exact, 2);
        for (var i = 0; i <= 2000; i++)
        {
            foreach (var d in i == 0 ? new[] { 0 } : new[] { -i, i })
            {
                var m = basis + d / 100m;
                if (RoundPrice(cost * (1 + m / 100m), step, mode) == target) return m;
            }
        }
        return basis;
    }

    public static string FormatDuration(int seconds)
    {
        var m = seconds / 60;
        var s = seconds % 60;
        return $"{m}′ {s:00}″";
    }
}
