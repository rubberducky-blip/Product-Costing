using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

using ProductCosting.Core.Localization;

namespace ProductCosting.Core.Import;

/// <summary>A file that can't be read, with a message saying exactly why.</summary>
public sealed class SpreadsheetException(string message) : Exception(message);

/// <summary>Reads the first sheet of an .xlsx file (or a .csv) as rows of text. Numbers come back in invariant form.</summary>
public static class Spreadsheet
{
    public static List<string[]> Read(string path)
    {
        byte[] data;
        try
        {
            // also while the file is open in Excel
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            data = ms.ToArray();
        }
        catch (FileNotFoundException) { throw new SpreadsheetException(Strings.Get("import.err.notFound")); }
        catch (UnauthorizedAccessException) { throw new SpreadsheetException(Strings.Get("import.err.access")); }
        catch (IOException ex) { throw new SpreadsheetException(string.Format(Strings.Get("import.err.locked"), ex.Message)); }

        if (data.Length == 0) throw new SpreadsheetException(Strings.Get("import.err.empty"));
        if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)) return ReadCsv(data);
        // the old binary Excel format (.xls) starts with the OLE signature
        if (data.Length >= 8 && data[0] == 0xD0 && data[1] == 0xCF && data[2] == 0x11 && data[3] == 0xE0)
            throw new SpreadsheetException(Strings.Get("import.err.xls"));
        if (data.Length < 4 || data[0] != 'P' || data[1] != 'K')
            throw new SpreadsheetException(Strings.Get("import.err.notXlsx"));
        try
        {
            return ReadXlsx(data);
        }
        catch (InvalidDataException ex) { throw new SpreadsheetException(string.Format(Strings.Get("import.err.damaged"), ex.Message)); }
        catch (System.Xml.XmlException ex) { throw new SpreadsheetException(string.Format(Strings.Get("import.err.damaged"), ex.Message)); }
    }

    static List<string[]> ReadXlsx(byte[] data)
    {
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        // part names compared as the format intends: case-insensitive, "/" separated, without a leading "/"
        static string Key(string name) => name.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        var parts = new Dictionary<string, ZipArchiveEntry>();
        foreach (var e in zip.Entries) parts.TryAdd(Key(e.FullName), e);
        XDocument? Load(string name) => parts.TryGetValue(Key(name), out var e) ? XDocument.Load(e.Open()) : null;
        static string Resolve(string baseFolder, string target)
        {
            if (target.StartsWith('/')) return target.TrimStart('/');
            var stack = new List<string>(baseFolder.Split('/', StringSplitOptions.RemoveEmptyEntries));
            foreach (var seg in target.Split('/'))
                if (seg == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }
                else if (seg != "." && seg.Length > 0) stack.Add(seg);
            return string.Join('/', stack);
        }
        string? Target(string relsPart, string baseFolder, Func<XElement, bool> which) =>
            Load(relsPart)?.Root?.Elements().Where(r => r.Name.LocalName == "Relationship").FirstOrDefault(which)
                ?.Attribute("Target")?.Value is { } t ? Resolve(baseFolder, t) : null;

        // the workbook: found through the package relationships (usually xl/workbook.xml)
        var workbookPath = Target("_rels/.rels", "", r => r.Attribute("Type")?.Value.EndsWith("/officeDocument") == true) ?? "xl/workbook.xml";
        var workbook = Load(workbookPath)
            ?? throw new SpreadsheetException(string.Format(Strings.Get("import.err.noWorkbook"), workbookPath,
                string.Join(", ", parts.Keys.Take(8))));
        var folder = workbookPath.Contains('/') ? workbookPath[..workbookPath.LastIndexOf('/')] : "";
        var relsPath = (folder.Length > 0 ? folder + "/" : "") + "_rels/" + workbookPath[(workbookPath.LastIndexOf('/') + 1)..] + ".rels";
        var ns = workbook.Root!.Name.Namespace;

        var shared = new List<string>();
        var sharedPath = Target(relsPath, folder, r => r.Attribute("Type")?.Value.EndsWith("/sharedStrings") == true) ?? Resolve(folder, "sharedStrings.xml");
        if (Load(sharedPath) is { } sst)
            shared = sst.Root!.Elements().Where(x => x.Name.LocalName == "si")
                .Select(si => string.Concat(si.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value))).ToList();

        // the first sheet of the workbook (not necessarily sheet1.xml)
        var first = workbook.Root.Elements().FirstOrDefault(x => x.Name.LocalName == "sheets")?.Elements().FirstOrDefault(x => x.Name.LocalName == "sheet")
            ?? throw new SpreadsheetException(Strings.Get("import.err.noSheet"));
        var relId = first.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
        var sheetPath = (relId is null ? null : Target(relsPath, folder, r => r.Attribute("Id")?.Value == relId)) ?? Resolve(folder, "worksheets/sheet1.xml");
        var sheet = Load(sheetPath) ?? throw new SpreadsheetException(string.Format(Strings.Get("import.err.noSheetPart"), sheetPath));

        var rows = new List<string[]>();
        foreach (var row in sheet.Descendants().Where(x => x.Name.LocalName == "row"))
        {
            var cells = new SortedDictionary<int, string>();
            var next = 0;
            foreach (var c in row.Elements().Where(x => x.Name.LocalName == "c"))
            {
                var col = c.Attribute("r")?.Value is { } r ? Column(r) : next;
                next = col + 1;
                var type = c.Attribute("t")?.Value;
                var v = c.Elements().FirstOrDefault(x => x.Name.LocalName == "v")?.Value;
                var text = type switch
                {
                    "s" when int.TryParse(v, out var i) && i >= 0 && i < shared.Count => shared[i],
                    "inlineStr" => string.Concat(c.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value)),
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
        if (rows.Count == 0) throw new SpreadsheetException(Strings.Get("import.err.noRows"));
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

    static List<string[]> ReadCsv(byte[] data)
    {
        var text = new StreamReader(new MemoryStream(data), Encoding.UTF8, detectEncodingFromByteOrderMarks: true).ReadToEnd();
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0) throw new SpreadsheetException(Strings.Get("import.err.noRows"));
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
