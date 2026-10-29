using Dapper;
using Microsoft.Data.Sqlite;
using ProductCosting.Core.Calc;
using ProductCosting.Core.Model;

namespace ProductCosting.Core.Data;

internal sealed class RecalcRequest
{
    /// <summary>Items to recompute (any kind). Products using a material whose cost changes are added automatically.</summary>
    public HashSet<long> Items { get; } = [];
    /// <summary>Items the user edited: their price change goes to history, but raises no notification.</summary>
    public HashSet<long> Direct { get; } = [];
    public HashSet<long> Created { get; } = [];
    public bool AllItems { get; set; }
    public bool AllProducts { get; set; }
    public bool UsdItems { get; set; }
    public bool ExchangeRate { get; set; }

    public bool IsEmpty => Items.Count == 0 && !AllItems && !AllProducts && !UsdItems;
}

public sealed class RecalcSummary
{
    public int Recomputed { get; set; }
    public int PriceChanges { get; set; }
    public int Notifications { get; set; }
}

/// <summary>
/// Recomputes cached costs and prices for the items affected by a change, in two passes:
/// bought items first, then the products whose recipes use a material that changed.
/// Only rows that actually changed are written. Price changes go to the day's history row and,
/// when caused by another change, to the notifications.
/// </summary>
internal sealed class PricingEngine(Store store, SqliteConnection c, SqliteTransaction t)
{
    const decimal Eps = 0.0000001m;

    sealed class Row
    {
        public long Id;
        public ItemKind Kind;
        public bool UsableAsMaterial, HasWaste;
        public decimal? WastePct;
        public decimal? MarkupPct, RoundingStep;
        public decimal Cost, CostMaterials, CostLabor, CostOverhead, EffMarkup, EffStep, Price;
        public int LaborSeconds;
        public CostWarning Warnings;
    }

    sealed record Change(long Id, decimal OldPrice, decimal NewPrice, PriceReason Reasons);

    static bool Diff(decimal a, decimal b) => Math.Abs(a - b) > Eps;

    public RecalcSummary Run(RecalcRequest req)
    {
        try { return RunCore(req); }
        finally { _update?.Dispose(); }
    }

