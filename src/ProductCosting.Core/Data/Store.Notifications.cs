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
        var hits = new List<SearchHit>();
        if (items.Count > 0)
            hits.AddRange(c.Query<SearchHit>("SELECT 0 AS is_supplier, id, kind, code, name, price FROM items WHERE id IN @items", new { items }));
        if (sups.Count > 0)
            hits.AddRange(c.Query<SearchHit>("SELECT 1 AS is_supplier, id, 0 AS kind, '' AS code, name, 0 AS price FROM suppliers WHERE id IN @sups", new { sups }));
        var norm = TextNorm.Normalize(query).Trim();
        // exact code / name prefix first
        return hits.OrderBy(h => TextNorm.Normalize(h.Code).StartsWith(norm) || TextNorm.Normalize(h.Name).StartsWith(norm) ? 0 : 1)
            .ThenBy(h => h.IsSupplier)
            .ThenBy(h => h.IsSupplier ? h.Name : h.Code, NaturalComparer.Instance).ToList();
    });
}
