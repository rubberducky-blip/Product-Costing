using System.Globalization;
using ProductCosting.Core.Localization;
using ProductCosting.Core.Model;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using S = ProductCosting.Core.Localization.Strings;

namespace ProductCosting.Core.Pdf;

public sealed record OrderLine(string Code, string? SupplierCode, string Name, string? Variant, decimal Quantity, QtyUnit Unit);

/// <summary>PDF exports: price-change report and supplier order.</summary>
public static class PdfReports
{
    static readonly string Accent = "#2563EB";
    static readonly string Muted = "#6B7280";
    static readonly string Line = "#E5E7EB";

    static PdfReports()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = false;
        // never fail an export over a missing fallback font (QuestPDF looks for Lato next to the exe); Inter is embedded below
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
        // Inter (SIL Open Font License) is embedded so Greek renders the same on every PC
        var asm = typeof(PdfReports).Assembly;
        foreach (var name in new[] { "Fonts.Inter-Regular.otf", "Fonts.Inter-SemiBold.otf" })
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            QuestPDF.Drawing.FontManager.RegisterFontFromStream(stream);
        }
    }

    static CultureInfo Culture => CultureInfo.GetCultureInfo(S.Language == "en" ? "en-IE" : "el-GR");

    static string Money(decimal v, string symbol) => v.ToString("N2", Culture) + " " + symbol;

    static void Page(PageDescriptor page, string title, string? subtitle, DateTime now)
    {
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Inter"));
        page.Header().PaddingBottom(14).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(title).FontSize(18).SemiBold().FontColor(Accent);
                if (!string.IsNullOrWhiteSpace(subtitle)) col.Item().PaddingTop(2).Text(subtitle).FontSize(10).FontColor(Muted);
            });
            row.ConstantItem(180).AlignRight().AlignBottom().Text(S.Format("pdf.generated", now.ToString("g", Culture))).FontSize(8).FontColor(Muted);
        });
        page.Footer().AlignCenter().Text(t =>
        {
            t.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
            var parts = S.Get("pdf.page").Split(["{0}", "{1}"], StringSplitOptions.None);
            t.Span(parts[0]);
            t.CurrentPageNumber();
            t.Span(parts.Length > 1 ? parts[1] : " / ");
            t.TotalPages();
            if (parts.Length > 2) t.Span(parts[2]);
        });
    }

    static IContainer HeaderCell(IContainer c) =>
        c.Background("#F3F4F6").BorderBottom(1).BorderColor("#D1D5DB").PaddingVertical(5).PaddingHorizontal(4).DefaultTextStyle(x => x.SemiBold());

    static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4).PaddingHorizontal(4);

    public static byte[] PriceChanges(IReadOnlyList<PriceNotification> rows, string currency, DateTime now)
    {
        return Document.Create(doc => doc.Page(page =>
        {
            Page(page, S.Get("notify.pdfTitle"), S.Format("notify.count", rows.Count), now);
            page.Size(PageSizes.A4.Landscape());
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(70);   // code
                    c.RelativeColumn(3);    // name
                    c.ConstantColumn(70);   // type
                    c.ConstantColumn(62);   // old
                    c.ConstantColumn(62);   // new
                    c.ConstantColumn(52);   // change
                    c.ConstantColumn(62);   // final
                    c.RelativeColumn(3);    // reasons
                });
                table.Header(h =>
                {
                    foreach (var key in new[] { "list.code", "list.name", "list.type" })
                        h.Cell().Element(HeaderCell).Text(S.Get(key));
                    foreach (var key in new[] { "notify.oldPrice", "notify.newPrice", "notify.change", "notify.finalPrice" })
                        h.Cell().Element(HeaderCell).AlignRight().Text(S.Get(key));
                    h.Cell().Element(HeaderCell).Text(S.Get("notify.reasons"));
                });
                foreach (var n in rows)
                {
                    var color = n.IsIncrease ? "#B91C1C" : "#15803D";
                    table.Cell().Element(BodyCell).Text(n.Code).SemiBold();
                    table.Cell().Element(BodyCell).Text(n.Name);
                    table.Cell().Element(BodyCell).Text(S.Kind(n.Kind)).FontColor(Muted);
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(n.OldPrice, currency));
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(n.NewPrice, currency));
                    table.Cell().Element(BodyCell).AlignRight().Text((n.ChangePct > 0 ? "+" : "") + n.ChangePct.ToString("N1", Culture) + "%").FontColor(color);
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(n.CurrentPrice, currency)).SemiBold();
                    table.Cell().Element(BodyCell).Text(t =>
                    {
                        t.Span(S.Reasons(n.Reasons));
                        if (n.Adjusted) t.Span(" · " + S.Get("notify.statusAdjusted")).FontColor(Accent);
                    });
                }
            });
        })).GeneratePdf();
    }

    public static byte[] SupplierOrder(Supplier supplier, IReadOnlyList<OrderLine> lines, DateTime now)
    {
        return Document.Create(doc => doc.Page(page =>
        {
            Page(page, S.Get("order.pdfTitle"), supplier.Name, now);
            page.Content().Column(col =>
            {
                col.Item().PaddingBottom(12).Row(row =>
                {
                    row.RelativeItem().Column(info =>
                    {
                        info.Item().Text(supplier.Name).FontSize(11).SemiBold();
                        foreach (var v in new[] { Countries.Name(supplier.Country), supplier.Email, supplier.Phone })
                            if (!string.IsNullOrWhiteSpace(v)) info.Item().Text(v).FontColor(Muted);
                    });
                    row.ConstantItem(160).AlignRight().Column(info =>
                    {
                        info.Item().AlignRight().Text(S.Get("order.date")).FontColor(Muted);
                        info.Item().AlignRight().Text(now.ToString("d", Culture)).FontSize(11).SemiBold();
                    });
                });
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(24);
                        c.ConstantColumn(90);
                        c.ConstantColumn(80);
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                        c.ConstantColumn(70);
                    });
                    table.Header(h =>
                    {
                        h.Cell().Element(HeaderCell).Text("#");
                        h.Cell().Element(HeaderCell).Text(S.Get("list.supplierCode"));
                        h.Cell().Element(HeaderCell).Text(S.Get("order.ourCode"));
                        h.Cell().Element(HeaderCell).Text(S.Get("list.name"));
                        h.Cell().Element(HeaderCell).Text(S.Get("item.variant"));
                        h.Cell().Element(HeaderCell).AlignRight().Text(S.Get("order.qty"));
                    });
                    var i = 1;
                    foreach (var l in lines)
                    {
                        table.Cell().Element(BodyCell).Text((i++).ToString(Culture)).FontColor(Muted);
                        table.Cell().Element(BodyCell).Text(l.SupplierCode ?? "").SemiBold();
                        table.Cell().Element(BodyCell).Text(l.Code);
                        table.Cell().Element(BodyCell).Text(l.Name);
                        table.Cell().Element(BodyCell).Text(l.Variant ?? "");
                        table.Cell().Element(BodyCell).AlignRight().Text(l.Quantity.ToString("0.###", Culture) + " " + S.Unit(l.Unit)).SemiBold();
                    }
                });
                col.Item().PaddingTop(10).AlignRight().Text(t =>
                {
                    t.Span(S.Get("order.totalLines") + ": ").FontColor(Muted);
                    t.Span(lines.Count.ToString(Culture)).SemiBold();
                });
            });
        })).GeneratePdf();
    }
}
