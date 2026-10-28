using System.Diagnostics;
using ProductCosting.Core.Data;
using ProductCosting.Core.Model;
using ProductCosting.Core.Pdf;
using ProductCosting.Core.Services;

namespace ProductCosting.Tests;

public sealed class StoreFixture : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "pc-tests-" + Guid.NewGuid().ToString("N"));
    public Store Store { get; }
    public DateTime Now { get; set; } = new(2026, 10, 1, 9, 0, 0);

    public StoreFixture()
    {
        Directory.CreateDirectory(Dir);
        Store = new Store(new Db(Path.Combine(Dir, "data.db"))) { Clock = () => Now };
        var s = Store.LoadSettings();
        s.LaborRatePerHour = 20;
        s.OverheadRatePerHour = 10;
        s.DefaultMarkupPct = 100;
        s.RoundingStep = 0.05m;
        s.BackupFolder = Path.Combine(Dir, "backups");
        Store.SaveSettings(s);
    }

    public long Supplier(string name, decimal discount = 0, decimal shipping = 0, Currency cur = Currency.Eur) =>
        Store.SaveSupplier(new Supplier { Name = name, DiscountPct = discount, ShippingPct = shipping, Currency = cur });

    public long Bought(ItemKind kind, string code, decimal price, long? supplier = null, decimal qty = 1, QtyUnit unit = QtyUnit.Pcs,
        bool usable = false, decimal? length = null) =>
        Store.SaveItem(new ItemDocument
        {
            Item = new Item { Kind = kind, Code = code, Name = code + " name", Unit = unit, UsableAsMaterial = usable, PieceLengthCm = length },
            Sources = [new ItemSource { SupplierId = supplier, PurchasePrice = price, PurchaseQty = qty }],
        });

    public long Product(string code, IEnumerable<(long Material, decimal Qty, QtyUnit Unit)> lines, params int[] seconds) =>
        Store.SaveItem(new ItemDocument
        {
            Item = new Item { Kind = ItemKind.Product, Code = code, Name = code + " name" },
            Lines = lines.Select(l => new RecipeLine { MaterialId = l.Material, Quantity = l.Qty, Unit = l.Unit }).ToList(),
            Labor = seconds.Select(s => new LaborEntry { Seconds = s }).ToList(),
        });

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Dir, true); } catch { }
    }
}

public class StoreTests
{
    [Fact]
    public void BoughtAndProductPrices()
    {
        using var f = new StoreFixture();
        var sup = f.Supplier("Acme", discount: 10, shipping: 10);
        var fabric = f.Bought(ItemKind.RawMaterial, "FAB", 100, sup, qty: 100, unit: QtyUnit.Cm);
        var item = f.Store.GetItem(fabric)!;
        Assert.Equal(0.99m, item.Cost);      // 100 × 0.9 × 1.1 / 100
        Assert.Equal(2.00m, item.Price);     // 1.98 → 2.00

        var p = f.Product("P1", [(fabric, 50, QtyUnit.Cm)], 600, 300); // 15 min
        var prod = f.Store.GetItem(p)!;
        Assert.Equal(49.5m, prod.CostMaterials);  // 50 cm × 0.99
        Assert.Equal(900, prod.LaborSeconds);
        Assert.Equal(5m, prod.CostLabor);
        Assert.Equal(2.5m, prod.CostOverhead);
        Assert.Equal(57m, prod.Cost);
        Assert.Equal(114m, prod.Price);
    }