    RecalcSummary RunCore(RecalcRequest req)
    {
        var s = Store.LoadSettings(c, t);
        var summary = new RecalcSummary();
        var changes = new List<Change>();

        // ---- pass 1: bought items ----
        string? boughtFilter = null;
        if (!req.AllItems)
        {
            var ids = new HashSet<long>(req.Items);
            if (req.UsdItems)
                ids.UnionWith(c.Query<long>("""
                    SELECT DISTINCT src.item_id FROM item_sources src
                    LEFT JOIN suppliers sup ON sup.id = src.supplier_id
                    WHERE COALESCE(src.currency, sup.currency, 0) = 1
                    """, transaction: t));
            Store.FillTemp(c, t, "calc_b", ids);
            boughtFilter = "id IN (SELECT id FROM temp.calc_b)";
        }

        var bought = LoadRows($"kind <> 1{(boughtFilter is null ? "" : " AND " + boughtFilter)}");
        var sources = LoadSources(boughtFilter is null ? null : "src.item_id IN (SELECT id FROM temp.calc_b)");
        var changedMaterials = new List<long>();

        foreach (var r in bought)
        {
            summary.Recomputed++;
            var srcs = sources.TryGetValue(r.Id, out var l) ? l : [];
            var bc = Costing.ComputeBought(srcs.Select(x => x.Source).ToList(), id => srcs.FirstOrDefault(x => x.Source.SupplierId == id).Supplier, s.UsdPerEur);
            var cost = Costing.WithWaste(bc.UnitCost, r.HasWaste ? r.WastePct : null);
            var markup = r.MarkupPct ?? s.DefaultMarkupPct;
            var step = r.RoundingStep ?? s.RoundingStep;
            var price = Costing.RoundPrice(cost * (1 + markup / 100m), step, s.RoundingMode);

            var reasons = PriceReason.None;
            if (Diff(cost, r.Cost))
            {
                reasons |= req.ExchangeRate ? PriceReason.ExchangeRate
                    : cost > r.Cost ? PriceReason.SupplierCostUp : PriceReason.SupplierCostDown;
                changedMaterials.Add(r.Id);
            }
            Finish(r, cost, 0, 0, 0, 0, markup, step, price, bc.Warnings, reasons, req, changes);
        }

        // ---- pass 2: products, in waves ----
        // A product can be part of another product's recipe, so when a product's cost changes,
        // the products using it are repriced in the next wave (until nothing changes).
        HashSet<long>? wave = null;
        if (!req.AllItems && !req.AllProducts)
        {
            wave = new HashSet<long>(req.Items);
            wave.UnionWith(UsersOf(changedMaterials));
        }
        for (var depth = 0; depth < 30 && (wave is null || wave.Count > 0); depth++)
        {
            string? productFilter = null;
            if (wave is not null)
            {
                Store.FillTemp(c, t, "calc_p", wave);
                productFilter = "IN (SELECT id FROM temp.calc_p)";
            }
            var products = LoadRows($"kind = 1{(productFilter is null ? "" : " AND id " + productFilter)}");
            if (products.Count == 0) break;
            var labor = c.Query<(long ProductId, long Seconds)>(
                    $"SELECT product_id, SUM(seconds) FROM labor_entries {(productFilter is null ? "" : "WHERE product_id " + productFilter)} GROUP BY product_id",
                    transaction: t)
                .ToDictionary(x => x.ProductId, x => (int)x.Seconds);
            var (lines, materials) = LoadLines(productFilter is null ? null : "rl.product_id " + productFilter);
            var changedProducts = new List<long>();

            foreach (var r in products)
            {
                summary.Recomputed++;
                var pc = Costing.ComputeProduct(lines.TryGetValue(r.Id, out var ls) ? ls : [],
                    id => materials.GetValueOrDefault(id), labor.GetValueOrDefault(r.Id), s.LaborRatePerHour, s.OverheadRatePerHour);
                var markup = r.MarkupPct ?? s.DefaultMarkupPct;
                var step = r.RoundingStep ?? s.RoundingStep;
                var price = Costing.RoundPrice(pc.Total * (1 + markup / 100m), step, s.RoundingMode);

                var reasons = PriceReason.None;
                if (Diff(pc.Materials, r.CostMaterials))
                    reasons |= req.ExchangeRate ? PriceReason.ExchangeRate
                        : pc.Materials > r.CostMaterials ? PriceReason.MaterialCostUp : PriceReason.MaterialCostDown;
                if (Diff(pc.Labor, r.CostLabor))
                    reasons |= pc.Labor > r.CostLabor ? PriceReason.LaborUp : PriceReason.LaborDown;
                if (Diff(pc.Overhead, r.CostOverhead))
                    reasons |= pc.Overhead > r.CostOverhead ? PriceReason.OverheadUp : PriceReason.OverheadDown;
                if (Diff(pc.Total, r.Cost)) changedProducts.Add(r.Id);
                Finish(r, pc.Total, pc.Materials, pc.Labor, pc.Overhead, pc.LaborSeconds, markup, step, price, pc.Warnings, reasons, req, changes);
            }
            wave = UsersOf(changedProducts);
        }

        changes = Merge(changes);
        summary.PriceChanges = changes.Count;
        summary.Notifications = RecordChanges(changes, req);
        return summary;
    }

    /// <summary>Products whose recipe uses any of these items.</summary>
    HashSet<long> UsersOf(List<long> items)
    {
        if (items.Count == 0) return [];
        Store.FillTemp(c, t, "calc_m", items);
        return c.Query<long>("SELECT DISTINCT product_id FROM recipe_lines WHERE material_id IN (SELECT id FROM temp.calc_m)", transaction: t).ToHashSet();
    }

