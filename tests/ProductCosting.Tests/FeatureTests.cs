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
        }
        var store = new Store(new Db(path));
        var item = store.GetItemRows(ListScope.RawMaterials, null).Single();
        Assert.Equal("OLD", item.Code);
        Assert.False(store.LoadItem(item.Id)!.Item.HasWaste);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch { }
    }
}
