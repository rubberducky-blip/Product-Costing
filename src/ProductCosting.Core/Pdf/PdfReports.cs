using System.Globalization;
using ProductCosting.Core.Localization;
using ProductCosting.Core.Model;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using S = ProductCosting.Core.Localization.Strings;

namespace ProductCosting.Core.Pdf;

/// <summary>A line of an order PDF; the price is per <see cref="Unit"/>, in <see cref="Currency"/>.</summary>
public sealed record OrderLine(string Code, string? SupplierCode, string Name, string? Variant, decimal Quantity, MeasureUnit Unit,
    decimal UnitPrice = 0, Currency Currency = Currency.Eur);

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

    static CultureInfo Culture => CultureOf(S.Language);

    static CultureInfo CultureOf(string lang) => CultureInfo.GetCultureInfo(lang == "en" ? "en-IE" : "el-GR");

    static string Money(decimal v, string symbol) => v.ToString("N2", Culture) + " " + symbol;

    static void Page(PageDescriptor page, string title, string? subtitle, DateTime now, string? lang = null, float fontSize = 9)
    {
        lang ??= S.Language;
        var culture = CultureOf(lang);
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.DefaultTextStyle(x => x.FontSize(fontSize).FontFamily("Inter"));
        page.Header().PaddingBottom(14).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(title).FontSize(18).SemiBold().FontColor(Accent);
                if (!string.IsNullOrWhiteSpace(subtitle)) col.Item().PaddingTop(2).Text(subtitle).FontSize(10).FontColor(Muted);
            });
            row.ConstantItem(180).AlignRight().AlignBottom().Text(string.Format(S.Get("pdf.generated", lang), now.ToString("g", culture))).FontSize(8).FontColor(Muted);
        });
        page.Footer().AlignCenter().Text(t =>
        {
            t.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
            var parts = S.Get("pdf.page", lang).Split(["{0}", "{1}"], StringSplitOptions.None);
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
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(n.CurrentPrice, currency) + n.PerUnitText).SemiBold();
                    table.Cell().Element(BodyCell).Text(t =>
                    {
                        t.Span(S.Reasons(n.Reasons));
                        if (n.Adjusted) t.Span(" · " + S.Get("notify.statusAdjusted")).FontColor(Accent);
                    });
                }
            });
        })).GeneratePdf();
    }

    /// <summary>
    /// An order for a supplier, laid out as <paramref name="options"/> say (columns, language, text size, and whether the
    /// supplier's details, ours with the logo, both or neither go at the top).
    /// </summary>
    public static byte[] SupplierOrder(Supplier supplier, IReadOnlyList<OrderLine> lines, DateTime now,
        OrderPdfOptions? options = null, CompanyInfo? company = null, byte[]? logo = null)
    {
        var o = options ?? OrderPdfOptions.Default;
        var lang = o.Language;
        var culture = CultureOf(lang);
        string T(string key) => S.Get(key, lang);
        string Unit(MeasureUnit u) => S.Get("mu." + u.ToString().ToLowerInvariant(), lang);
        string Price(decimal v, Currency c) => v.ToString("#,##0.00####", culture) + (c == Currency.Usd ? " $" : " €");
        var showSupplier = o.Header is OrderPdfHeader.Supplier or OrderPdfHeader.Both;
        var showCompany = o.Header is OrderPdfHeader.Company or OrderPdfHeader.Both && (company is { IsEmpty: false } || logo is not null);
        var cols = o.Columns == OrderPdfColumns.None ? OrderPdfColumns.Default : o.Columns;
        bool Has(OrderPdfColumns c) => cols.HasFlag(c);
        var big = o.FontSize + 2;

        return Document.Create(doc => doc.Page(page =>
        {
            Page(page, T("order.pdfTitle"), o.Header == OrderPdfHeader.None ? null : supplier.Name, now, lang, o.FontSize);
            page.Content().Column(col =>
            {
                // our company (logo and details)
                if (showCompany)
                    col.Item().PaddingBottom(12).Row(row =>
                    {
                        if (logo is not null) row.ConstantItem(110).Height(56).AlignLeft().AlignMiddle().Image(logo).FitArea();
                        if (company is { IsEmpty: false } cmp)
                            row.RelativeItem().PaddingLeft(logo is null ? 0 : 12).Column(info =>
                            {
                                if (!string.IsNullOrWhiteSpace(cmp.Name)) info.Item().Text(cmp.Name).FontSize(big).SemiBold();
                                var tax = string.Join(" · ", new[]
                                {
                                    string.IsNullOrWhiteSpace(cmp.VatNumber) ? null : $"{T("company.vat")}: {cmp.VatNumber}",
                                    string.IsNullOrWhiteSpace(cmp.TaxOffice) ? null : $"{T("company.taxOffice")}: {cmp.TaxOffice}",
                                }.OfType<string>());
                                foreach (var v in new[] { cmp.Address, tax, string.Join(" · ", new[] { cmp.Phone, cmp.Email }.Where(x => !string.IsNullOrWhiteSpace(x))) })
                                    if (!string.IsNullOrWhiteSpace(v)) info.Item().Text(v).FontColor(Muted);
                            });
                        else row.RelativeItem();
                    });
                // the supplier (who the order is for) and the date
                col.Item().PaddingBottom(12).Row(row =>
                {
                    row.RelativeItem().Column(info =>
                    {
                        if (!showSupplier) return;
                        if (showCompany) info.Item().Text(T("order.pdfTo")).FontColor(Muted);
                        info.Item().Text(supplier.Name).FontSize(big).SemiBold();
                        var country = Countries.Find(supplier.Country) is { } k ? (lang == "en" ? k.En : k.El) : supplier.Country;
                        foreach (var v in new[] { country, supplier.Email, supplier.Phone })
                            if (!string.IsNullOrWhiteSpace(v)) info.Item().Text(v).FontColor(Muted);
                    });
                    row.ConstantItem(160).AlignRight().Column(info =>
                    {
                        info.Item().AlignRight().Text(T("order.date")).FontColor(Muted);
                        info.Item().AlignRight().Text(now.ToString("d", culture)).FontSize(big).SemiBold();
                    });
                });
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        if (Has(OrderPdfColumns.Number)) c.ConstantColumn(24);
                        if (Has(OrderPdfColumns.SupplierCode)) c.ConstantColumn(90);
                        if (Has(OrderPdfColumns.OurCode)) c.ConstantColumn(80);
                        if (Has(OrderPdfColumns.Name)) c.RelativeColumn(3);
                        if (Has(OrderPdfColumns.Variant)) c.RelativeColumn(2);
                        if (Has(OrderPdfColumns.Quantity)) c.ConstantColumn(70);
                        if (Has(OrderPdfColumns.UnitPrice)) c.ConstantColumn(70);
                        if (Has(OrderPdfColumns.Value)) c.ConstantColumn(75);
                    });
                    table.Header(h =>
                    {
                        if (Has(OrderPdfColumns.Number)) h.Cell().Element(HeaderCell).Text("#");
                        if (Has(OrderPdfColumns.SupplierCode)) h.Cell().Element(HeaderCell).Text(T("list.supplierCode"));
                        if (Has(OrderPdfColumns.OurCode)) h.Cell().Element(HeaderCell).Text(T("order.ourCode"));
                        if (Has(OrderPdfColumns.Name)) h.Cell().Element(HeaderCell).Text(T("list.name"));
                        if (Has(OrderPdfColumns.Variant)) h.Cell().Element(HeaderCell).Text(T("item.variant"));
                        if (Has(OrderPdfColumns.Quantity)) h.Cell().Element(HeaderCell).AlignRight().Text(T("order.qty"));
                        if (Has(OrderPdfColumns.UnitPrice)) h.Cell().Element(HeaderCell).AlignRight().Text(T("order.pdfUnitPrice"));
                        if (Has(OrderPdfColumns.Value)) h.Cell().Element(HeaderCell).AlignRight().Text(T("order.value"));
                    });
                    var i = 1;
                    foreach (var l in lines)
                    {
                        if (Has(OrderPdfColumns.Number)) table.Cell().Element(BodyCell).Text((i++).ToString(culture)).FontColor(Muted);
                        if (Has(OrderPdfColumns.SupplierCode)) table.Cell().Element(BodyCell).Text(l.SupplierCode ?? "").SemiBold();
                        if (Has(OrderPdfColumns.OurCode)) table.Cell().Element(BodyCell).Text(l.Code);
                        if (Has(OrderPdfColumns.Name)) table.Cell().Element(BodyCell).Text(l.Name);
                        if (Has(OrderPdfColumns.Variant)) table.Cell().Element(BodyCell).Text(l.Variant ?? "");
                        if (Has(OrderPdfColumns.Quantity)) table.Cell().Element(BodyCell).AlignRight().Text(l.Quantity.ToString("0.###", culture) + " " + Unit(l.Unit)).SemiBold();
                        if (Has(OrderPdfColumns.UnitPrice)) table.Cell().Element(BodyCell).AlignRight().Text(Price(l.UnitPrice, l.Currency));
                        if (Has(OrderPdfColumns.Value)) table.Cell().Element(BodyCell).AlignRight().Text((l.UnitPrice * l.Quantity).ToString("N2", culture) + (l.Currency == Currency.Usd ? " $" : " €"));
                    }
                });
                col.Item().PaddingTop(10).AlignRight().Column(sum =>
                {
                    sum.Item().AlignRight().Text(t =>
                    {
                        t.Span(T("order.totalLines") + ": ").FontColor(Muted);
                        t.Span(lines.Count.ToString(culture)).SemiBold();
                    });
                    // with the value column: the total per currency
                    if (Has(OrderPdfColumns.Value))
                        foreach (var g in lines.GroupBy(l => l.Currency))
                            sum.Item().AlignRight().Text(t =>
                            {
                                t.Span(T("order.totalValue") + ": ").FontColor(Muted);
                                t.Span(g.Sum(l => l.UnitPrice * l.Quantity).ToString("N2", culture) + (g.Key == Currency.Usd ? " $" : " €")).SemiBold();
                            });
                });
            });
        })).GeneratePdf();
    }
}