    /// <summary>An item repriced in several waves gets one change: first old price → last new price, all reasons.</summary>
    static List<Change> Merge(List<Change> changes)
    {
        var merged = new List<Change>();
        foreach (var g in changes.GroupBy(ch => ch.Id))
        {
            var first = g.First();
            var last = g.Last();
            var reasons = g.Aggregate(PriceReason.None, (r, ch) => r | ch.Reasons);
            if (reasons.HasFlag(PriceReason.Created)) merged.Add(new Change(g.Key, 0, last.NewPrice, PriceReason.Created));
            else if (Diff(first.OldPrice, last.NewPrice)) merged.Add(new Change(g.Key, first.OldPrice, last.NewPrice, reasons));
        }
        return merged;
    }

    SqliteCommand? _update;

    void Finish(Row r, decimal cost, decimal mat, decimal lab, decimal ovh, int seconds, decimal markup, decimal step,
        decimal price, CostWarning warnings, PriceReason reasons, RecalcRequest req, List<Change> changes)
    {
        if (Diff(markup, r.EffMarkup)) reasons |= PriceReason.MarginAdjustment;
        if (Diff(step, r.EffStep)) reasons |= PriceReason.Rounding;
        var priceChanged = Diff(price, r.Price);
        var rowChanged = priceChanged || Diff(cost, r.Cost) || Diff(mat, r.CostMaterials) || Diff(lab, r.CostLabor)
            || Diff(ovh, r.CostOverhead) || seconds != r.LaborSeconds || Diff(markup, r.EffMarkup) || Diff(step, r.EffStep)
            || warnings != r.Warnings;
        var created = req.Created.Contains(r.Id);

        if (rowChanged || created)
        {
            if (_update is null)
            {
                _update = c.CreateCommand();
                _update.Transaction = t;
                _update.CommandText = """
                    UPDATE items SET cost=$cost, cost_materials=$mat, cost_labor=$lab, cost_overhead=$ovh, labor_seconds=$sec,
                        eff_markup=$mk, eff_step=$st, price=$price, warnings=$w WHERE id=$id
                    """;
                foreach (var n in new[] { "$cost", "$mat", "$lab", "$ovh", "$mk", "$st", "$price" })
                    _update.Parameters.Add(n, SqliteType.Real);
                _update.Parameters.Add("$sec", SqliteType.Integer);
                _update.Parameters.Add("$w", SqliteType.Integer);
                _update.Parameters.Add("$id", SqliteType.Integer);
                _update.Prepare();
            }
            var p = _update.Parameters;
            p["$cost"].Value = (double)cost;
            p["$mat"].Value = (double)mat;
            p["$lab"].Value = (double)lab;
            p["$ovh"].Value = (double)ovh;
            p["$mk"].Value = (double)markup;
            p["$st"].Value = (double)step;
            p["$price"].Value = (double)price;
            p["$sec"].Value = seconds;
            p["$w"].Value = (int)warnings;
            p["$id"].Value = r.Id;
            _update.ExecuteNonQuery();
        }

        if (created)
            changes.Add(new Change(r.Id, r.Price, price, PriceReason.Created));
        else if (priceChanged)
            changes.Add(new Change(r.Id, r.Price, price, reasons == PriceReason.None ? PriceReason.Rounding : reasons));
    }

