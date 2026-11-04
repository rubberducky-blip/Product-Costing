using ProductCosting.Core.Calc;
using Dapper;
using Microsoft.Data.Sqlite;
using ProductCosting.Core.Data;
using ProductCosting.Core.Model;

namespace ProductCosting.Tests;

public class FeatureTests
{
    [Fact]
    public void Waste_OnMaterial_AndOnRecipeLine()
    {
        using var f = new StoreFixture();
        // 2 m² of leather for 140 €, 30% thrown away → the usable 1,4 m² carry the whole price
        var leather = f.Store.SaveItem(new ItemDocument
        {
            Item = new Item { Kind = ItemKind.RawMaterial, Code = "L", Name = "Σαράτσικο", Unit = QtyUnit.Cm2, HasWaste = true, WastePct = 30 },
            Sources = [new ItemSource { PurchasePrice = 140, PurchaseQty = 20000 }],
        });
        Assert.Equal(0.01m, f.Store.GetItem(leather)!.Cost);   // 140 / 20000 / 0,7

        // a strap uses 30 cm² with 10% extra waste → 33 cm²
        var strap = f.Store.SaveItem(new ItemDocument
        {
            Item = new Item { Kind = ItemKind.Product, Code = "S", Name = "Λουρί" },
            Lines = [new RecipeLine { MaterialId = leather, Quantity = 30, Unit = QtyUnit.Cm2, WastePct = 10 }],
        });
        Assert.Equal(0.33m, f.Store.GetItem(strap)!.CostMaterials);

        // waste is ignored when the material doesn't have it switched on
        var doc = f.Store.LoadItem(leather)!;
        doc.Item.HasWaste = false;
        f.Store.SaveItem(doc);
        Assert.Equal(0.007m, f.Store.GetItem(leather)!.Cost);
        Assert.Null(f.Store.GetItem(leather)!.WastePct);
    }

    [Fact]
    public void ProductsInsideProducts_RepriceUpTheChain()
    {
        using var f = new StoreFixture();
        var sup = f.Supplier("Acme");
        var mat = f.Bought(ItemKind.RawMaterial, "M", 10, sup);
        var merch = f.Bought(ItemKind.Merchandise, "G", 4, sup);   // plain merchandise, not marked as raw material
        var basic = f.Product("BASE", [(mat, 1, QtyUnit.Pcs)]);
        var plus = f.Product("PLUS", [(basic, 1, QtyUnit.Pcs), (merch, 1, QtyUnit.Pcs)]);
        var gift = f.Product("GIFT", [(plus, 2, QtyUnit.Pcs)]);
        Assert.Equal(14m, f.Store.GetItem(plus)!.Cost);
        Assert.Equal(28m, f.Store.GetItem(gift)!.Cost);

        var s = f.Store.GetSupplier(sup)!;
        s.DiscountPct = 50;
        f.Store.SaveSupplier(s);
        Assert.Equal(7m, f.Store.GetItem(plus)!.Cost);
        Assert.Equal(14m, f.Store.GetItem(gift)!.Cost);
        var n = f.Store.GetNotifications().Single(x => x.ItemId == gift);
        Assert.Equal(56m, n.OldPrice);   // one notification per item, first old → last new price
        Assert.Equal(28m, n.NewPrice);
        Assert.Contains(f.Store.GetUsage(basic), u => u.ProductId == plus);

        // a product can't contain itself, even through another product
        var doc = f.Store.LoadItem(basic)!;
        doc.Lines.Add(new RecipeLine { MaterialId = gift, Quantity = 1 });
        Assert.Throws<RecipeCycleException>(() => f.Store.SaveItem(doc));
    }

