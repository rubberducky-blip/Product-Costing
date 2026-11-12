using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ProductCosting.App.Views;
using ProductCosting.Core.Localization;
using ProductCosting.Core.Model;

namespace ProductCosting.App.Infrastructure;

/// <summary>
/// The printed list of a supplier's items (code, name, supplier code, variation, pack) with a line to write on beside
/// each row; or, when the supplier has one, the person's own file printed instead.
/// </summary>
public static class ItemListPrint
{
    static string FileKey(long supplierId) => $"supplier-print:{supplierId}";
    static string NameKey(long supplierId) => $"supplier-print-name:{supplierId}";

    /// <summary>The name of the supplier's own printout, or null.</summary>
    public static string? CustomName(long supplierId) =>
        AppState.Store.GetBlob(NameKey(supplierId)) is { } b ? System.Text.Encoding.UTF8.GetString(b) : null;

    public static void SetCustom(long supplierId, string path)
    {
        AppState.Store.SetBlob(FileKey(supplierId), File.ReadAllBytes(path));
        AppState.Store.SetBlob(NameKey(supplierId), System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(path)));
    }

    public static void RemoveCustom(long supplierId)
    {
        AppState.Store.SetBlob(FileKey(supplierId), null);
        AppState.Store.SetBlob(NameKey(supplierId), null);
    }

    public static void Print(Supplier supplier)
    {
        if (AppState.Store.GetBlob(FileKey(supplier.Id)) is { } data && CustomName(supplier.Id) is { } name)
            PrintFile(data, name);
        else
            PrintList(supplier);
    }

    /// <summary>Hands the file to Windows to print with its own program (or opens it when that program can't print).</summary>
    static void PrintFile(byte[] data, string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ProductCosting");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)));
        File.WriteAllBytes(path, data);
        try
        {
            Process.Start(new ProcessStartInfo(path) { Verb = "print", UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Dialogs.Info(Loc.T("printList.opened"));
        }
    }

    static void PrintList(Supplier supplier)
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        var doc = Build(supplier, dlg.PrintableAreaWidth);
        doc.PageWidth = dlg.PrintableAreaWidth;
        doc.PageHeight = dlg.PrintableAreaHeight;
        doc.ColumnWidth = dlg.PrintableAreaWidth;
        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, Loc.F("printList.title", supplier.Name));
    }

    /// <summary>One row per item, or per variation of this supplier (same code and name), plus an empty writing line.</summary>
    /// <param name="pageWidth">The printable width: the columns get fixed widths from it (relative widths don't work in printed tables).</param>
    internal static FlowDocument Build(Supplier supplier, double pageWidth)
    {
        var items = AppState.Store.GetSupplierItems(supplier.Id);
        var variants = AppState.Store.GetVariants(items.Select(i => i.ItemId));
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            PagePadding = new Thickness(48),
            Foreground = Brushes.Black,
            Background = Brushes.White,
        };
        doc.Blocks.Add(new Paragraph(new Run(supplier.Name)) { FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        doc.Blocks.Add(new Paragraph(new Run($"{Loc.T("printList.heading")} · {DateTime.Now.ToString("d", Loc.Culture)}"))
            { Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 14) });

        var table = new Table { CellSpacing = 0 };
        // code, supplier code, variation and pack fixed; the name and the writing line share the rest
        double[] fixedCols = [80, 110, 110, 90];
        var rest = Math.Max(200, pageWidth - doc.PagePadding.Left - doc.PagePadding.Right - fixedCols.Sum() - 4);
        foreach (var w in new[] { fixedCols[0], rest * 0.55, fixedCols[1], fixedCols[2], fixedCols[3], rest * 0.45 })
            table.Columns.Add(new TableColumn { Width = new GridLength(w) });
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        var line = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB));

        TableCell Cell(string text, bool bold = false, bool head = false, bool writeLine = false) => new(new Paragraph(new Run(text))
            { Margin = new Thickness(0), FontWeight = bold || head ? FontWeights.SemiBold : FontWeights.Normal })
        {
            Padding = new Thickness(4, 6, 6, 6),
            BorderBrush = writeLine ? Brushes.Black : line,
            BorderThickness = writeLine ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 0, head ? 1.5 : 0.5),
            Background = head ? new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)) : null,
        };
        var header = new TableRow();
        foreach (var h in new[] { "list.code", "list.name", "list.supplierCode", "item.variant", "item.pack" })
            header.Cells.Add(Cell(Loc.T(h), head: true));
        header.Cells.Add(Cell("", head: true));
        group.Rows.Add(header);

        foreach (var i in items)
        {
            var pack = i.EntryPack is decimal p ? $"{Fmt.Num(p)} {Strings.Measure(i.EntryUnit)}" : "";
            var mine = variants.TryGetValue(i.ItemId, out var all) ? all.Where(v => v.SupplierId is null || v.SupplierId == supplier.Id).ToList() : [];
            var rows = mine.Count > 0 ? mine.Select(v => (Code: v.Code ?? i.SupplierCode, Variant: v.Name)).ToList() : [(i.SupplierCode, (string?)null)];
            foreach (var (code, variant) in rows)
            {
                var r = new TableRow();
                r.Cells.Add(Cell(i.Code, bold: true));
                r.Cells.Add(Cell(i.Name));
                r.Cells.Add(Cell(code ?? ""));
                r.Cells.Add(Cell(variant ?? ""));
                r.Cells.Add(Cell(pack));
                // room to write by hand
                r.Cells.Add(Cell("", writeLine: true));
                group.Rows.Add(r);
            }
        }
        doc.Blocks.Add(table);
        return doc;
    }
}
