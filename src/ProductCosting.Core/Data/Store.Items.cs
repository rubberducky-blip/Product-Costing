using Dapper;
using Microsoft.Data.Sqlite;
using ProductCosting.Core.Calc;
using ProductCosting.Core.Model;

namespace ProductCosting.Core.Data;

public sealed partial class Store
{
    const string ItemColumns = """
        id, kind, code, name, usable_as_material, folder_id, material_folder_id, unit, piece_length_cm, piece_area_cm2,
        piece_weight_kg, markup_pct, rounding_step, notes, cost, cost_materials, cost_labor, cost_overhead, labor_seconds,
        eff_markup, eff_step, price, warnings, has_unseen_change
        """;

    public Item? GetItem(long id) => Read(c => c.QuerySingleOrDefault<Item>($"SELECT {ItemColumns} FROM items WHERE id=@id", new { id }));

    public ItemDocument? LoadItem(long id) => Read(c =>
    {
        var item = c.QuerySingleOrDefault<Item>($"SELECT {ItemColumns} FROM items WHERE id=@id", new { id });
        if (item is null) return null;
        return new ItemDocument
        {
            Item = item,
            Sources = c.Query<ItemSource>("SELECT id, item_id, supplier_id, supplier_code, purchase_price, purchase_qty, currency, discount_pct, shipping_pct, weight FROM item_sources WHERE item_id=@id ORDER BY position", new { id }).ToList(),
            Lines = c.Query<RecipeLine>("SELECT id, product_id, material_id, quantity, unit, comment FROM recipe_lines WHERE product_id=@id ORDER BY position", new { id }).ToList(),
            Labor = c.Query<LaborEntry>("SELECT id, product_id, seconds, comment FROM labor_entries WHERE product_id=@id ORDER BY position", new { id }).ToList(),
            Variants = c.Query<ItemVariant>("SELECT id, item_id, name, code FROM item_variants WHERE item_id=@id ORDER BY position", new { id }).ToList(),
        };
    });

    public bool CodeExists(string code, long exceptId) =>
        Read(c => c.ExecuteScalar<long>("SELECT count(*) FROM items WHERE code = @code COLLATE NOCASE AND id <> @exceptId", new { code = code.Trim(), exceptId }) > 0);