    [Fact]
    public void Duplicate_ItemAndSupplier()
    {
        using var f = new StoreFixture();
        var sup = f.Supplier("Acme");
        var src = f.Store.SaveItem(new ItemDocument
        {
            Item = new Item { Kind = ItemKind.Merchandise, Code = "M-1", Name = "Κούπα" },
            Sources = [new ItemSource { SupplierId = sup, SupplierCode = "X", PurchasePrice = 3, Moq = 12, PackQty = 6 }],
            Variants = [new ItemVariant { Name = "Μπλε", Code = "X-B" }],
        });
        var copy = f.Store.DuplicateItem(src);
        var copy2 = f.Store.DuplicateItem(src);
        var d = f.Store.LoadItem(copy)!;
        Assert.Equal("M-1-2", d.Item.Code);
        Assert.Equal("M-1-3", f.Store.GetItem(copy2)!.Code);
        Assert.Equal(12m, d.Sources[0].Moq);
        Assert.Equal(6m, d.Sources[0].PackQty);
        Assert.Single(d.Variants);
        Assert.Equal(f.Store.GetItem(src)!.Price, d.Item.Price);
        var row = f.Store.GetSupplierItems(sup).First(r => r.ItemId == src);
        Assert.Equal(12m, row.Moq);

        Assert.Equal("Acme (2)", f.Store.GetSupplier(f.Store.DuplicateSupplier(sup))!.Name);
        Assert.Equal(f.Store.GetItem(src)!.EffMarkup, f.Store.GetItemRows(ListScope.Merchandise, null).First(r => r.Id == src).EffMarkup);
    }

    [Fact]
    public void SetPrice_RoundsThenAdjustsMarkup_AndPurchasePrice()
    {
        using var f = new StoreFixture();
        var sup = f.Supplier("Acme");
        var m = f.Bought(ItemKind.Merchandise, "M", 10, sup);
        Assert.Equal(25.00m, f.Store.SetPrice(m, 24.98m));      // rounded to the 0,05 step first
        var it = f.Store.GetItem(m)!;
        Assert.Equal(25.00m, it.Price);
        Assert.Equal(150m, it.EffMarkup);
        Assert.Contains(f.Store.GetHistory(m), h => h.Reasons.HasFlag(PriceReason.MarginAdjustment));

        Assert.True(f.Store.SetPurchasePrice(m, 12));
        Assert.Equal(12m, f.Store.GetItem(m)!.Cost);
        var doc = f.Store.LoadItem(m)!;
        doc.Sources.Add(new ItemSource { PurchasePrice = 5 });
        f.Store.SaveItem(doc);
        Assert.False(f.Store.SetPurchasePrice(m, 1));            // two suppliers: not from the list
        Assert.Equal(2, f.Store.GetItemRows(ListScope.Merchandise, null).Single().SourceCount);
    }

    [Fact]
    public void SavedOrders_AndVariantsPerSupplier()
    {
        using var f = new StoreFixture();
        var a = f.Supplier("A");
        var b = f.Supplier("B");
        var m = f.Store.SaveItem(new ItemDocument
        {
            Item = new Item { Kind = ItemKind.Merchandise, Code = "M", Name = "Κούπα" },
            Sources = [new ItemSource { SupplierId = a, PurchasePrice = 1 }, new ItemSource { SupplierId = b, PurchasePrice = 2 }],
            Variants = [new ItemVariant { Name = "Μπλε", SupplierId = a }, new ItemVariant { Name = "Κόκκινη" }, new ItemVariant { Name = "Χ", SupplierId = 999 }],
        });
        var vs = f.Store.LoadItem(m)!.Variants;
        Assert.Equal(a, vs[0].SupplierId);
        Assert.Null(vs[1].SupplierId);
        Assert.Null(vs[2].SupplierId);   // not one of the item's suppliers → every supplier

        var id = f.Store.SaveOrder(null, a, [new PurchaseOrderLine { ItemId = m, Code = "M", Name = "Κούπα", Variant = "Μπλε", Quantity = 12 }]);
        f.Store.SaveOrder(id, a, [new PurchaseOrderLine { ItemId = m, Code = "M", Name = "Κούπα", Variant = "Μπλε", Quantity = 24 },
                                  new PurchaseOrderLine { ItemId = m, Code = "M", Name = "Κούπα", Quantity = 6 }]);
        var order = Assert.Single(f.Store.GetOrders(a));
        Assert.Equal(2, order.LineCount);
        Assert.Equal(30m, order.TotalQuantity);
        Assert.Equal(24m, f.Store.GetOrderLines(id)[0].Quantity);
        Assert.Empty(f.Store.GetOrders(b));
        f.Store.DeleteOrder(id);
        Assert.Empty(f.Store.GetOrders(a));
    }

