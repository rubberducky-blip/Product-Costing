using Dapper;
using Microsoft.Data.Sqlite;
using ProductCosting.Core.Calc;
using ProductCosting.Core.Model;

namespace ProductCosting.Core.Data;

public sealed partial class Store
{
    const string SupplierColumns = "id, name, folder_id, country, discount_pct, shipping_pct, vat_pct, currency, email, phone, notes";

    public Supplier? GetSupplier(long id) => Read(c => c.QuerySingleOrDefault<Supplier>($"SELECT {SupplierColumns} FROM suppliers WHERE id=@id", new { id }));

    public List<Supplier> GetSuppliers() => Read(c => c.Query<Supplier>($"SELECT {SupplierColumns} FROM suppliers").OrderBy(s => s.Name, NaturalComparer.Instance).ToList());

    const string SupplierRowSelect = """
        SELECT s.id, s.name, s.folder_id, s.position, s.country, s.discount_pct, s.shipping_pct, s.currency, s.email, s.phone,
               (SELECT count(DISTINCT item_id) FROM item_sources src WHERE src.supplier_id = s.id) AS item_count
        FROM suppliers s
        """;

    public List<SupplierRow> GetSupplierRows(long? folderId) => Read(c =>
    {
        var rows = c.Query<SupplierRow>($"{SupplierRowSelect} WHERE s.folder_id IS @folderId", new { folderId }).ToList();
        return GetSortMode(c, ListScope.Suppliers, folderId) == Model.SortMode.Manual
            ? rows.OrderBy(r => r.Position).ThenBy(r => r.Name, NaturalComparer.Instance).ToList()
            : rows.OrderBy(r => r.Name, NaturalComparer.Instance).ToList();
    });

    public List<SupplierRow> FilterSupplierRows(string text) => Read(c =>
        c.Query<SupplierRow>($"{SupplierRowSelect} WHERE s.list_key LIKE @p", new { p = TextNorm.LikePattern(text) })
            .OrderBy(r => r.Name, NaturalComparer.Instance).ToList());

    /// <summary>Saves a supplier. A change of discount, shipping or currency reprices every item bought from it.</summary>
    public long SaveSupplier(Supplier s) => Write(DataChange.Suppliers | DataChange.Items | DataChange.Notifications, (c, t) =>
    {
        s.Name = s.Name.Trim();
        var now = Now;
        Supplier? old = null;
        if (s.Id == 0)
        {
            var pos = NextPosition(c, t, "SELECT max(position) FROM suppliers WHERE folder_id IS @FolderId", s);
            s.Id = c.ExecuteScalar<long>("""
                INSERT INTO suppliers(name, folder_id, position, country, discount_pct, shipping_pct, vat_pct, currency, email, phone, notes, created_at, updated_at)
                VALUES(@Name, @FolderId, @pos, @Country, @DiscountPct, @ShippingPct, @VatPct, @Currency, @Email, @Phone, @Notes, @now, @now) RETURNING id
                """, new DynamicParameters(s).Also(p => { p.Add("pos", pos); p.Add("now", now); }), t);
        }
        else
        {
            old = c.QuerySingle<Supplier>($"SELECT {SupplierColumns} FROM suppliers WHERE id=@Id", s, t);
            c.Execute("""
                UPDATE suppliers SET name=@Name, folder_id=@FolderId, country=@Country, discount_pct=@DiscountPct, shipping_pct=@ShippingPct,
                    vat_pct=@VatPct, currency=@Currency, email=@Email, phone=@Phone, notes=@Notes, updated_at=@now WHERE id=@Id
                """, new DynamicParameters(s).Also(p => p.Add("now", now)), t);
            if (old.FolderId != s.FolderId)
                c.Execute("UPDATE suppliers SET position=@p WHERE id=@Id", new { p = NextPosition(c, t, "SELECT max(position) FROM suppliers WHERE folder_id IS @FolderId AND id<>@Id", s), s.Id }, t);
        }
        RefreshSupplierSearch(c, t, s);

        if (old is not null)
        {
            var items = c.Query<long>("SELECT DISTINCT item_id FROM item_sources WHERE supplier_id=@Id", s, t).ToList();
            if (old.Name != s.Name) RefreshItemSearch(c, t, items);
            if (old.DiscountPct != s.DiscountPct || old.ShippingPct != s.ShippingPct || old.Currency != s.Currency)
            {
                var req = new RecalcRequest();
                req.Items.UnionWith(items);
                new PricingEngine(this, c, t).Run(req);
            }
        }
        return s.Id;
    });

    /// <summary>Copies a supplier as "Name (2)". Its items stay with the original.</summary>
    public long DuplicateSupplier(long id)
    {
        var s = GetSupplier(id) ?? throw new InvalidOperationException("Supplier not found.");
        var names = GetSuppliers().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = s.Name;
        for (var n = 2; names.Contains(name); n++) name = $"{s.Name} ({n})";
        s.Id = 0;
        s.Name = name;
        return SaveSupplier(s);
    }

    // ---------- saved purchase orders ----------