    /// <summary>Inserts or updates an item with its sources, recipe, labor and variants, then reprices it and what depends on it.</summary>
    public long SaveItem(ItemDocument doc) => Write(DataChange.Items | DataChange.Notifications, (c, t) =>
    {
        var it = doc.Item;
        it.Code = it.Code.Trim();
        it.Name = it.Name.Trim();
        if (c.ExecuteScalar<long>("SELECT count(*) FROM items WHERE code = @Code COLLATE NOCASE AND id <> @Id", it, t) > 0)
            throw new DuplicateCodeException(it.Code);
        if (it.Kind != ItemKind.Merchandise) it.UsableAsMaterial = false;
        if (!it.UsableAsMaterial) it.MaterialFolderId = null;

        var created = it.Id == 0;
        var now = Now;
        var args = new
        {
            it.Id, it.Kind, it.Code, it.Name, it.UsableAsMaterial, it.FolderId, it.MaterialFolderId, it.Unit,
            it.PieceLengthCm, it.PieceAreaCm2, it.PieceWeightKg, it.MarkupPct, it.RoundingStep,
            Notes = string.IsNullOrWhiteSpace(it.Notes) ? null : it.Notes, Now = now,
        };
        if (created)
        {
            var pos = NextPosition(c, t, "SELECT max(position) FROM items WHERE kind=@Kind AND folder_id IS @FolderId", it);
            var mpos = NextPosition(c, t, "SELECT max(material_position) FROM items WHERE material_folder_id IS @MaterialFolderId", it);
            it.Id = c.ExecuteScalar<long>("""
                INSERT INTO items(kind, code, name, usable_as_material, folder_id, material_folder_id, unit, piece_length_cm,
                    piece_area_cm2, piece_weight_kg, markup_pct, rounding_step, notes, position, material_position, created_at, updated_at)
                VALUES(@Kind, @Code, @Name, @UsableAsMaterial, @FolderId, @MaterialFolderId, @Unit, @PieceLengthCm,
                    @PieceAreaCm2, @PieceWeightKg, @MarkupPct, @RoundingStep, @Notes, @pos, @mpos, @Now, @Now)
                RETURNING id
                """, new DynamicParameters(args).Also(p => { p.Add("pos", pos); p.Add("mpos", mpos); }), t);
        }
        else
        {
            var oldMatFolder = c.ExecuteScalar<long?>("SELECT material_folder_id FROM items WHERE id=@Id", it, t);
            var oldFolder = c.ExecuteScalar<long?>("SELECT folder_id FROM items WHERE id=@Id", it, t);
            c.Execute("""
                UPDATE items SET code=@Code, name=@Name, usable_as_material=@UsableAsMaterial, folder_id=@FolderId,
                    material_folder_id=@MaterialFolderId, unit=@Unit, piece_length_cm=@PieceLengthCm, piece_area_cm2=@PieceAreaCm2,
                    piece_weight_kg=@PieceWeightKg, markup_pct=@MarkupPct, rounding_step=@RoundingStep, notes=@Notes, updated_at=@Now
                WHERE id=@Id
                """, args, t);
            if (oldFolder != it.FolderId)
                c.Execute("UPDATE items SET position=@p WHERE id=@Id", new { p = NextPosition(c, t, "SELECT max(position) FROM items WHERE kind=@Kind AND folder_id IS @FolderId AND id<>@Id", it), it.Id }, t);
            if (oldMatFolder != it.MaterialFolderId)
                c.Execute("UPDATE items SET material_position=@p WHERE id=@Id", new { p = NextPosition(c, t, "SELECT max(material_position) FROM items WHERE material_folder_id IS @MaterialFolderId AND id<>@Id", it), it.Id }, t);
            c.Execute("DELETE FROM item_sources WHERE item_id=@Id; DELETE FROM recipe_lines WHERE product_id=@Id; DELETE FROM labor_entries WHERE product_id=@Id; DELETE FROM item_variants WHERE item_id=@Id;", it, t);
        }

        var id = it.Id;
        if (it.IsBought)
        {
            var i = 0;
            foreach (var s in doc.Sources)
            {
                s.ItemId = id;
                c.Execute("""
                    INSERT INTO item_sources(item_id, supplier_id, supplier_code, purchase_price, purchase_qty, currency, discount_pct, shipping_pct, weight, position)
                    VALUES(@ItemId, @SupplierId, @SupplierCode, @PurchasePrice, @PurchaseQty, @Currency, @DiscountPct, @ShippingPct, @Weight, @pos)
                    """, new DynamicParameters(s).Also(p => p.Add("pos", i++)), t);
            }
            i = 0;
            foreach (var v in doc.Variants.Where(v => !string.IsNullOrWhiteSpace(v.Name) || !string.IsNullOrWhiteSpace(v.Code)))
                c.Execute("INSERT INTO item_variants(item_id, name, code, position) VALUES(@id, @Name, @Code, @pos)",
                    new { id, Name = v.Name.Trim(), Code = v.Code?.Trim(), pos = i++ }, t);
        }
        else
        {
            var i = 0;
            foreach (var l in doc.Lines)
                c.Execute("INSERT INTO recipe_lines(product_id, material_id, quantity, unit, comment, position) VALUES(@id, @MaterialId, @Quantity, @Unit, @Comment, @pos)",
                    new { id, l.MaterialId, l.Quantity, l.Unit, l.Comment, pos = i++ }, t);
            i = 0;
            foreach (var l in doc.Labor)
                c.Execute("INSERT INTO labor_entries(product_id, seconds, comment, position) VALUES(@id, @Seconds, @Comment, @pos)",
                    new { id, l.Seconds, l.Comment, pos = i++ }, t);
        }

        RefreshItemSearch(c, t, [id]);

        var req = new RecalcRequest();
        req.Items.Add(id);
        req.Direct.Add(id);
        if (created) req.Created.Add(id);
        // unit or piece size changes don't change the material's own cost, but do change the recipes using it
        req.Items.UnionWith(c.Query<long>("SELECT DISTINCT product_id FROM recipe_lines WHERE material_id=@id", new { id }, t));
        new PricingEngine(this, c, t).Run(req);
        return id;
    });

    public void DeleteItem(long id) => Write(DataChange.Items | DataChange.Notifications, (c, t) =>
    {
        var users = c.Query<long>("SELECT DISTINCT product_id FROM recipe_lines WHERE material_id=@id", new { id }, t).ToList();
        c.Execute("DELETE FROM search_index WHERE rowid=@r; DELETE FROM items WHERE id=@id", new { id, r = id * 2 }, t);
        if (users.Count == 0) return;
        var req = new RecalcRequest();
        req.Items.UnionWith(users);
        new PricingEngine(this, c, t).Run(req);
    });

