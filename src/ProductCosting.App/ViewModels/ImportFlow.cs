using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.Views;
using ProductCosting.Core.Calc;
using ProductCosting.Core.Import;
using ProductCosting.Core.Model;

namespace ProductCosting.App.ViewModels;

/// <summary>
/// Settings → import merchandise and raw materials from Excel: pick the file, decide about suppliers that don't exist
/// yet (create them, or give their codes to an existing supplier), confirm, import.
/// </summary>
public static class ImportFlow
{
    public static void Run(Window owner)
    {
        var dlg = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv" };
        if (dlg.ShowDialog(owner) != true) return;
        List<string[]> table;
        try
        {
            table = Spreadsheet.Read(dlg.FileName);
        }
        catch (SpreadsheetException ex)
        {
            Dialogs.Error(ex.Message);
            return;
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.F("import.unreadable", $"{ex.GetType().Name}: {ex.Message}"));
            return;
        }
        // the user confirms which column holds what (recognised titles are matched already)
        var columns = new ImportColumnsWindow(table) { Owner = owner };
        if (columns.ShowDialog() != true) return;
        var file = ItemImport.Parse(table, columns.Map, columns.HasHeader);
        if (file.Rows.Count == 0)
        {
            Dialogs.Info(Loc.T("import.empty") + Problems(file.Problems));
            return;
        }

        // suppliers by name; the ones the file names that don't exist yet are asked about one by one
        var store = AppState.Store;
        var map = new Dictionary<string, long>();
        foreach (var s in store.GetSuppliers()) map.TryAdd(TextNorm.Normalize(s.Name).Trim(), s.Id);
        var unknown = file.Rows.Where(r => r.Supplier is not null)
            .GroupBy(r => TextNorm.Normalize(r.Supplier!).Trim())
            .Where(g => !map.ContainsKey(g.Key))
            .Select(g => (Key: g.Key, Name: g.First().Supplier!.Trim(), Count: g.Count()))
            .ToList();
        var create = new List<(string Key, string Name)>();
        foreach (var u in unknown)
        {
            var ask = new ImportSupplierWindow(u.Name, u.Count) { Owner = owner };
            if (ask.ShowDialog() != true) return;
            if (ask.ExistingId is long id) map[u.Key] = id;
            else create.Add((u.Key, u.Name));
        }

        var codes = store.GetCodes();
        var existing = file.Rows.Count(r => codes.Contains(r.Code.Trim()));
        if (!Dialogs.Confirm(Loc.F("import.confirm", file.Rows.Count, file.Rows.Count - existing, existing) + Problems(file.Problems),
                Loc.T("import.run"))) return;

        ImportResult result;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            foreach (var (key, name) in create) map[key] = store.SaveSupplier(new Supplier { Name = name });
            result = store.ImportItems(file.Rows, map);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
        var all = file.Problems.Concat(result.Problems).ToList();
        Dialogs.Info(Loc.F("import.done", result.Created, result.Updated) + Problems(all), Loc.T("import.title"));
    }

    /// <summary>"Line 4: unknown type…" for the first problems, as an extra paragraph.</summary>
    static string Problems(List<ImportProblem> problems)
    {
        if (problems.Count == 0) return "";
        var lines = problems.OrderBy(p => p.Line).Take(12).Select(p => Loc.F("import.line", p.Line, Loc.T("import.problem." + p.Reason)));
        var more = problems.Count > 12 ? "\n" + Loc.F("import.more", problems.Count - 12) : "";
        return "\n\n" + Loc.T("import.problems") + "\n" + string.Join("\n", lines) + more;
    }
}
