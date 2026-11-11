using Dapper;
using ProductCosting.Core.Calc;
using ProductCosting.Core.Localization;
using ProductCosting.Core.Model;

namespace ProductCosting.Core.Data;

/// <summary>An item on the shopping list: what is wanted, before deciding who to order it from.</summary>
public sealed class WishItem
{
    public long ItemId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public ItemKind Kind { get; set; }
    public QtyUnit Unit { get; set; }
    public MeasureUnit? PriceUnit { get; set; }
    /// <summary>In the base unit (cm, cm², kg, pieces).</summary>
    public decimal Quantity { get; set; }
    public string AddedAt { get; set; } = "";
    /// <summary>Its suppliers, for the list.</summary>
    public string? Suppliers { get; set; }

    /// <summary>The unit the list shows and takes the quantity in: the one the item is bought in.</summary>
    public MeasureUnit ShownUnit => Unit.EntryUnit(PriceUnit);
}

/// <summary>Which suppliers the orders made from the shopping list may use.</summary>
public enum SupplierRegion { Any = 0, Greece = 1, Europe = 2 }

public sealed record WishOrdersResult(int Created, int Extended, int Items, List<long> Unplaced);

public sealed partial class Store
{
    public List<WishItem> GetWishList() => Read(c => c.Query<WishItem>("""
        SELECT w.item_id, i.code, i.name, i.kind, i.unit, i.price_unit, w.quantity, w.added_at,
               (SELECT group_concat(name, ', ') FROM (SELECT DISTINCT s.name FROM item_sources src JOIN suppliers s ON s.id = src.supplier_id
                 WHERE src.item_id = w.item_id ORDER BY s.name)) AS suppliers
        FROM wish_list w JOIN items i ON i.id = w.item_id
        ORDER BY w.added_at, i.code
        """).ToList());

    /// <summary>The wanted quantities by item (base unit), e.g. for the "take from the list" buttons of an order.</summary>
    public Dictionary<long, decimal> GetWishQuantities() => Read(c =>
        c.Query<(long ItemId, double Quantity)>("SELECT item_id, quantity FROM wish_list").ToDictionary(x => x.ItemId, x => (decimal)x.Quantity));

    /// <summary>Adds an item to the list, or changes its quantity (base unit); 0 or less takes it off.</summary>
    public void SetWish(long itemId, decimal quantity) => Write(DataChange.Wish, (c, t) =>
    {
        if (quantity <= 0) c.Execute("DELETE FROM wish_list WHERE item_id=@itemId", new { itemId }, t);
        else c.Execute("""
            INSERT INTO wish_list(item_id, quantity, added_at) VALUES(@itemId, @quantity, @now)
            ON CONFLICT(item_id) DO UPDATE SET quantity = excluded.quantity
            """, new { itemId, quantity = (double)quantity, now = Now }, t);
    });

    public void RemoveWish(IEnumerable<long> itemIds) => Write(DataChange.Wish, (c, t) =>
    {
        foreach (var id in itemIds) c.Execute("DELETE FROM wish_list WHERE item_id=@id", new { id }, t);
    });

    /// <summary>
    /// Turns the chosen items of the shopping list into draft orders: per supplier (the cheapest, or as few suppliers as
    /// possible), only suppliers of <paramref name="region"/>. A supplier's existing draft gets the items rather than a
    /// second draft. The items placed leave the list; the others stay.
    /// </summary>
    public WishOrdersResult OrderWishes(IReadOnlyCollection<long> itemIds, PlanMode mode, SupplierRegion region)
    {
        var wanted = GetWishList().Where(w => itemIds.Contains(w.ItemId)).ToList();
        var suppliers = GetSuppliers().ToDictionary(s => s.Id);
        bool Allowed(long supplierId) => suppliers.TryGetValue(supplierId, out var s) && region switch
        {
            SupplierRegion.Greece => string.Equals(s.Country?.Trim(), "GR", StringComparison.OrdinalIgnoreCase),
            SupplierRegion.Europe => s.Country is { } code && Countries.Europe.Contains(code.Trim()),
            _ => true,
        };
        var costs = GetSupplierCosts(wanted.Select(w => w.ItemId));
        var plan = OrderPlanner.Plan(wanted.Select(w => new PlanItem(w.ItemId, w.Quantity,
            (costs.GetValueOrDefault(w.ItemId) ?? []).Where(x => Allowed(x.SupplierId)).Select(x => new PlanOffer(x.SupplierId, x.UnitCostEur)).ToList())).ToList(), mode);

        int created = 0, extended = 0, placed = 0;
        foreach (var (supplierId, items) in plan.Orders)
        {
            var rows = GetSupplierItems(supplierId).ToDictionary(r => r.ItemId);
            var draft = GetOrders(supplierId).Where(o => o.Status == OrderStatus.Draft).OrderByDescending(o => o.UpdatedAt).FirstOrDefault();
            var lines = draft is null ? [] : GetOrderLines(draft.Id);
            foreach (var (itemId, baseQty) in items)
            {
                if (!rows.TryGetValue(itemId, out var r)) continue;
                var qty = r.EntryUnit.FromBase(baseQty);
                // the same item already on the draft (without a variation): the quantities add up
                var same = lines.FirstOrDefault(l => l.ItemId == itemId && l.Variant is null);
                if (same is not null) same.Quantity += same.ShownUnit == r.EntryUnit ? qty : same.ShownUnit.FromBase(baseQty);
                else lines.Add(new PurchaseOrderLine
                {
                    ItemId = itemId, Code = r.Code, SupplierCode = r.SupplierCode, Name = r.Name, Unit = r.Unit, EntryUnit = r.EntryUnit,
                    Quantity = qty, UnitPrice = r.EntryUnitPrice, Currency = r.Currency, DiscountPct = r.DiscountPct, ShippingPct = r.ShippingPct,
                });
                placed++;
            }
            SaveOrder(draft?.Id, supplierId, lines);
            if (draft is null) created++; else extended++;
        }
        RemoveWish(plan.Orders.Values.SelectMany(l => l.Select(x => x.ItemId)));
        return new WishOrdersResult(created, extended, placed, plan.Unplaced);
    }

    /// <summary>For each item, the other open orders (draft or executed) it is already on.</summary>
    public Dictionary<long, List<string>> GetOpenOrderMentions(long? exceptOrderId) => Read(c =>
        c.Query<(long ItemId, string Supplier, int Status, double Quantity, int Unit, int? EntryUnit)>("""
            SELECT l.item_id, s.name, o.status, l.quantity, l.unit, l.entry_unit
            FROM purchase_order_lines l JOIN purchase_orders o ON o.id = l.order_id JOIN suppliers s ON s.id = o.supplier_id
            WHERE o.status IN (0, 1) AND l.item_id IS NOT NULL AND o.id IS NOT @exceptOrderId
            """, new { exceptOrderId })
        .GroupBy(x => x.ItemId)
        .ToDictionary(g => g.Key, g => g.Select(x =>
            $"{x.Supplier} · {Strings.Get(x.Status == 0 ? "order.status.Draft" : "order.status.Executed")} · " +
            $"{((decimal)x.Quantity).ToString("0.###", System.Globalization.CultureInfo.CurrentCulture)} {Strings.Measure(((QtyUnit)x.Unit).EntryUnit((MeasureUnit?)x.EntryUnit))}").ToList()));
}