    public int CountUsage(long materialId) =>
        Read(c => c.ExecuteScalar<int>("SELECT count(DISTINCT product_id) FROM recipe_lines WHERE material_id=@materialId", new { materialId }));

    public List<UsageRow> GetUsage(long materialId) => Read(c => c.Query<UsageRow>("""
        SELECT p.id AS product_id, p.code, p.name, SUM(rl.quantity) AS quantity, rl.unit
        FROM recipe_lines rl JOIN items p ON p.id = rl.product_id
        WHERE rl.material_id = @materialId
        GROUP BY p.id, rl.unit
        """, new { materialId }).OrderBy(r => r.Code, NaturalComparer.Instance).ToList());

    /// <summary>Every raw material plus merchandise used as raw material, with what is needed to cost a recipe line.</summary>
    public List<(MaterialInfo Info, long? FolderId)> GetMaterials() => Read(c => c.Query<MaterialInfo, long?, (MaterialInfo, long?)>("""
        SELECT id, kind, code, name, unit, piece_length_cm, piece_area_cm2, piece_weight_kg, cost,
               CASE WHEN kind = 3 THEN folder_id ELSE material_folder_id END AS folder
        FROM items WHERE kind = 3 OR (kind = 2 AND usable_as_material = 1)
        """, (m, f) => (m, f), splitOn: "folder").OrderBy(x => x.Item1.Code, NaturalComparer.Instance).ToList());

    public MaterialInfo? GetMaterial(long id) => Read(c => c.QuerySingleOrDefault<MaterialInfo>(
        "SELECT id, kind, code, name, unit, piece_length_cm, piece_area_cm2, piece_weight_kg, cost FROM items WHERE id=@id", new { id }));

    public List<PriceHistoryEntry> GetHistory(long itemId) => Read(c => c.Query<PriceHistoryEntry>(
        "SELECT item_id, day, old_price, price, reasons FROM price_history WHERE item_id=@itemId ORDER BY day", new { itemId }).ToList());

    // ---------- lists ----------

    const string RowSelect = """
        SELECT i.id, i.kind, i.code, i.name, {0} AS folder_id, {1} AS position, i.unit, i.cost, i.price, i.warnings,
               i.has_unseen_change, i.usable_as_material,
               (SELECT group_concat(name, ', ') FROM (SELECT DISTINCT s.name FROM item_sources src JOIN suppliers s ON s.id = src.supplier_id WHERE src.item_id = i.id)) AS suppliers,
               (SELECT src.supplier_code FROM item_sources src WHERE src.item_id = i.id ORDER BY src.position LIMIT 1) AS supplier_code
        FROM items i
        """;

    static string RowsSql(ListScope scope, string where)
    {
        var normal = string.Format(RowSelect, "i.folder_id", "i.position");
        if (scope != ListScope.RawMaterials)
            return $"{normal} WHERE i.kind = {(int)scope} AND {where.Replace("{folder}", "i.folder_id")}";
        var merch = string.Format(RowSelect, "i.material_folder_id", "i.material_position");
        return $"""
            {normal} WHERE i.kind = 3 AND {where.Replace("{folder}", "i.folder_id")}
            UNION ALL
            {merch} WHERE i.kind = 2 AND i.usable_as_material = 1 AND {where.Replace("{folder}", "i.material_folder_id")}
            """;
    }

    /// <summary>Rows directly inside one folder (null = top level), in the folder's sort order.</summary>
    public List<ItemRow> GetItemRows(ListScope scope, long? folderId) => Read(c =>
    {
        var rows = c.Query<ItemRow>(RowsSql(scope, "{folder} IS @folderId"), new { folderId }).ToList();
        return GetSortMode(c, scope, folderId) == Model.SortMode.Manual
            ? rows.OrderBy(r => r.Position).ThenBy(r => r.Code, NaturalComparer.Instance).ToList()
            : rows.OrderBy(r => r.Code, NaturalComparer.Instance).ToList();
    });

    /// <summary>Rows anywhere in the list whose code, name, supplier or supplier code contains the text.</summary>
    public List<ItemRow> FilterItemRows(ListScope scope, string text) => Read(c =>
        c.Query<ItemRow>(RowsSql(scope, "i.list_key LIKE @p"), new { p = TextNorm.LikePattern(text) })
            .OrderBy(r => r.Code, NaturalComparer.Instance).ToList());

