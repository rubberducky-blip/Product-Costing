using ProductCosting.Core.Calc;
using ProductCosting.Core.Model;

namespace ProductCosting.Core.Import;

/// <summary>One row of an import file: merchandise or raw material with one supplier.</summary>
public sealed record ImportRow(int Line, ItemKind Kind, string Code, string Name, string? Supplier, string? SupplierCode,
    decimal? Moq, decimal? Cost, decimal? Price);

public sealed record ImportProblem(int Line, string Reason);

public sealed record ImportFile(List<ImportRow> Rows, List<ImportProblem> Problems);

public sealed record ImportResult(int Created, int Updated, List<ImportProblem> Problems);

/// <summary>
/// Turns spreadsheet rows into import rows. Columns are found by their headers (Greek or English); without headers
/// they are taken in this order: code, supplier, name, supplier code, MOQ, purchase cost, selling price, type.
/// </summary>
public enum ImportField { Code, Supplier, Name, SupplierCode, Moq, Cost, Price, Kind }

public static class ItemImport
{
    /// <summary>The fields an import needs; the others are optional.</summary>
    public static IReadOnlyList<ImportField> Required { get; } = [ImportField.Code, ImportField.Cost, ImportField.Price, ImportField.Kind];

    /// <summary>The fixed column order used when the file has no header row.</summary>
    public static Dictionary<ImportField, int> DefaultOrder() => Enum.GetValues<ImportField>().ToDictionary(c => c, c => (int)c);

    /// <summary>Reads with the columns found from the header row (or in the fixed order without one).</summary>
    public static ImportFile Parse(List<string[]> table)
    {
        var map = table.Count > 0 ? Guess(table[0]) : null;
        return Parse(table, map ?? DefaultOrder(), hasHeader: map is not null);
    }

    /// <summary>Reads with the columns the user matched; rows without a cost or a selling price above zero are reported, not imported.</summary>
    public static ImportFile Parse(List<string[]> table, IReadOnlyDictionary<ImportField, int> map, bool hasHeader)
    {
        var rows = new List<ImportRow>();
        var problems = new List<ImportProblem>();
        var start = hasHeader ? 1 : 0;

        for (var r = start; r < table.Count; r++)
        {
            var cells = table[r];
            var line = r + 1;
            string Cell(ImportField c) => map.TryGetValue(c, out var i) && i >= 0 && i < cells.Length ? cells[i].Trim() : "";
            var code = Cell(ImportField.Code);
            if (code.Length == 0 && cells.All(string.IsNullOrWhiteSpace)) continue;
            if (code.Length == 0) { problems.Add(new(line, "code")); continue; }
            var kind = Kind(Cell(ImportField.Kind));
            if (kind is null) { problems.Add(new(line, "kind")); continue; }
            decimal? Num(ImportField c, string what)
            {
                var text = Cell(c);
                if (text.Length == 0) return null;
                var n = Spreadsheet.Number(text);
                if (n is null || n < 0) problems.Add(new(line, what));
                return n is >= 0 ? n : null;
            }
            var moq = Num(ImportField.Moq, "moq");
            var cost = Num(ImportField.Cost, "cost");
            var price = Num(ImportField.Price, "price");
            if (cost is not > 0) { problems.Add(new(line, "zeroCost")); continue; }
            if (price is not > 0) { problems.Add(new(line, "zeroPrice")); continue; }
            var name = Cell(ImportField.Name);
            rows.Add(new ImportRow(line, kind.Value, code, name.Length > 0 ? name : code, NullIfEmpty(Cell(ImportField.Supplier)),
                NullIfEmpty(Cell(ImportField.SupplierCode)), moq is > 0 ? moq : null, cost, price is > 0 ? price : null));
        }
        return new(rows, problems);
    }

    static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    /// <summary>"Εμπόρευμα" / "merchandise" or "Πρώτη ύλη" / "raw material" (any case, with or without accents).</summary>
    public static ItemKind? Kind(string text)
    {
        var t = TextNorm.Normalize(text);
        if (t.Contains("εμπορ") || t.Contains("merch") || t.Contains("goods")) return ItemKind.Merchandise;
        if (t.Contains("πρωτ") || t.Contains("υλη") || t.Contains("raw") || t.Contains("material")) return ItemKind.RawMaterial;
        return null;
    }

    /// <summary>Which column holds what, from the titles of a header row; null when the row doesn't look like titles.</summary>
    public static Dictionary<ImportField, int>? Guess(string[] first)
    {
        var map = new Dictionary<ImportField, int>();
        for (var i = 0; i < first.Length; i++)
        {
            var h = TextNorm.Normalize(first[i]);
            if (h.Length == 0) continue;
            ImportField? col =
                (h.Contains("κωδ") || h.Contains("code")) && (h.Contains("προμ") || h.Contains("suppl") || h.Contains("vendor")) ? ImportField.SupplierCode
                : h.Contains("κωδ") || h.Contains("code") || h == "sku" ? ImportField.Code
                : h.Contains("προμηθ") || h.Contains("supplier") || h.Contains("vendor") ? ImportField.Supplier
                : h.Contains("moq") || h.Contains("ελαχ") || h.Contains("minimum") ? ImportField.Moq
                : h.Contains("πωλ") || h.Contains("sale") || h.Contains("sell") || h.Contains("λιαν") ? ImportField.Price
                : h.Contains("κοστ") || h.Contains("αγορ") || h.Contains("cost") || h.Contains("purchase") ? ImportField.Cost
                : h.Contains("τυπ") || h.Contains("ειδος") || h.Contains("κατηγ") || h.Contains("type") || h.Contains("kind") ? ImportField.Kind
                : h.Contains("ονομ") || h.Contains("περιγρ") || h.Contains("name") || h.Contains("descr") ? ImportField.Name
                : h.Contains("τιμη") || h.Contains("price") ? ImportField.Price
                : null;
            if (col is { } c && !map.ContainsKey(c)) map[c] = i;
        }
        return map.ContainsKey(ImportField.Code) && map.Count >= 2 ? map : null;
    }
}