    /// <summary>Saved orders of one supplier, or of every supplier when <paramref name="supplierId"/> is null; newest first.</summary>
    public List<PurchaseOrder> GetOrders(long? supplierId = null) => Read(c =>
    {
        var orders = c.Query<PurchaseOrder>($"""
            SELECT o.id, o.supplier_id, s.name AS supplier_name, s.currency AS supplier_currency, o.status, o.created_at, o.updated_at,
                   o.executed_at, o.archived_at, o.vat_pct
            FROM purchase_orders o JOIN suppliers s ON s.id = o.supplier_id
            {(supplierId is null ? "" : "WHERE o.supplier_id = @supplierId")}
            ORDER BY o.updated_at DESC
            """, new { supplierId }).ToList();
        if (orders.Count == 0) return orders;
        var usdPerEur = LoadSettings(c, null).UsdPerEur;
        var lines = c.Query<(long OrderId, double Quantity, double UnitPrice, int Currency)>($"""
            SELECT l.order_id, l.quantity, l.unit_price, l.currency FROM purchase_order_lines l
            {(supplierId is null ? "" : "JOIN purchase_orders o ON o.id = l.order_id WHERE o.supplier_id = @supplierId")}
            """, new { supplierId }).ToLookup(l => l.OrderId);
        foreach (var o in orders)
        {
            var ls = lines[o.Id].ToList();
            o.LineCount = ls.Count;
            o.TotalQuantity = ls.Sum(l => (decimal)l.Quantity);
            o.TotalValue = ls.Sum(l => Costing.Convert((decimal)l.Quantity * (decimal)l.UnitPrice, (Currency)l.Currency, o.SupplierCurrency, usdPerEur));
        }
        return orders;
    });

    public PurchaseOrder? GetOrder(long id) => GetOrders().FirstOrDefault(o => o.Id == id);

    public List<PurchaseOrderLine> GetOrderLines(long orderId) => Read(c => c.Query<PurchaseOrderLine>("""
        SELECT item_id, variant, code, supplier_code, name, unit, quantity, unit_price, currency, discount_pct, shipping_pct
        FROM purchase_order_lines WHERE order_id=@orderId ORDER BY position
        """, new { orderId }).ToList());

    /// <summary>Saves a draft order (new when <paramref name="orderId"/> is null) and returns its id. Executed orders don't change.</summary>
    public long SaveOrder(long? orderId, long supplierId, IReadOnlyList<PurchaseOrderLine> lines) => Write(DataChange.Suppliers, (c, t) =>
    {
        var now = Now;
        var vat = c.ExecuteScalar<double>("SELECT vat_pct FROM suppliers WHERE id=@supplierId", new { supplierId }, t);
        long id;
        if (orderId is long existing && c.ExecuteScalar<int?>("SELECT status FROM purchase_orders WHERE id=@existing", new { existing }, t) is int status)
        {
            if (status != (int)OrderStatus.Draft) throw new InvalidOperationException("Only draft orders can be changed.");
            id = existing;
            c.Execute("UPDATE purchase_orders SET updated_at=@now, vat_pct=@vat WHERE id=@id; DELETE FROM purchase_order_lines WHERE order_id=@id", new { now, vat, id }, t);
        }
        else
            id = c.ExecuteScalar<long>("INSERT INTO purchase_orders(supplier_id, created_at, updated_at, vat_pct) VALUES(@supplierId, @now, @now, @vat) RETURNING id",
                new { supplierId, now, vat }, t);
        var i = 0;
        foreach (var l in lines)
            c.Execute("""
                INSERT INTO purchase_order_lines(order_id, item_id, variant, code, supplier_code, name, unit, quantity, unit_price, currency,
                    discount_pct, shipping_pct, position)
                VALUES(@id, @ItemId, @Variant, @Code, @SupplierCode, @Name, @Unit, @Quantity, @UnitPrice, @Currency, @DiscountPct, @ShippingPct, @pos)
                """, new DynamicParameters(l).Also(p => { p.Add("id", id); p.Add("pos", i++); }), t);
        return id;
    });

    /// <summary>Moves an order on: draft → executed → archived (and back from archived to executed).</summary>
    public void SetOrderStatus(long id, OrderStatus status) => Write(DataChange.Suppliers, (c, t) =>
    {
        var now = Now;
        c.Execute(status switch
        {
            OrderStatus.Executed => "UPDATE purchase_orders SET status=1, executed_at=COALESCE(executed_at, @now), archived_at=NULL, updated_at=@now WHERE id=@id",
            OrderStatus.Archived => "UPDATE purchase_orders SET status=2, archived_at=@now, updated_at=@now WHERE id=@id AND status=1",
            _ => "UPDATE purchase_orders SET status=0, executed_at=NULL, archived_at=NULL, updated_at=@now WHERE id=@id",
        }, new { id, now }, t);
    });

    public void DeleteOrder(long id) => Write(DataChange.Suppliers, (c, t) =>
        c.Execute("DELETE FROM purchase_orders WHERE id=@id", new { id }, t));

