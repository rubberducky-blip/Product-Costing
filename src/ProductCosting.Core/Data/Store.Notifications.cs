using Dapper;
using Microsoft.Data.Sqlite;
using ProductCosting.Core.Calc;
using ProductCosting.Core.Model;

namespace ProductCosting.Core.Data;

public sealed partial class Store
{
    public List<PriceNotification> GetNotifications() => Read(c => c.Query<PriceNotification>("""
        SELECT n.id, n.item_id, n.old_price, n.new_price, n.reasons, n.updated_at, n.adjusted,
               i.code, i.name, i.kind, i.price AS current_price, i.cost, i.eff_markup
        FROM price_notifications n JOIN items i ON i.id = n.item_id
        ORDER BY n.updated_at DESC, i.code
        """).ToList());

    public int CountNotifications() => Read(c => c.ExecuteScalar<int>("SELECT count(*) FROM price_notifications"));

    public void HideNotification(long id) => Write(DataChange.Notifications, (c, t) =>
        c.Execute("DELETE FROM price_notifications WHERE id=@id", new { id }, t));

    public void HideAllNotifications() => Write(DataChange.Notifications, (c, t) =>
        c.Execute("DELETE FROM price_notifications", transaction: t));

    /// <summary>Clears the red dot of an item (it was hovered).</summary>
    public void MarkSeen(long itemId) => Write(DataChange.None, (c, t) =>
        c.Execute("UPDATE items SET has_unseen_change=0 WHERE id=@itemId AND has_unseen_change=1", new { itemId }, t));

    /// <summary>Sets the item's markup so its price goes back to the notification's old price.</summary>
    public bool AdjustMargin(long notificationId) => Write(DataChange.Notifications | DataChange.Items, (c, t) =>
        AdjustMargins(c, t, c.Query<long>("SELECT id FROM price_notifications WHERE id=@notificationId", new { notificationId }, t).ToList()) > 0);

    /// <summary>Margin expansion: adjusts every item whose price dropped back to its previous price.</summary>
    public int MarginExpansion() => Write(DataChange.Notifications | DataChange.Items, (c, t) =>
        AdjustMargins(c, t, c.Query<long>("SELECT id FROM price_notifications WHERE new_price < old_price AND adjusted = 0", transaction: t).ToList()));

    public int CountMarginExpansion() => Read(c => c.ExecuteScalar<int>("SELECT count(*) FROM price_notifications WHERE new_price < old_price AND adjusted = 0"));

