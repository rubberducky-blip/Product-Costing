using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace ProductCosting.Core.Import;

/// <summary>Reads the first sheet of an .xlsx file (or a .csv) as rows of text. Numbers come back in invariant form.</summary>
public static class Spreadsheet
{
    static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static List<string[]> Read(string path) =>
        Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? ReadCsv(path) : ReadXlsx(path);

    static List<string[]> ReadXlsx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        XDocument Load(string name) =>
            XDocument.Load((zip.GetEntry(name) ?? throw new InvalidDataException($"Missing {name}")).Open());

        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is not null)
            shared = Load("xl/sharedStrings.xml").Root!.Elements(Main + "si")
                .Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value))).ToList();

        // the first sheet of the workbook (not necessarily sheet1.xml)
        var sheetPath = "xl/worksheets/sheet1.xml";
        var first = Load("xl/workbook.xml").Root!.Element(Main + "sheets")?.Elements(Main + "sheet").FirstOrDefault();
        var relId = first?.Attribute(Rel + "id")?.Value;
        if (relId is not null && zip.GetEntry("xl/_rels/workbook.xml.rels") is not null)
        {
            var target = Load("xl/_rels/workbook.xml.rels").Root!.Elements(PackageRel + "Relationship")
                .FirstOrDefault(r => r.Attribute("Id")?.Value == relId)?.Attribute("Target")?.Value;
            if (target is not null) sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        }

        var rows = new List<string[]>();
        foreach (var row in Load(sheetPath).Descendants(Main + "row"))
        {
            var cells = new SortedDictionary<int, string>();
            var next = 0;
            foreach (var c in row.Elements(Main + "c"))
            {
                var col = c.Attribute("r")?.Value is { } r ? Column(r) : next;
                next = col + 1;
                var type = c.Attribute("t")?.Value;
                var v = c.Element(Main + "v")?.Value;
                var text = type switch
                {
                    "s" when int.TryParse(v, out var i) && i >= 0 && i < shared.Count => shared[i],
                    "inlineStr" => string.Concat(c.Descendants(Main + "t").Select(t => t.Value)),
                    "b" => v == "1" ? "TRUE" : "FALSE",
                    _ => v ?? "",
                };
                cells[col] = text.Trim();
            }
            if (cells.Count == 0) continue;
            var arr = new string[cells.Keys.Max() + 1];
            for (var i = 0; i < arr.Length; i++) arr[i] = cells.GetValueOrDefault(i, "");
            if (arr.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(arr);
        }
        return rows;
    }

    /// <summary>"AB12" → 27 (zero-based column).</summary>
    static int Column(string cellRef)
    {
        var n = 0;
        foreach (var ch in cellRef)
        {
            if (!char.IsLetter(ch)) break;
            n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return n - 1;
    }

    static List<string[]> ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0) return [];
        // Greek Excel saves CSV with semicolons
        var sep = lines[0].Count(ch => ch == ';') >= lines[0].Count(ch => ch == ',') ? ';' : ',';
        return lines.Select(l => SplitCsv(l, sep)).ToList();
    }

    static string[] SplitCsv(string line, char sep)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (ch == sep && !quoted) { parts.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(ch);
        }
        parts.Add(sb.ToString().Trim());
        return parts.ToArray();
    }

    /// <summary>A number from a cell: invariant ("3.5") or as typed in Greek ("3,5" / "1.234,50"), with or without € / $.</summary>
    public static decimal? Number(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Replace("€", "").Replace("$", "").Replace(" ", "").Replace(" ", "").Trim();
        if (t.Contains(',') && t.Contains('.'))
            t = t.LastIndexOf(',') > t.LastIndexOf('.') ? t.Replace(".", "").Replace(',', '.') : t.Replace(",", "");
        else if (t.Contains(',')) t = t.Replace(',', '.');
        return decimal.TryParse(t, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