    // ---------- supplier price changes ----------

    /// <summary>
    /// Records purchase-price changes of an item's suppliers (one row per item, supplier and day,
    /// removed again when the price is back to the day's starting price).
    /// </summary>
    internal void RecordSupplierPrices(SqliteConnection c, SqliteTransaction t, long itemId,
        IEnumerable<(long SupplierId, decimal Price)> before, IEnumerable<(long SupplierId, decimal Price)> after)
    {
        var old = before.GroupBy(x => x.SupplierId).ToDictionary(g => g.Key, g => g.First().Price);
        foreach (var (sid, price) in after.GroupBy(x => x.SupplierId).Select(g => g.First()))
        {
            if (!old.TryGetValue(sid, out var was) || was == price) continue;
            c.Execute("""
                INSERT INTO supplier_price_history(item_id, supplier_id, day, old_price, price) VALUES(@itemId, @sid, @day, @was, @price)
                ON CONFLICT(item_id, supplier_id, day) DO UPDATE SET price = excluded.price;
                DELETE FROM supplier_price_history WHERE item_id=@itemId AND supplier_id=@sid AND day=@day AND abs(price - old_price) < 0.0000001;
                """, new { itemId, sid, day = Today, was = (double)was, price = (double)price }, t);
        }
    }

    /// <summary>Per day: how many of the supplier's items changed purchase price, and the average change.</summary>
    public List<SupplierChangeDay> GetSupplierHistory(long supplierId) => Read(c =>
    {
        var total = c.ExecuteScalar<int>("SELECT count(DISTINCT item_id) FROM item_sources WHERE supplier_id=@supplierId", new { supplierId });
        return c.Query<(string Day, double Old, double Price)>(
                "SELECT day, old_price, price FROM supplier_price_history WHERE supplier_id=@supplierId ORDER BY day", new { supplierId })
            .GroupBy(r => r.Day)
            .Select(g => new SupplierChangeDay
            {
                Day = g.Key,
                Changed = g.Count(),
                Total = Math.Max(total, g.Count()),
                Increased = g.Count(r => r.Price > r.Old),
                Decreased = g.Count(r => r.Price < r.Old),
                AvgChangePct = (decimal)g.Where(r => r.Old > 0).Select(r => (r.Price - r.Old) / r.Old * 100).DefaultIfEmpty(0).Average(),
            }).ToList();
    });

    public void DeleteSupplier(long id) => Write(DataChange.Suppliers | DataChange.Items | DataChange.Notifications, (c, t) =>
    {
        var items = c.Query<long>("SELECT DISTINCT item_id FROM item_sources WHERE supplier_id=@id", new { id }, t).ToList();
        c.Execute("DELETE FROM search_index WHERE rowid=@r; DELETE FROM suppliers WHERE id=@id", new { id, r = id * 2 + 1 }, t);
        RefreshItemSearch(c, t, items);
        var req = new RecalcRequest();
        req.Items.UnionWith(items);
        new PricingEngine(this, c, t).Run(req);
    });

    static void RefreshSupplierSearch(SqliteConnection c, SqliteTransaction t, Supplier s)
    {
        var key = TextNorm.Join(s.Name, Localization.Countries.SearchText(s.Country), s.Email, s.Phone);
        var body = TextNorm.Join(key, s.Notes);
        c.Execute("UPDATE suppliers SET list_key=@key WHERE id=@Id", new { key, s.Id }, t);
        c.Execute("DELETE FROM search_index WHERE rowid=@r; INSERT INTO search_index(rowid, body) VALUES(@r, @body);", new { r = s.Id * 2 + 1, body }, t);
    }

    /// <summary>Items bought from a supplier (one row per item), for the supplier page and the order form.</summary>
    public List<SupplierItemRow> GetSupplierItems(long supplierId) => Read(c => c.Query<SupplierItemRow>("""
        SELECT i.id AS item_id, i.kind, i.code, i.name, src.supplier_code, src.purchase_price, src.purchase_qty, i.unit,
               COALESCE(src.currency, s.currency, 0) AS currency, src.weight, src.moq, src.pack_qty,
               COALESCE(src.discount_pct, s.discount_pct) AS discount_pct, COALESCE(src.shipping_pct, s.shipping_pct) AS shipping_pct
        FROM item_sources src JOIN items i ON i.id = src.item_id JOIN suppliers s ON s.id = src.supplier_id
        WHERE src.supplier_id = @supplierId
        GROUP BY i.id
        """, new { supplierId }).OrderBy(r => r.Code, NaturalComparer.Instance).ToList());

    public Dictionary<long, List<ItemVariant>> GetVariants(IEnumerable<long> itemIds) => Read(c =>
    {
        var ids = itemIds.ToList();
        if (ids.Count == 0) return [];
        return c.Query<ItemVariant>("SELECT id, item_id, name, code, supplier_id FROM item_variants WHERE item_id IN @ids ORDER BY item_id, position", new { ids })
            .GroupBy(v => v.ItemId).ToDictionary(g => g.Key, g => g.ToList());
    });
}
