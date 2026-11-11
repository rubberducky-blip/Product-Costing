namespace ProductCosting.Core.Calc;

/// <summary>An item wanted, and who sells it at what final cost (per base unit, in euro, discount and shipping included).</summary>
public sealed record PlanItem(long ItemId, decimal Quantity, IReadOnlyList<PlanOffer> Offers);

public sealed record PlanOffer(long SupplierId, decimal UnitCost);

public enum PlanMode
{
    /// <summary>Each item from the supplier with the lowest cost.</summary>
    Cheapest,
    /// <summary>As few suppliers as possible; among those, each item from the cheapest one that has it.</summary>
    FewestOrders,
}

/// <summary>Splits a list of wanted items into orders per supplier.</summary>
public static class OrderPlanner
{
    /// <returns>Supplier → its items (item, quantity); and the items no (allowed) supplier sells.</returns>
    public static (Dictionary<long, List<(long ItemId, decimal Quantity)>> Orders, List<long> Unplaced) Plan(IReadOnlyList<PlanItem> items, PlanMode mode)
    {
        var orders = new Dictionary<long, List<(long, decimal)>>();
        var unplaced = items.Where(i => i.Offers.Count == 0).Select(i => i.ItemId).ToList();
        var placeable = items.Where(i => i.Offers.Count > 0).ToList();

        HashSet<long>? chosen = null;
        if (mode == PlanMode.FewestOrders)
        {
            // greedy set cover: the supplier with most of the items still open, then the cheaper for them; until all are covered
            chosen = [];
            var open = placeable.ToList();
            while (open.Count > 0)
            {
                var best = open.SelectMany(i => i.Offers.Select(o => (o.SupplierId, Cost: o.UnitCost * i.Quantity)))
                    .GroupBy(x => x.SupplierId)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Sum(x => x.Cost))
                    .ThenBy(g => g.Key)
                    .First().Key;
                chosen.Add(best);
                open.RemoveAll(i => i.Offers.Any(o => o.SupplierId == best));
            }
        }

        foreach (var item in placeable)
        {
            var offer = item.Offers.Where(o => chosen is null || chosen.Contains(o.SupplierId))
                .OrderBy(o => o.UnitCost).ThenBy(o => o.SupplierId).First();
            if (!orders.TryGetValue(offer.SupplierId, out var list)) orders[offer.SupplierId] = list = [];
            list.Add((item.ItemId, item.Quantity));
        }
        return (orders, unplaced);
    }
}
