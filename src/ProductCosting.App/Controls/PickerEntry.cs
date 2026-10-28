using System.Windows;
using ProductCosting.Core.Calc;
using ProductCosting.Core.Model;

namespace ProductCosting.App.Controls;

/// <summary>One line of a <see cref="SearchPicker"/>: a selectable value or a folder heading.</summary>
public sealed class PickerEntry
{
    public long? Id { get; init; }
    public string Text { get; init; } = "";
    public string? Detail { get; init; }
    public int Depth { get; init; }
    public bool IsHeader { get; init; }
    public bool IsFolder { get; init; }
    public string Path { get; init; } = "";
    internal string Key { get; init; } = "";
    public bool ShowPath { get; init; }

    /// <summary>Text shown in the closed picker: folders show their full path.</summary>
    public string Display => IsFolder ? Path : Text;

    internal PickerEntry Flat() => new()
    {
        Id = Id, Text = Text, Detail = Detail, Depth = 0, IsFolder = IsFolder, Path = Path, Key = Key,
        ShowPath = !string.IsNullOrEmpty(Path) && !IsFolder,
    };

    public Thickness Indent => new(Depth * 18, 0, 0, 0);
    public string Glyph => IsHeader || IsFolder ? "\uE8B7" : "";
    public bool HasGlyph => IsHeader || IsFolder;

    public override string ToString() => Text;

    static Dictionary<long, string> Paths(IReadOnlyList<Folder> folders)
    {
        var byId = folders.ToDictionary(f => f.Id);
        var paths = new Dictionary<long, string>();
        string P(Folder f)
        {
            if (paths.TryGetValue(f.Id, out var p)) return p;
            var s = f.ParentId is long pid && byId.TryGetValue(pid, out var parent) ? P(parent) + " › " + f.Name : f.Name;
            paths[f.Id] = s;
            return s;
        }
        foreach (var f in folders) P(f);
        return paths;
    }

    /// <summary>Values grouped under their folder tree. Values at the top level come last.</summary>
    public static List<PickerEntry> Tree(IReadOnlyList<Folder> folders, IEnumerable<(long Id, string Text, string? Detail, long? FolderId)> values, string? noneText = null)
    {
        var list = new List<PickerEntry>();
        if (noneText is not null) list.Add(new PickerEntry { Id = null, Text = noneText, Key = "" });
        var paths = Paths(folders);
        var byFolder = values.GroupBy(v => v.FolderId ?? 0).ToDictionary(g => g.Key, g => g.OrderBy(v => v.Text, NaturalComparer.Instance).ToList());
        var children = folders.GroupBy(f => f.ParentId ?? 0).ToDictionary(g => g.Key, g => g.OrderBy(f => f.Name, NaturalComparer.Instance).ToList());

        bool HasContent(long folderId) =>
            byFolder.ContainsKey(folderId) || (children.TryGetValue(folderId, out var subs) && subs.Any(s => HasContent(s.Id)));

        void Add(long folderId, int depth)
        {
            foreach (var f in children.GetValueOrDefault(folderId) ?? [])
            {
                if (!HasContent(f.Id)) continue;
                list.Add(new PickerEntry { Text = f.Name, Depth = depth, IsHeader = true, Path = paths[f.Id] });
                Add(f.Id, depth + 1);
            }
            if (folderId == 0) return;
            foreach (var v in byFolder.GetValueOrDefault(folderId) ?? [])
                list.Add(Value(v, depth, paths.GetValueOrDefault(folderId, "")));
        }
        Add(0, 0);
        foreach (var v in byFolder.GetValueOrDefault(0) ?? []) list.Add(Value(v, 0, ""));
        return list;
    }

    static PickerEntry Value((long Id, string Text, string? Detail, long? FolderId) v, int depth, string path) => new()
    {
        Id = v.Id, Text = v.Text, Detail = v.Detail, Depth = depth, Path = path,
        Key = TextNorm.Join(v.Text, v.Detail, path),
    };

    /// <summary>A plain list without folders; <paramref name="searchText"/> is what typing matches against.</summary>
    public static List<PickerEntry> Flat(IEnumerable<(long Id, string Text, string SearchText)> values) =>
        values.Select(v => new PickerEntry { Id = v.Id, Text = v.Text, Key = TextNorm.Join(v.Text, v.SearchText) }).ToList();

    /// <summary>Every folder of a list, indented, plus the top level.</summary>
    public static List<PickerEntry> Folders(IReadOnlyList<Folder> folders, string rootText)
    {
        var paths = Paths(folders);
        var list = new List<PickerEntry> { new() { Id = null, Text = rootText, Key = TextNorm.Normalize(rootText) } };
        var children = folders.GroupBy(f => f.ParentId ?? 0).ToDictionary(g => g.Key, g => g.OrderBy(f => f.Name, NaturalComparer.Instance).ToList());
        void Add(long parent, int depth)
        {
            foreach (var f in children.GetValueOrDefault(parent) ?? [])
            {
                list.Add(new PickerEntry { Id = f.Id, Text = f.Name, Depth = depth, IsFolder = true, Path = paths[f.Id], Key = TextNorm.Normalize(paths[f.Id]) });
                Add(f.Id, depth + 1);
            }
        }
        Add(0, 0);
        return list;
    }
}
