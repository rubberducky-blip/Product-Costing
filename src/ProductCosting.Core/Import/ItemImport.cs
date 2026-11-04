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
public static class ItemImport
{
    enum Col { Code, Supplier, Name, SupplierCode, Moq, Cost, Price, Kind }

    public static ImportFile Parse(List<string[]> table)
    {
        var rows = new List<ImportRow>();
        var problems = new List<ImportProblem>();
        if (table.Count == 0) return new(rows, problems);

        var map = Headers(table[0]);
        var start = map is null ? 0 : 1;
        map ??= Enum.GetValues<Col>().ToDictionary(c => c, c => (int)c);

        for (var r = start; r < table.Count; r++)
        {
            var cells = table[r];
            var line = r + 1;
            string Cell(Col c) => map.TryGetValue(c, out var i) && i < cells.Length ? cells[i].Trim() : "";
            var code = Cell(Col.Code);
            if (code.Length == 0 && cells.All(string.IsNullOrWhiteSpace)) continue;
            if (code.Length == 0) { problems.Add(new(line, "code")); continue; }
            var kind = Kind(Cell(Col.Kind));
            if (kind is null) { problems.Add(new(line, "kind")); continue; }
            decimal? Num(Col c, string what)
            {
                var text = Cell(c);
                if (text.Length == 0) return null;
                var n = Spreadsheet.Number(text);
                if (n is null || n < 0) problems.Add(new(line, what));
                return n is >= 0 ? n : null;
            }
            var moq = Num(Col.Moq, "moq");
            var cost = Num(Col.Cost, "cost");
            var price = Num(Col.Price, "price");
            var name = Cell(Col.Name);
            rows.Add(new ImportRow(line, kind.Value, code, name.Length > 0 ? name : code, NullIfEmpty(Cell(Col.Supplier)),
                NullIfEmpty(Cell(Col.SupplierCode)), moq is > 0 ? moq : null, cost, price is > 0 ? price : null));
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

    /// <summary>Which column holds what, when the first row is a header row (at least the code and one more).</summary>
    static Dictionary<Col, int>? Headers(string[] first)
    {
        var map = new Dictionary<Col, int>();
        for (var i = 0; i < first.Length; i++)
        {
            var h = TextNorm.Normalize(first[i]);
            if (h.Length == 0) continue;
            Col? col =
                (h.Contains("κωδ") || h.Contains("code")) && (h.Contains("προμ") || h.Contains("suppl") || h.Contains("vendor")) ? Col.SupplierCode
                : h.Contains("κωδ") || h.Contains("code") || h == "sku" ? Col.Code
                : h.Contains("προμηθ") || h.Contains("supplier") || h.Contains("vendor") ? Col.Supplier
                : h.Contains("moq") || h.Contains("ελαχ") || h.Contains("minimum") ? Col.Moq
                : h.Contains("πωλ") || h.Contains("sale") || h.Contains("sell") || h.Contains("λιαν") ? Col.Price
                : h.Contains("κοστ") || h.Contains("αγορ") || h.Contains("cost") || h.Contains("purchase") ? Col.Cost
                : h.Contains("τυπ") || h.Contains("ειδος") || h.Contains("κατηγ") || h.Contains("type") || h.Contains("kind") ? Col.Kind
                : h.Contains("ονομ") || h.Contains("περιγρ") || h.Contains("name") || h.Contains("descr") ? Col.Name
                : h.Contains("τιμη") || h.Contains("price") ? Col.Price
                : null;
            if (col is { } c && !map.ContainsKey(c)) map[c] = i;
        }
        return map.ContainsKey(Col.Code) && map.Count >= 2 ? map : null;
    }
}