    [Fact]
    public void OrderTotals_StatusesAndValues()
    {
        // 10 × 2 € with 10% discount and 5% shipping; VAT 24% on goods − discount; shipping is estimated after the total
        var t = ProductCosting.Core.Calc.Costing.Order(
            [new PurchaseOrderLine { Quantity = 10, UnitPrice = 2, DiscountPct = 10, ShippingPct = 5 }], 24, Currency.Eur, 1.1m);
        Assert.Equal(20m, t.Goods);
        Assert.Equal(2m, t.Discount);
        Assert.Equal(0.9m, t.Shipping);
        Assert.Equal(4.32m, t.Vat);
        Assert.Equal(22.32m, t.Total);
        Assert.Equal(23.22m, t.TotalWithShipping);
        // dollars into a euro total
        Assert.Equal(10m, ProductCosting.Core.Calc.Costing.Order([new PurchaseOrderLine { Quantity = 1, UnitPrice = 11, Currency = Currency.Usd }], 0, Currency.Eur, 1.1m).Total);

        using var f = new StoreFixture();
        var sup = f.Store.SaveSupplier(new Supplier { Name = "A", VatPct = 24 });
        Assert.Equal(24m, f.Store.GetSupplier(sup)!.VatPct);
        var id = f.Store.SaveOrder(null, sup, [new PurchaseOrderLine { Code = "X", Name = "X", Quantity = 3, UnitPrice = 2.5m }]);
        var o = f.Store.GetOrders(sup).Single();
        Assert.Equal(OrderStatus.Draft, o.Status);
        Assert.Equal(9.3m, o.TotalValue); // 7.5 + 24% VAT
        Assert.Equal(24m, o.VatPct);
        f.Store.SetOrderStatus(id, OrderStatus.Archived);                 // not before it is executed
        Assert.Equal(OrderStatus.Draft, f.Store.GetOrder(id)!.Status);
        f.Store.SetOrderStatus(id, OrderStatus.Executed);
        Assert.NotNull(f.Store.GetOrder(id)!.ExecutedAt);
        Assert.Throws<InvalidOperationException>(() => f.Store.SaveOrder(id, sup, []));
        f.Store.SetOrderStatus(id, OrderStatus.Archived);
        Assert.Equal(OrderStatus.Archived, f.Store.GetOrders().Single().Status);
        Assert.Equal("A", f.Store.GetOrders().Single().SupplierName);
    }

    [Fact]
    public void SupplierHistory_AverageChangePerDay()
    {
        using var f = new StoreFixture();
        var sup = f.Supplier("Acme");
        var a = f.Bought(ItemKind.Merchandise, "A", 10, sup);
        var b = f.Bought(ItemKind.Merchandise, "B", 20, sup);
        f.Bought(ItemKind.Merchandise, "C", 30, sup);
        f.Now = f.Now.AddDays(1);
        f.Store.SetPurchasePrice(a, 11);       // +10%
        var doc = f.Store.LoadItem(b)!;
        doc.Sources[0].PurchasePrice = 24;     // +20%
        f.Store.SaveItem(doc);
        var day = Assert.Single(f.Store.GetSupplierHistory(sup));
        Assert.Equal(2, day.Changed);
        Assert.Equal(3, day.Total);
        Assert.Equal(15m, Math.Round(day.AvgChangePct, 6));
        f.Store.SetPurchasePrice(a, 10);       // back to the day's start: no longer a change
        Assert.Equal(1, f.Store.GetSupplierHistory(sup).Single().Changed);
    }