    int RecordChanges(List<Change> changes, RecalcRequest req)
    {
        if (changes.Count == 0) return 0;
        var now = store.Now;
        var today = store.Today;
        var notifications = 0;

        using var hist = c.CreateCommand();
        hist.Transaction = t;
        hist.CommandText = """
            INSERT INTO price_history(item_id, day, old_price, price, reasons) VALUES($id, $day, $old, $new, $r)
            ON CONFLICT(item_id, day) DO UPDATE SET price = excluded.price, reasons = reasons | excluded.reasons;
            DELETE FROM price_history WHERE item_id = $id AND day = $day AND old_price IS NOT NULL
                AND abs(price - old_price) < 0.0000001 AND (reasons & 1) = 0;
            """;
        var hId = hist.Parameters.Add("$id", SqliteType.Integer);
        var hDay = hist.Parameters.Add("$day", SqliteType.Text);
        var hOld = hist.Parameters.Add("$old", SqliteType.Real);
        var hNew = hist.Parameters.Add("$new", SqliteType.Real);
        var hR = hist.Parameters.Add("$r", SqliteType.Integer);

        using var note = c.CreateCommand();
        note.Transaction = t;
        note.CommandText = """
            INSERT INTO price_notifications(item_id, old_price, new_price, reasons, adjusted, created_at, updated_at)
            VALUES($id, $old, $new, $r, 0, $now, $now)
            ON CONFLICT(item_id) DO UPDATE SET new_price = excluded.new_price, reasons = reasons | excluded.reasons,
                adjusted = 0, updated_at = excluded.updated_at;
            DELETE FROM price_notifications WHERE item_id = $id AND abs(new_price - old_price) < 0.0000001;
            UPDATE items SET has_unseen_change = 1 WHERE id = $id;
            """;
        var nId = note.Parameters.Add("$id", SqliteType.Integer);
        var nOld = note.Parameters.Add("$old", SqliteType.Real);
        var nNew = note.Parameters.Add("$new", SqliteType.Real);
        var nR = note.Parameters.Add("$r", SqliteType.Integer);
        note.Parameters.AddWithValue("$now", now);

        foreach (var ch in changes)
        {
            var created = ch.Reasons == PriceReason.Created;
            hId.Value = ch.Id;
            hDay.Value = today;
            hOld.Value = created ? DBNull.Value : (double)ch.OldPrice;
            hNew.Value = (double)ch.NewPrice;
            hR.Value = (int)ch.Reasons;
            hist.ExecuteNonQuery();

            if (created || req.Direct.Contains(ch.Id)) continue;
            nId.Value = ch.Id;
            nOld.Value = (double)ch.OldPrice;
            nNew.Value = (double)ch.NewPrice;
            nR.Value = (int)ch.Reasons;
            note.ExecuteNonQuery();
            notifications++;
        }
        return notifications;
    }