    [Fact]
    public void SupplierChange_RepricesItemsAndProducts_AndNotifies()
    {
        using var f = new StoreFixture();
        var sup = f.Supplier("Acme");
        var mat = f.Bought(ItemKind.RawMaterial, "M1", 10, sup);
        var p = f.Product("P1", [(mat, 2, QtyUnit.Pcs)]);
        Assert.Empty(f.Store.GetNotifications()); // own edits don't notify

        var s = f.Store.GetSupplier(sup)!;
        s.DiscountPct = 50;
        f.Store.SaveSupplier(s);

        var notes = f.Store.GetNotifications().ToDictionary(n => n.ItemId);
        Assert.Equal(2, notes.Count);
        Assert.Equal(20m, notes[mat].OldPrice);
        Assert.Equal(10m, notes[mat].NewPrice);
        Assert.Equal(PriceReason.SupplierCostDown, notes[mat].Reasons);
        Assert.Equal(40m, notes[p].OldPrice);
        Assert.Equal(20m, notes[p].NewPrice);
        Assert.Equal(PriceReason.MaterialCostDown, notes[p].Reasons);
        Assert.True(f.Store.GetItem(p)!.HasUnseenChange);
        f.Store.MarkSeen(p);
        Assert.False(f.Store.GetItem(p)!.HasUnseenChange);
    }

    [Fact]
    public void Notifications_MergeAndVanishWhenPriceReturns()
    {
        using var f = new StoreFixture();
        var sup = f.Supplier("Acme");
        var mat = f.Bought(ItemKind.RawMaterial, "M1", 10, sup);
        var s = f.Store.GetSupplier(sup)!;
        s.DiscountPct = 10; f.Store.SaveSupplier(s);
        s.DiscountPct = 20; f.Store.SaveSupplier(s);
        var n = Assert.Single(f.Store.GetNotifications());
        Assert.Equal(20m, n.OldPrice);
        Assert.Equal(16m, n.NewPrice);
        s.DiscountPct = 0; f.Store.SaveSupplier(s);
        Assert.Empty(f.Store.GetNotifications());
    }

    [Fact]
    public void LaborRate_RepricesProducts_WithReason()
    {
        using var f = new StoreFixture();
        var p = f.Product("P1", [], 3600);
        var s = f.Store.LoadSettings();
        s.LaborRatePerHour = 30;
        var sum = f.Store.SaveSettings(s);
        Assert.Equal(1, sum.PriceChanges);
        var n = Assert.Single(f.Store.GetNotifications());
        Assert.Equal(PriceReason.LaborUp, n.Reasons);
        Assert.Equal(60m, n.OldPrice);
        Assert.Equal(80m, n.NewPrice);
    }

    [Fact]
    public void ExchangeRate_RepricesDollarItems()
    {
        using var f = new StoreFixture();
        var usd = f.Supplier("US Inc", cur: Currency.Usd);
        var eur = f.Supplier("EU Ltd");
        var a = f.Bought(ItemKind.Merchandise, "A", 110, usd);
        var b = f.Bought(ItemKind.Merchandise, "B", 110, eur);
        Assert.Equal(100m, f.Store.GetItem(a)!.Cost);
        var s = f.Store.LoadSettings();
        s.UsdPerEur = 1.0m;
        f.Store.SaveSettings(s);
        Assert.Equal(110m, f.Store.GetItem(a)!.Cost);
        Assert.Equal(110m, f.Store.GetItem(b)!.Cost);
        var n = Assert.Single(f.Store.GetNotifications());
        Assert.Equal(a, n.ItemId);
        Assert.Equal(PriceReason.ExchangeRate, n.Reasons);

        // switching the supplier to euro follows on items that use the supplier's currency
        var sup = f.Store.GetSupplier(usd)!;
        s.UsdPerEur = 2m; f.Store.SaveSettings(s);
        Assert.Equal(55m, f.Store.GetItem(a)!.Cost);
        sup.Currency = Currency.Eur; f.Store.SaveSupplier(sup);
        Assert.Equal(110m, f.Store.GetItem(a)!.Cost);
    }