    int AdjustMargins(SqliteConnection c, SqliteTransaction t, List<long> notificationIds)
    {
        if (notificationIds.Count == 0) return 0;
        var s = LoadSettings(c, t);
        var req = new RecalcRequest();
        var adjusted = new List<long>();
        FillTemp(c, t, "adjust_n", notificationIds);
        var rows = c.Query<(long Id, long ItemId, double OldPrice, double Cost, double EffStep)>("""
            SELECT n.id, n.item_id, n.old_price, i.cost, i.eff_step FROM price_notifications n JOIN items i ON i.id = n.item_id
            WHERE n.id IN (SELECT id FROM temp.adjust_n)
            """, transaction: t).ToList();
        using var upd = c.CreateCommand();
        upd.Transaction = t;
        upd.CommandText = "UPDATE items SET markup_pct=$m, updated_at=$now WHERE id=$id";
        var pm = upd.Parameters.Add("$m", Microsoft.Data.Sqlite.SqliteType.Real);
        var pid = upd.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Integer);
        upd.Parameters.AddWithValue("$now", Now);
        foreach (var n in rows)
        {
            var markup = Costing.MarkupForPrice((decimal)n.Cost, (decimal)n.OldPrice, (decimal)n.EffStep, s.RoundingMode);
            if (markup is null) continue;
            pm.Value = (double)markup.Value;
            pid.Value = n.ItemId;
            upd.ExecuteNonQuery();
            req.Items.Add(n.ItemId);
            req.Direct.Add(n.ItemId);
            adjusted.Add(n.Id);
        }
        if (adjusted.Count == 0) return 0;
        new PricingEngine(this, c, t).Run(req);
        FillTemp(c, t, "adjust_n", adjusted);
        c.Execute("UPDATE price_notifications SET adjusted=1 WHERE id IN (SELECT id FROM temp.adjust_n)", transaction: t);
        return adjusted.Count;
    }

    // ---------- global search ----------

    public List<SearchHit> Search(string query, int limit = 60) => Read(c =>
    {
        var p = TextNorm.LikePattern(query);
        if (p.Length <= 2) return [];
        var ids = c.Query<long>("SELECT rowid FROM search_index WHERE body LIKE @p LIMIT @limit", new { p, limit }).ToList();
        var items = ids.Where(r => r % 2 == 0).Select(r => r / 2).ToList();
        var sups = ids.Where(r => r % 2 == 1).Select(r => r / 2).ToList();
        var q = TextNorm.Normalize(query).Trim();
        var hits = new List<SearchHit>();
        if (items.Count > 0)
        {
            foreach (var (hit, key, notes) in c.Query<SearchHit, string, string?, (SearchHit, string, string?)>(
                "SELECT 0 AS is_supplier, id, kind, code, name, price, cost, unit, list_key, notes FROM items WHERE id IN @items",
                (h, k, n) => (h, k, n), new { items }, splitOn: "list_key,notes"))
            {
                if (!TextNorm.Normalize(key).Contains(q))
                {
                    hit.Snippet = Snippet(notes, q);
                    if (hit.Snippet is null)
                    {
                        var texts = c.Query<string>("""
                            SELECT comment FROM recipe_lines WHERE product_id=@id AND comment IS NOT NULL
                            UNION ALL SELECT comment FROM labor_entries WHERE product_id=@id AND comment IS NOT NULL
                            UNION ALL SELECT name FROM item_variants WHERE item_id=@id
                            """, new { id = hit.Id });
                        hit.Snippet = texts.Select(t => Snippet(t, q)).FirstOrDefault(x => x is not null);
                    }
                }
                hits.Add(hit);
            }
        }
        if (sups.Count > 0)
        {
            foreach (var (hit, key, notes) in c.Query<SearchHit, string, string?, (SearchHit, string, string?)>(
                "SELECT 1 AS is_supplier, id, 0 AS kind, '' AS code, name, 0 AS price, list_key, notes FROM suppliers WHERE id IN @sups",
                (h, k, n) => (h, k, n), new { sups }, splitOn: "list_key,notes"))
            {
                if (!TextNorm.Normalize(key).Contains(q)) hit.Snippet = Snippet(notes, q);
                hits.Add(hit);
            }
        }
        // exact code / name prefix first
        return hits.OrderBy(h => TextNorm.Normalize(h.Code).StartsWith(q) || TextNorm.Normalize(h.Name).StartsWith(q) ? 0 : 1)
            .ThenBy(h => h.IsSupplier)
            .ThenBy(h => h.IsSupplier ? h.Name : h.Code, NaturalComparer.Instance).ToList();
    });

    /// <summary>A short excerpt of <paramref name="text"/> around the first match of the normalized query.</summary>
    internal static string? Snippet(string? text, string normalizedQuery)
    {
        if (string.IsNullOrWhiteSpace(text) || normalizedQuery.Length == 0) return null;
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var at = TextNorm.Normalize(flat).IndexOf(normalizedQuery, StringComparison.Ordinal);
        if (at < 0) return null;
        // normalizing keeps one character per character for Greek and Latin text, so positions line up
        var start = Math.Max(0, at - 30);
        var end = Math.Min(flat.Length, at + normalizedQuery.Length + 45);
        return (start > 0 ? "…" : "") + flat[start..end] + (end < flat.Length ? "…" : "");
    }
}