    [Fact]
    public void Units_EnteredInMetresAndGrams_StoredInBaseUnits()
    {
        Assert.Equal(250m, MeasureUnit.G.FromBase(0.25m));
        Assert.Equal(0.25m, MeasureUnit.G.ToBase(250));
        Assert.Equal(MeasureUnit.Cm2, QtyUnit.Cm2.EntryUnit(MeasureUnit.Kg)); // a unit of another dimension falls back

        using var f = new StoreFixture();
        // 2 m of leather for 10 €: 0.05 € per cm
        var leather = f.Store.SaveItem(new ItemDocument
        {
            Item = new Item { Kind = ItemKind.RawMaterial, Code = "L", Name = "L", Unit = QtyUnit.Cm },
            Sources = [new ItemSource { PurchasePrice = 10, PurchaseQty = MeasureUnit.M.ToBase(2), PurchaseUnit = MeasureUnit.M }],
        });
        var item = f.Store.GetItem(leather)!;
        Assert.Equal(0.05m, item.Cost);                              // per cm, for recipes
        Assert.Equal(MeasureUnit.M, item.PriceUnit);                 // priced per metre, like it is bought
        Assert.Equal(5m, item.SaleCost);
        Assert.Equal(10m, item.Price);                               // 5 € + 100% markup
        Assert.Equal(MeasureUnit.M, f.Store.LoadItem(leather)!.Sources[0].PurchaseUnit);
        var row = f.Store.GetItemRows(ListScope.RawMaterials, null).Single(r => r.Id == leather);
        Assert.Equal(5m, row.Cost);
        Assert.Equal(50m, Math.Round(row.MarginPct, 6));
        Assert.Equal(12m, f.Store.SetPrice(leather, 12));            // the markup follows the price per metre
        Assert.Equal(140m, Math.Round(f.Store.GetItem(leather)!.EffMarkup, 6));
        // a recipe line typed in metres: 0.3 m = 30 cm
        var p = f.Store.SaveItem(new ItemDocument
        {
            Item = new Item { Kind = ItemKind.Product, Code = "P", Name = "P" },
            Lines = [new RecipeLine { MaterialId = leather, Quantity = 30, Unit = QtyUnit.Cm, EntryUnit = MeasureUnit.M }],
        });
        var line = f.Store.LoadItem(p)!.Lines[0];
        Assert.Equal(MeasureUnit.M, line.EntryUnit);
        Assert.Equal(0.3m, MeasureUnit.M.FromBase(line.Quantity));
        Assert.Equal(1.5m, f.Store.GetItem(p)!.CostMaterials);
    }

    [Fact]
    public void Orders_InThePurchaseUnit()
    {
        using var f = new StoreFixture();
        var sup = f.Supplier("Hides");
        // 1.5 m² for 90 €, ordered in 3 m² minimums and 1.5 m² packs
        f.Store.SaveItem(new ItemDocument
        {
            Item = new Item { Kind = ItemKind.RawMaterial, Code = "L", Name = "Leather", Unit = QtyUnit.Cm2 },
            Sources = [new ItemSource { SupplierId = sup, PurchasePrice = 90, PurchaseQty = 15000, PurchaseUnit = MeasureUnit.M2, Moq = 30000, PackQty = 15000 }],
        });
        var row = Assert.Single(f.Store.GetSupplierItems(sup));
        Assert.Equal(MeasureUnit.M2, row.EntryUnit);
        Assert.Equal(1.5m, row.EntryQty);
        Assert.Equal(60m, row.EntryUnitPrice);
        Assert.Equal(3m, row.EntryMoq);
        Assert.Equal(1.5m, row.EntryPack);

        var id = f.Store.SaveOrder(null, sup,
        [
            new PurchaseOrderLine { Code = "L", Name = "Leather", Unit = QtyUnit.Cm2, EntryUnit = MeasureUnit.M2, Quantity = 3, UnitPrice = 60 },
            new PurchaseOrderLine { Code = "OLD", Name = "Older line", Unit = QtyUnit.Cm2, Quantity = 100, UnitPrice = 0.01m },
        ]);
        var lines = f.Store.GetOrderLines(id);
        Assert.Equal(MeasureUnit.M2, lines[0].ShownUnit);
        Assert.Equal(MeasureUnit.Cm2, lines[1].ShownUnit);           // saved before units: still in cm²
        Assert.Equal(181m, f.Store.GetOrder(id)!.TotalValue);
    }

    [Fact]
    public void History_RecordsMarginChanges_EvenWhenThePriceStays()
    {
        using var f = new StoreFixture();
        var m = f.Bought(ItemKind.Merchandise, "M", 10);            // price 20,00, margin 50%
        f.Now = f.Now.AddDays(1);
        var doc = f.Store.LoadItem(m)!;
        doc.Sources[0].PurchasePrice = 10.01m;                       // 20,02 rounds back to 20,00
        f.Store.SaveItem(doc);
        Assert.Equal(20m, f.Store.GetItem(m)!.Price);
        var h = f.Store.GetHistory(m);
        Assert.Equal(2, h.Count);
        Assert.Equal(50m, Math.Round(h[1].OldMargin!.Value, 4));
        Assert.Equal(49.95m, Math.Round(h[1].Margin!.Value, 4));
        Assert.Equal(-1, h[1].MarginTrend);
        Assert.Empty(f.Store.GetNotifications());                   // no price change, nothing to notify

        // the sales overhead lowers the adjusted margin of bought items only, never prices
        Assert.Equal(25.77m, ProductCosting.Core.Calc.Costing.AdjustedMargin(50m, ItemKind.Merchandise, 24.23m));
        Assert.Equal(50m, ProductCosting.Core.Calc.Costing.AdjustedMargin(50m, ItemKind.Product, 24.23m));
        var s = f.Store.LoadSettings();
        s.SalesOverheadPct = 24.23m;
        f.Store.SaveSettings(s);
        Assert.Equal(24.23m, f.Store.LoadSettings().SalesOverheadPct);
        Assert.Equal(20m, f.Store.GetItem(m)!.Price);
    }

