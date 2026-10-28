using Dapper;
using Microsoft.Data.Sqlite;
using ProductCosting.Core.Calc;
using ProductCosting.Core.Model;

namespace ProductCosting.Core.Data;

public sealed partial class Store
{
    public List<Folder> GetFolders(ListScope scope) => Read(c => c.Query<Folder>(
        "SELECT id, scope, parent_id, name, sort_mode, position FROM folders WHERE scope=@scope", new { scope }).ToList());

    /// <summary>Subfolders of a folder (null = top level) with their contents count, in the container's sort order.</summary>
    public List<FolderTile> GetFolderTiles(ListScope scope, long? parentId) => Read(c =>
    {
        var tiles = c.Query<FolderTile>("""
            SELECT f.id, f.parent_id, f.name, f.position,
                   (SELECT count(*) FROM folders s WHERE s.scope = f.scope AND s.parent_id = f.id) AS folder_count
            FROM folders f WHERE f.scope=@scope AND f.parent_id IS @parentId
            """, new { scope, parentId }).ToList();
        if (tiles.Count > 0)
        {
            var counts = c.Query<(long Folder, int Count)>(scope switch
            {
                ListScope.Suppliers => "SELECT folder_id, count(*) FROM suppliers WHERE folder_id IS NOT NULL GROUP BY folder_id",
                ListScope.RawMaterials => """
                    SELECT folder_id, count(*) FROM items WHERE kind=3 AND folder_id IS NOT NULL GROUP BY folder_id
                    UNION ALL SELECT material_folder_id, count(*) FROM items WHERE usable_as_material=1 AND material_folder_id IS NOT NULL GROUP BY material_folder_id
                    """,
                _ => $"SELECT folder_id, count(*) FROM items WHERE kind={(int)scope} AND folder_id IS NOT NULL GROUP BY folder_id",
            }).GroupBy(x => x.Folder).ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
            foreach (var tile in tiles) tile.ItemCount = counts.GetValueOrDefault(tile.Id);
        }
        return GetSortMode(c, scope, parentId) == Model.SortMode.Manual
            ? tiles.OrderBy(f => f.Position).ThenBy(f => f.Name, NaturalComparer.Instance).ToList()
            : tiles.OrderBy(f => f.Name, NaturalComparer.Instance).ToList();
    });

    internal static SortMode GetSortMode(SqliteConnection c, ListScope scope, long? folderId, SqliteTransaction? t = null) =>
        folderId is long id
            ? (SortMode)c.ExecuteScalar<int>("SELECT sort_mode FROM folders WHERE id=@id", new { id }, t)
            : (SortMode)c.ExecuteScalar<int>("SELECT COALESCE((SELECT sort_mode FROM scope_settings WHERE scope=@scope), 0)", new { scope }, t);

    public SortMode GetSortMode(ListScope scope, long? folderId) => Read(c => GetSortMode(c, scope, folderId));

    /// <summary>Sets how a folder sorts. Switching to manual starts from the current order by code.</summary>
    public void SetSortMode(ListScope scope, long? folderId, SortMode mode)
    {
        var rows = scope == ListScope.Suppliers
            ? GetSupplierRows(folderId).Select(r => r.Id).ToList()
            : GetItemRows(scope, folderId).Select(r => r.Id).ToList();
        var folders = GetFolderTiles(scope, folderId).Select(f => f.Id).ToList();
        Write(DataChange.Folders | DataChange.Items | DataChange.Suppliers, (c, t) =>
        {
            if (folderId is long id) c.Execute("UPDATE folders SET sort_mode=@mode WHERE id=@id", new { mode, id }, t);
            else c.Execute("INSERT INTO scope_settings(scope, sort_mode) VALUES(@scope, @mode) ON CONFLICT(scope) DO UPDATE SET sort_mode=excluded.sort_mode", new { scope, mode }, t);
            if (mode == Model.SortMode.Manual)
            {
                WritePositions(c, t, scope, rows);
                for (var i = 0; i < folders.Count; i++) c.Execute("UPDATE folders SET position=@i WHERE id=@id", new { i, id = folders[i] }, t);
            }
        });
    }

    public long CreateFolder(ListScope scope, long? parentId, string name) => Write(DataChange.Folders, (c, t) =>
    {
        var pos = NextPosition(c, t, "SELECT max(position) FROM folders WHERE scope=@scope AND parent_id IS @parentId", new { scope, parentId });
        return c.ExecuteScalar<long>("INSERT INTO folders(scope, parent_id, name, position) VALUES(@scope, @parentId, @name, @pos) RETURNING id",
            new { scope, parentId, name = name.Trim(), pos }, t);
    });

    public void RenameFolder(long id, string name) => Write(DataChange.Folders, (c, t) =>
        c.Execute("UPDATE folders SET name=@name WHERE id=@id", new { id, name = name.Trim() }, t));

    /// <summary>Deletes a folder. Its contents (rows and subfolders) move up to its parent.</summary>
    public void DeleteFolder(long id) => Write(DataChange.Folders | DataChange.Items | DataChange.Suppliers, (c, t) =>
    {
        var f = c.QuerySingle<Folder>("SELECT id, scope, parent_id, name FROM folders WHERE id=@id", new { id }, t);
        var args = new { id, parent = f.ParentId };
        c.Execute("UPDATE folders SET parent_id=@parent WHERE parent_id=@id", args, t);
        switch (f.Scope)
        {
            case ListScope.Suppliers: c.Execute("UPDATE suppliers SET folder_id=@parent WHERE folder_id=@id", args, t); break;
            case ListScope.RawMaterials:
                c.Execute("UPDATE items SET folder_id=@parent WHERE kind=3 AND folder_id=@id", args, t);
                c.Execute("UPDATE items SET material_folder_id=@parent WHERE material_folder_id=@id", args, t);
                break;
            default: c.Execute("UPDATE items SET folder_id=@parent WHERE kind=@k AND folder_id=@id", new { id, parent = f.ParentId, k = (int)f.Scope }, t); break;
        }
        c.Execute("DELETE FROM folders WHERE id=@id", args, t);
    });

    /// <summary>Moves a folder under another one (null = top level). Refuses to move a folder into itself or its subfolders.</summary>
    public bool MoveFolder(long id, long? newParentId) => Write(DataChange.Folders, (c, t) =>
    {
        for (var p = newParentId; p is long pid; p = c.ExecuteScalar<long?>("SELECT parent_id FROM folders WHERE id=@pid", new { pid }, t))
            if (pid == id) return false;
        var scope = c.ExecuteScalar<int>("SELECT scope FROM folders WHERE id=@id", new { id }, t);
        var pos = NextPosition(c, t, "SELECT max(position) FROM folders WHERE scope=@scope AND parent_id IS @newParentId", new { scope, newParentId });
        c.Execute("UPDATE folders SET parent_id=@newParentId, position=@pos WHERE id=@id", new { id, newParentId, pos }, t);
        return true;
    });

    public void ReorderFolders(IReadOnlyList<long> orderedIds) => Write(DataChange.Folders, (c, t) =>
    {
        for (var i = 0; i < orderedIds.Count; i++) c.Execute("UPDATE folders SET position=@i WHERE id=@id", new { i, id = orderedIds[i] }, t);
    });
}
