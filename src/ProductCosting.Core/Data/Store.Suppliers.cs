using Dapper;
using Microsoft.Data.Sqlite;
using ProductCosting.Core.Calc;
using ProductCosting.Core.Model;

namespace ProductCosting.Core.Data;

public sealed partial class Store
{
    const string SupplierColumns = "id, name, folder_id, country, discount_pct, shipping_pct, currency, email, phone, notes";

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
                INSERT INTO suppliers(name, folder_id, position, country, discount_pct, shipping_pct, currency, email, phone, notes, created_at, updated_at)
                VALUES(@Name, @FolderId, @pos, @Country, @DiscountPct, @ShippingPct, @Currency, @Email, @Phone, @Notes, @now, @now) RETURNING id
                """, new DynamicParameters(s).Also(p => { p.Add("pos", pos); p.Add("now", now); }), t);
        }
        else
        {
            old = c.QuerySingle<Supplier>($"SELECT {SupplierColumns} FROM suppliers WHERE id=@Id", s, t);
            c.Execute("""
                UPDATE suppliers SET name=@Name, folder_id=@FolderId, country=@Country, discount_pct=@DiscountPct, shipping_pct=@ShippingPct,
                    currency=@Currency, email=@Email, phone=@Phone, notes=@Notes, updated_at=@now WHERE id=@Id
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
               COALESCE(src.currency, s.currency, 0) AS currency, src.weight
        FROM item_sources src JOIN items i ON i.id = src.item_id JOIN suppliers s ON s.id = src.supplier_id
        WHERE src.supplier_id = @supplierId
        GROUP BY i.id
        """, new { supplierId }).OrderBy(r => r.Code, NaturalComparer.Instance).ToList());

    public Dictionary<long, List<ItemVariant>> GetVariants(IEnumerable<long> itemIds) => Read(c =>
    {
        var ids = itemIds.ToList();
        if (ids.Count == 0) return [];
        return c.Query<ItemVariant>("SELECT id, item_id, name, code FROM item_variants WHERE item_id IN @ids ORDER BY item_id, position", new { ids })
            .GroupBy(v => v.ItemId).ToDictionary(g => g.Key, g => g.ToList());
    });
}