    [Fact]
    public void Import_FromSpreadsheetRows()
    {
        using var f = new StoreFixture();
        var acme = f.Supplier("Acme");
        var existing = f.Bought(ItemKind.Merchandise, "M-1", 5, acme);
        var file = ProductCosting.Core.Import.ItemImport.Parse(
        [
            ["Κωδικός", "Προμηθευτής", "Όνομα", "Κωδικός προμηθευτή", "MOQ", "Κόστος αγοράς", "Τιμή πώλησης", "Τύπος"],
            ["M-1", "ACME", "Κούπα", "A-1", "12", "4", "10", "Εμπόρευμα"],
            ["R-9", "Νέος", "Δέρμα", "N-9", "", "2,50", "6,00", "πρώτη ύλη"],
            ["X-1", "Acme", "Λάθος", "", "", "1", "", "κάτι άλλο"],
        ]);
        Assert.Equal(2, file.Rows.Count);
        Assert.Contains(file.Problems, p => p is { Line: 4, Reason: "kind" });
        Assert.Equal(2.5m, file.Rows[1].Cost);

        // "Νέος" is not a supplier yet: the app asks; here it goes to Acme
        var map = new Dictionary<string, long> { [TextNorm.Normalize("Acme")] = acme, [TextNorm.Normalize("Νέος")] = acme };
        var result = f.Store.ImportItems(file.Rows, map);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Empty(result.Problems);

        var m1 = f.Store.LoadItem(existing)!;
        Assert.Equal("Κούπα", m1.Item.Name);
        Assert.Equal(4m, m1.Sources.Single().PurchasePrice);
        Assert.Equal("A-1", m1.Sources[0].SupplierCode);
        Assert.Equal(12m, m1.Sources[0].Moq);
        Assert.Equal(10m, f.Store.GetItem(existing)!.Price);              // markup worked out from cost and price
        Assert.Equal(150m, Math.Round(f.Store.GetItem(existing)!.EffMarkup, 4));

        var r9 = f.Store.GetItemRows(ListScope.RawMaterials, null).Single(r => r.Code == "R-9");
        Assert.Equal(6m, r9.Price);
        Assert.Equal(acme, f.Store.LoadItem(r9.Id)!.Sources[0].SupplierId);
    }

