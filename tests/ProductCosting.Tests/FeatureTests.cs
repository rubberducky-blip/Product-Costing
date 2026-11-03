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