    [Fact]
    public void MarginAdjustment_RestoresOldPrice()
    {
        using var f = new StoreFixture();
        var sup = f.Supplier("Acme");
        var mat = f.Bought(ItemKind.RawMaterial, "M1", 10, sup);
        var p = f.Product("P1", [(mat, 1, QtyUnit.Pcs)], 60);
        var before = f.Store.GetItem(p)!.Price;
        var s = f.Store.GetSupplier(sup)!;
        s.DiscountPct = 30;
        f.Store.SaveSupplier(s);
        Assert.Equal(2, f.Store.CountMarginExpansion());
        Assert.Equal(2, f.Store.MarginExpansion());
        Assert.Equal(before, f.Store.GetItem(p)!.Price);
        Assert.Equal(20m, f.Store.GetItem(mat)!.Price);
        var notes = f.Store.GetNotifications();
        Assert.All(notes, n => Assert.True(n.Adjusted));
        Assert.All(notes, n => Assert.Equal(n.OldPrice, n.CurrentPrice));
        Assert.Equal(0, f.Store.CountMarginExpansion());
        var pdf = PdfReports.PriceChanges(notes, "€", f.Now);
        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public void History_OneRowPerDay_WithAllReasons()
    {
        using var f = new StoreFixture();
        var sup = f.Supplier("Acme");
        var mat = f.Bought(ItemKind.RawMaterial, "M1", 10, sup);
        var p = f.Product("P1", [(mat, 1, QtyUnit.Pcs)], 60);

        f.Now = f.Now.AddDays(1);
        var s = f.Store.GetSupplier(sup)!;
        s.DiscountPct = 10; f.Store.SaveSupplier(s);
        var st = f.Store.LoadSettings();
        st.LaborRatePerHour = 40; f.Store.SaveSettings(st);

        var h = f.Store.GetHistory(p);
        Assert.Equal(2, h.Count);
        Assert.Equal(PriceReason.Created, h[0].Reasons);
        Assert.Equal("2026-10-02", h[1].Day);
        Assert.Equal(PriceReason.MaterialCostDown | PriceReason.LaborUp, h[1].Reasons);
        Assert.Equal(f.Store.GetItem(p)!.Price, h[1].Price);
        Assert.Equal(h[0].Price, h[1].OldPrice);

        // a change that is undone the same day leaves no row
        f.Now = f.Now.AddDays(1);
        s.DiscountPct = 0; f.Store.SaveSupplier(s);
        s.DiscountPct = 10; f.Store.SaveSupplier(s);
        Assert.Equal(2, f.Store.GetHistory(mat).Count);
    }

    [Fact]
    public void MaterialUnitChange_RepricesRecipes_AndDeleteWarns()
    {
        using var f = new StoreFixture();
        var mat = f.Bought(ItemKind.RawMaterial, "M1", 10, null, unit: QtyUnit.Pcs);
        var p = f.Product("P1", [(mat, 10, QtyUnit.Cm)]);
        Assert.True(f.Store.GetItem(p)!.Warnings.HasFlag(CostWarning.NoConversion));

        var doc = f.Store.LoadItem(mat)!;
        doc.Item.PieceLengthCm = 100;
        f.Store.SaveItem(doc);
        var prod = f.Store.GetItem(p)!;
        Assert.Equal(CostWarning.None, prod.Warnings);
        Assert.Equal(1m, prod.Cost);
        Assert.Single(f.Store.GetUsage(mat));

        f.Store.DeleteItem(mat);
        prod = f.Store.GetItem(p)!;
        Assert.True(prod.Warnings.HasFlag(CostWarning.MissingMaterial));
        Assert.Equal(0m, prod.Cost);
    }

    [Fact]
    public void MerchandiseAsMaterial_ShowsInRawMaterialFolders()
    {
        using var f = new StoreFixture();
        var folder = f.Store.CreateFolder(ListScope.RawMaterials, null, "Fabrics");
        var m = f.Bought(ItemKind.Merchandise, "MER", 5, usable: true);
        f.Bought(ItemKind.RawMaterial, "RAW", 5);
        Assert.Equal(2, f.Store.GetItemRows(ListScope.RawMaterials, null).Count);
        f.Store.MoveItems(ListScope.RawMaterials, [m], folder);
        Assert.Single(f.Store.GetItemRows(ListScope.RawMaterials, null));
        Assert.Single(f.Store.GetItemRows(ListScope.RawMaterials, folder));
        // still at the top of the merchandise list
        Assert.Single(f.Store.GetItemRows(ListScope.Merchandise, null));
        Assert.Equal(1, f.Store.GetFolderTiles(ListScope.RawMaterials, null).Single().ItemCount);
    }

    [Fact]
    public void Folders_SortMoveDelete()
    {
        using var f = new StoreFixture();
        var a = f.Store.CreateFolder(ListScope.Products, null, "A");
        var b = f.Store.CreateFolder(ListScope.Products, a, "B");
        Assert.False(f.Store.MoveFolder(a, b));
        var p2 = f.Product("P2", []);
        var p10 = f.Product("P10", []);
        var p1 = f.Product("P1", []);
        Assert.Equal(["P1", "P2", "P10"], f.Store.GetItemRows(ListScope.Products, null).Select(r => r.Code));
        f.Store.SetSortMode(ListScope.Products, null, SortMode.Manual);
        f.Store.ReorderRows(ListScope.Products, null, [p10, p1, p2]);
        Assert.Equal(["P10", "P1", "P2"], f.Store.GetItemRows(ListScope.Products, null).Select(r => r.Code));
        f.Store.MoveItems(ListScope.Products, [p1], b);
        f.Store.DeleteFolder(b);
        Assert.Single(f.Store.GetItemRows(ListScope.Products, a));
    }

    [Fact]
    public void Search_FindsCommentsSupplierCodesAndGreek()
    {
        using var f = new StoreFixture();
        var sup = f.Supplier("Υφάσματα Παπαδόπουλος");
        var mat = f.Store.SaveItem(new ItemDocument
        {
            Item = new Item { Kind = ItemKind.RawMaterial, Code = "R-1", Name = "Βαμβάκι" },
            Sources = [new ItemSource { SupplierId = sup, SupplierCode = "XK-998", PurchasePrice = 1 }],
        });
        f.Store.SaveItem(new ItemDocument
        {
            Item = new Item { Kind = ItemKind.Product, Code = "P-1", Name = "Τσάντα" },
            Lines = [new RecipeLine { MaterialId = mat, Quantity = 1, Comment = "διπλή ραφή" }],
        });
        Assert.Contains(f.Store.Search("xk-99"), h => h.Id == mat);
        Assert.Contains(f.Store.Search("ΡΑΦΗ"), h => h.Code == "P-1" && h.Snippet == "διπλή ραφή");
        Assert.Null(f.Store.Search("τσαντ").Single().Snippet);   // matched on the name: no snippet
        Assert.Contains(f.Store.Search("παπαδοπ"), h => h.IsSupplier);
        Assert.Contains(f.Store.Search("παπαδοπ"), h => h.Id == mat);   // item found by its supplier
        Assert.Single(f.Store.FilterItemRows(ListScope.RawMaterials, "βαμβακ"));

        Assert.Throws<DuplicateCodeException>(() => f.Store.SaveItem(new ItemDocument { Item = new Item { Kind = ItemKind.Product, Code = "p-1", Name = "x" } }));
    }

    [Fact]
    public void Snippet_AroundTheMatch()
    {
        var text = "Πρώτα κόβουμε το δέρμα στο σωστό μέγεθος, μετά περνάμε διπλή ραφή στις άκρες και τέλος βάφουμε.";
        var snip = Store.Snippet(text, ProductCosting.Core.Calc.TextNorm.Normalize("ΡΑΦΗ"));
        Assert.NotNull(snip);
        Assert.Contains("διπλή ραφή", snip);
        Assert.StartsWith("…", snip);
        Assert.Null(Store.Snippet(text, "χρυσο"));
        Assert.Equal("Ελλάδα", ProductCosting.Core.Localization.Countries.Name("GR"));
        Assert.Equal("κάτι άλλο", ProductCosting.Core.Localization.Countries.Name("κάτι άλλο"));
    }

    [Fact]
    public void Backup_Restore_TakesSafetyCopy()
    {
        using var f = new StoreFixture();
        var backups = new BackupService(f.Store);
        f.Bought(ItemKind.RawMaterial, "KEEP", 1);
        var b = backups.BackupNow();
        f.Bought(ItemKind.RawMaterial, "LATER", 1);
        Assert.Equal(2, f.Store.GetItemRows(ListScope.RawMaterials, null).Count);

        f.Now = f.Now.AddSeconds(5);
        var safety = backups.Restore(b.Path);
        Assert.Equal("safety", safety.Kind);
        Assert.Single(f.Store.GetItemRows(ListScope.RawMaterials, null));
        Assert.Equal(2, backups.ListBackups().Count);

        Assert.NotNull(backups.RunAutoBackupIfDue());
        Assert.Null(backups.RunAutoBackupIfDue());
        Assert.Throws<InvalidDataException>(() => backups.Restore(Path.Combine(f.Dir, "nope.db")));
    }

    [Fact]
    public void SupplierOrder_Pdf()
    {
        using var f = new StoreFixture();
        var sup = f.Supplier("Acme");
        var m = f.Store.SaveItem(new ItemDocument
        {
            Item = new Item { Kind = ItemKind.Merchandise, Code = "M1", Name = "Κούπα" },
            Sources = [new ItemSource { SupplierId = sup, SupplierCode = "S1", PurchasePrice = 3 }],
            Variants = [new ItemVariant { Name = "Κόκκινο", Code = "S1-R" }, new ItemVariant { Name = "Μπλε", Code = "S1-B" }],
        });
        var rows = f.Store.GetSupplierItems(sup);
        Assert.Single(rows);
        Assert.Equal(2, f.Store.GetVariants([m])[m].Count);
        var pdf = PdfReports.SupplierOrder(f.Store.GetSupplier(sup)!, [new OrderLine("M1", "S1-R", "Κούπα", "Κόκκινο", 12, QtyUnit.Pcs)], f.Now);
        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public void Performance_ThousandsOfItems()
    {
        using var f = new StoreFixture();
        var sup = f.Supplier("Bulk");
        var mats = new List<long>();
        for (var i = 0; i < 1000; i++) mats.Add(f.Bought(ItemKind.RawMaterial, "M" + i, 1 + i % 7, sup));
        for (var i = 0; i < 3000; i++) f.Product("P" + i, [(mats[i % 1000], 2, QtyUnit.Pcs), (mats[(i * 7) % 1000], 1, QtyUnit.Pcs)], 120);

        var sw = Stopwatch.StartNew();
        var s = f.Store.GetSupplier(sup)!;
        s.DiscountPct = 15;
        f.Store.SaveSupplier(s);
        var supplierMs = sw.ElapsedMilliseconds;
        Assert.Equal(4000, f.Store.CountNotifications());

        sw.Restart();
        var st = f.Store.LoadSettings();
        st.LaborRatePerHour = 25;
        f.Store.SaveSettings(st);
        var laborMs = sw.ElapsedMilliseconds;

        sw.Restart();
        Assert.Equal(4000, f.Store.MarginExpansion());
        var expansionMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var rows = f.Store.GetItemRows(ListScope.Products, null);
        var listMs = sw.ElapsedMilliseconds;
        Assert.Equal(3000, rows.Count);

        // generous bounds: these run in tens of milliseconds on a normal PC
        Assert.True(supplierMs < 5000, $"supplier {supplierMs} ms");
        Assert.True(laborMs < 5000, $"labor {laborMs} ms");
        Assert.True(expansionMs < 10000, $"expansion {expansionMs} ms");
        Assert.True(listMs < 2000, $"list {listMs} ms");
        Console.WriteLine($"supplier {supplierMs} ms, labor {laborMs} ms, expansion {expansionMs} ms, list {listMs} ms");
    }
}