    List<Row> LoadRows(string where)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = $"""
            SELECT id, kind, usable_as_material, markup_pct, rounding_step, cost, cost_materials, cost_labor, cost_overhead,
                   labor_seconds, eff_markup, eff_step, price, warnings, has_waste, waste_pct
            FROM items WHERE {where}
            """;
        using var rd = cmd.ExecuteReader();
        var list = new List<Row>();
        while (rd.Read())
        {
            list.Add(new Row
            {
                Id = rd.GetInt64(0),
                Kind = (ItemKind)rd.GetInt32(1),
                UsableAsMaterial = rd.GetInt64(2) != 0,
                MarkupPct = rd.IsDBNull(3) ? null : (decimal)rd.GetDouble(3),
                RoundingStep = rd.IsDBNull(4) ? null : (decimal)rd.GetDouble(4),
                Cost = (decimal)rd.GetDouble(5),
                CostMaterials = (decimal)rd.GetDouble(6),
                CostLabor = (decimal)rd.GetDouble(7),
                CostOverhead = (decimal)rd.GetDouble(8),
                LaborSeconds = rd.GetInt32(9),
                EffMarkup = (decimal)rd.GetDouble(10),
                EffStep = (decimal)rd.GetDouble(11),
                Price = (decimal)rd.GetDouble(12),
                Warnings = (CostWarning)rd.GetInt32(13),
                HasWaste = rd.GetInt64(14) != 0,
                WastePct = rd.IsDBNull(15) ? null : (decimal)rd.GetDouble(15),
            });
        }
        return list;
    }

    Dictionary<long, List<(ItemSource Source, Supplier? Supplier)>> LoadSources(string? where)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = $"""
            SELECT src.item_id, src.supplier_id, src.purchase_price, src.purchase_qty, src.currency, src.discount_pct,
                   src.shipping_pct, src.weight, sup.discount_pct, sup.shipping_pct, sup.currency
            FROM item_sources src LEFT JOIN suppliers sup ON sup.id = src.supplier_id
            {(where is null ? "" : "WHERE " + where)}
            ORDER BY src.item_id, src.position
            """;
        using var rd = cmd.ExecuteReader();
        var map = new Dictionary<long, List<(ItemSource, Supplier?)>>();
        while (rd.Read())
        {
            var src = new ItemSource
            {
                ItemId = rd.GetInt64(0),
                SupplierId = rd.IsDBNull(1) ? null : rd.GetInt64(1),
                PurchasePrice = (decimal)rd.GetDouble(2),
                PurchaseQty = (decimal)rd.GetDouble(3),
                Currency = rd.IsDBNull(4) ? null : (Currency)rd.GetInt32(4),
                DiscountPct = rd.IsDBNull(5) ? null : (decimal)rd.GetDouble(5),
                ShippingPct = rd.IsDBNull(6) ? null : (decimal)rd.GetDouble(6),
                Weight = (decimal)rd.GetDouble(7),
            };
            Supplier? sup = rd.IsDBNull(8) ? null : new Supplier
            {
                Id = src.SupplierId ?? 0,
                DiscountPct = (decimal)rd.GetDouble(8),
                ShippingPct = (decimal)rd.GetDouble(9),
                Currency = (Currency)rd.GetInt32(10),
            };
            if (!map.TryGetValue(src.ItemId, out var l)) map[src.ItemId] = l = [];
            l.Add((src, sup));
        }
        return map;
    }

    (Dictionary<long, List<RecipeLine>>, Dictionary<long, MaterialInfo>) LoadLines(string? where)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = $"""
            SELECT rl.product_id, rl.material_id, rl.quantity, rl.unit,
                   m.kind, m.unit, m.piece_length_cm, m.piece_area_cm2, m.piece_weight_kg, m.cost, rl.waste_pct
            FROM recipe_lines rl LEFT JOIN items m ON m.id = rl.material_id
            {(where is null ? "" : "WHERE " + where)}
            ORDER BY rl.product_id, rl.position
            """;
        using var rd = cmd.ExecuteReader();
        var lines = new Dictionary<long, List<RecipeLine>>();
        var mats = new Dictionary<long, MaterialInfo>();
        while (rd.Read())
        {
            var line = new RecipeLine
            {
                ProductId = rd.GetInt64(0),
                MaterialId = rd.IsDBNull(1) ? null : rd.GetInt64(1),
                Quantity = (decimal)rd.GetDouble(2),
                Unit = (QtyUnit)rd.GetInt32(3),
                WastePct = rd.IsDBNull(10) ? null : (decimal)rd.GetDouble(10),
            };
            if (line.MaterialId is long mid && !rd.IsDBNull(4) && !mats.ContainsKey(mid))
            {
                mats[mid] = new MaterialInfo
                {
                    Id = mid,
                    Kind = (ItemKind)rd.GetInt32(4),
                    Unit = (QtyUnit)rd.GetInt32(5),
                    PieceLengthCm = rd.IsDBNull(6) ? null : (decimal)rd.GetDouble(6),
                    PieceAreaCm2 = rd.IsDBNull(7) ? null : (decimal)rd.GetDouble(7),
                    PieceWeightKg = rd.IsDBNull(8) ? null : (decimal)rd.GetDouble(8),
                    Cost = (decimal)rd.GetDouble(9),
                };
            }
            if (!lines.TryGetValue(line.ProductId, out var l)) lines[line.ProductId] = l = [];
            l.Add(line);
        }
        return (lines, mats);
    }
}