    /// <summary>Moves rows into a folder (null = top level), appended at the end.</summary>
    public void MoveItems(ListScope scope, IReadOnlyList<long> ids, long? folderId) => Write(DataChange.Items, (c, t) =>
    {
        if (scope == ListScope.Suppliers)
        {
            var pos = NextPosition(c, t, "SELECT max(position) FROM suppliers WHERE folder_id IS @folderId", new { folderId });
            foreach (var id in ids) c.Execute("UPDATE suppliers SET folder_id=@folderId, position=@p WHERE id=@id", new { folderId, p = pos++, id }, t);
            return;
        }
        var p1 = NextPosition(c, t, "SELECT max(position) FROM items WHERE kind=@k AND folder_id IS @folderId", new { k = (int)scope, folderId });
        var p2 = NextPosition(c, t, "SELECT max(material_position) FROM items WHERE material_folder_id IS @folderId", new { folderId });
        foreach (var id in ids)
        {
            var kind = (ItemKind)c.ExecuteScalar<int>("SELECT kind FROM items WHERE id=@id", new { id }, t);
            if (scope == ListScope.RawMaterials && kind == ItemKind.Merchandise)
                c.Execute("UPDATE items SET material_folder_id=@folderId, material_position=@p WHERE id=@id", new { folderId, p = p2++, id }, t);
            else
                c.Execute("UPDATE items SET folder_id=@folderId, position=@p WHERE id=@id", new { folderId, p = p1++, id }, t);
        }
    });

    /// <summary>Stores a manual order: <paramref name="orderedIds"/> is the full list of rows in the folder.</summary>
    public void ReorderRows(ListScope scope, long? folderId, IReadOnlyList<long> orderedIds) => Write(DataChange.Items | DataChange.Suppliers, (c, t) =>
        WritePositions(c, t, scope, orderedIds));

    static void WritePositions(SqliteConnection c, SqliteTransaction t, ListScope scope, IReadOnlyList<long> orderedIds)
    {
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var id = orderedIds[i];
            if (scope == ListScope.Suppliers)
                c.Execute("UPDATE suppliers SET position=@i WHERE id=@id", new { i, id }, t);
            else if (scope == ListScope.RawMaterials)
                c.Execute("UPDATE items SET material_position = CASE WHEN kind=2 THEN @i ELSE material_position END, position = CASE WHEN kind=3 THEN @i ELSE position END WHERE id=@id", new { i, id }, t);
            else
                c.Execute("UPDATE items SET position=@i WHERE id=@id", new { i, id }, t);
        }
    }

    // ---------- search text ----------

    internal static void RefreshItemSearch(SqliteConnection c, SqliteTransaction t, IEnumerable<long> ids)
    {
        foreach (var id in ids)
        {
            var it = c.QuerySingleOrDefault<(string Code, string Name, string? Notes)>("SELECT code, name, notes FROM items WHERE id=@id", new { id }, t);
            if (it.Code is null) continue;
            var src = c.Query<(string? SupplierCode, string? Supplier)>(
                "SELECT src.supplier_code, s.name FROM item_sources src LEFT JOIN suppliers s ON s.id = src.supplier_id WHERE src.item_id=@id", new { id }, t).ToList();
            var variants = c.Query<(string Name, string? Code)>("SELECT name, code FROM item_variants WHERE item_id=@id", new { id }, t).ToList();
            var comments = c.Query<string>("""
                SELECT comment FROM recipe_lines WHERE product_id=@id AND comment IS NOT NULL
                UNION ALL SELECT comment FROM labor_entries WHERE product_id=@id AND comment IS NOT NULL
                """, new { id }, t).ToList();

            var key = TextNorm.Join([it.Code, it.Name, .. src.Select(s => s.SupplierCode), .. src.Select(s => s.Supplier), .. variants.Select(v => v.Code)]);
            var body = TextNorm.Join([key, it.Notes, .. variants.Select(v => v.Name), .. comments]);
            c.Execute("UPDATE items SET list_key=@key WHERE id=@id", new { key, id }, t);
            c.Execute("DELETE FROM search_index WHERE rowid=@r; INSERT INTO search_index(rowid, body) VALUES(@r, @body);", new { r = id * 2, body }, t);
        }
    }
}

static class DapperExtensions
{
    public static DynamicParameters Also(this DynamicParameters p, Action<DynamicParameters> a)
    {
        a(p);
        return p;
    }
}