    [Fact]
    public void Spreadsheet_ReadsXlsx()
    {
        var path = Path.Combine(Path.GetTempPath(), "pc-xlsx-" + Guid.NewGuid().ToString("N") + ".xlsx");
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            void Add(string name, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(xml);
            }
            const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Add("xl/workbook.xml", $"<workbook xmlns=\"{ns}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Φύλλο1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"worksheet\" Target=\"worksheets/data.xml\"/></Relationships>");
            Add("xl/sharedStrings.xml", $"<sst xmlns=\"{ns}\"><si><t>Κωδικός</t></si><si><t>Τιμή πώλησης</t></si><si><r><t>Κού</t></r><r><t>πα</t></r></si></sst>");
            Add("xl/worksheets/data.xml", $"<worksheet xmlns=\"{ns}\"><sheetData>"
                + "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"C1\" t=\"s\"><v>1</v></c></row>"
                + "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>2</v></c><c r=\"C2\"><v>12.5</v></c></row>"
                + "</sheetData></worksheet>");
        }
        var rows = ProductCosting.Core.Import.Spreadsheet.Read(path);
        File.Delete(path);
        Assert.Equal(2, rows.Count);
        Assert.Equal(["Κωδικός", "", "Τιμή πώλησης"], rows[0]);
        Assert.Equal("Κούπα", rows[1][0]);
        Assert.Equal(12.5m, ProductCosting.Core.Import.Spreadsheet.Number(rows[1][2]));
        Assert.Equal(1234.5m, ProductCosting.Core.Import.Spreadsheet.Number("1.234,50 €"));
    }

    [Fact]
    public void Spreadsheet_ToleratesOddPackages_AndExplainsErrors()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pc-xlsx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "odd.xlsx");
        // backslashes and capitals in the part names, the strict namespace, the workbook found through _rels/.rels
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            void Add(string name, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(xml);
            }
            const string ns = "http://purl.oclc.org/ooxml/spreadsheetml/main";
            Add("_rels\\.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"r1\" Type=\"http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument\" Target=\"XL/Book.xml\"/></Relationships>");
            Add("XL\\Book.xml", $"<workbook xmlns=\"{ns}\" xmlns:r=\"http://purl.oclc.org/ooxml/officeDocument/relationships\"><sheets><sheet name=\"A\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("XL\\_rels\\Book.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"x/worksheet\" Target=\"Sheets/One.xml\"/></Relationships>");
            Add("XL\\Sheets\\One.xml", $"<worksheet xmlns=\"{ns}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>M-1</t></is></c><c r=\"B1\"><v>3</v></c></row></sheetData></worksheet>");
        }
        var rows = ProductCosting.Core.Import.Spreadsheet.Read(path);
        Assert.Equal(["M-1", "3"], rows.Single());

        // an old .xls and a renamed text file get messages that say what is wrong
        var xls = Path.Combine(dir, "old.xlsx");
        File.WriteAllBytes(xls, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0]);
        Assert.Contains(".xls", Assert.Throws<ProductCosting.Core.Import.SpreadsheetException>(() => ProductCosting.Core.Import.Spreadsheet.Read(xls)).Message);
        var txt = Path.Combine(dir, "text.xlsx");
        File.WriteAllText(txt, "hello");
        Assert.Throws<ProductCosting.Core.Import.SpreadsheetException>(() => ProductCosting.Core.Import.Spreadsheet.Read(txt));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Upgrade_FromSchemaVersion1()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pc-up-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "old.db");
        using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            c.Open();
            c.Execute(typeof(Db).Assembly.GetType("ProductCosting.Core.Data.Schema")!.GetField("V1")!.GetValue(null) as string ?? "");
            c.Execute("PRAGMA user_version = 1");
            c.Execute("INSERT INTO items(kind, code, name, created_at, updated_at) VALUES(3, 'OLD', 'Old', '', '')");
            c.Execute("INSERT INTO suppliers(name, country, created_at, updated_at) VALUES('GR', 'GR', '', ''), ('IT', 'IT', '', '')");
            // leather bought as 15000 cm² for 90 €, priced per cm² before
            c.Execute("INSERT INTO items(kind, code, name, unit, cost, price, created_at, updated_at) VALUES(3, 'LEA', 'Leather', 2, 0.006, 0.0, '', '')");
            c.Execute("INSERT INTO item_sources(item_id, purchase_price, purchase_qty) VALUES((SELECT id FROM items WHERE code = 'LEA'), 90, 15000)");
        }
        var store = new Store(new Db(path));
        var item = store.GetItemRows(ListScope.RawMaterials, null).Single(r => r.Code == "OLD");
        Assert.Equal("OLD", item.Code);
        Assert.False(store.LoadItem(item.Id)!.Item.HasWaste);
        // area purchases are shown in m² and priced per m² after the upgrade: 60 €/m² + 100% markup
        var lea = store.LoadItem(store.GetItemRows(ListScope.RawMaterials, null).Single(r => r.Code == "LEA").Id)!;
        Assert.Equal(MeasureUnit.M2, lea.Sources[0].PurchaseUnit);
        Assert.Equal(MeasureUnit.M2, lea.Item.PriceUnit);
        Assert.Equal(120m, lea.Item.Price);
        Assert.Contains(store.GetHistory(lea.Item.Id), h => h.Reasons.HasFlag(PriceReason.PriceUnit));
        Assert.Empty(store.GetNotifications());
        // Greek suppliers get the 24% VAT default, others keep none
        Assert.Equal([24m, 0m], store.GetSuppliers().OrderBy(s => s.Name).Select(s => s.VatPct));
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch { }
    }
}
