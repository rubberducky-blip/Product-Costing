using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProductCosting.App.ViewModels;
using ProductCosting.App.Views;
using ProductCosting.Core.Data;
using ProductCosting.Core.Model;
using ProductCosting.Core.Pdf;

namespace ProductCosting.App.Infrastructure;

/// <summary>
/// Self-test for CI (<c>ProductCosting.exe --smoke folder</c>): seeds demo data in a separate folder,
/// opens every page, popup and dialog, saves screenshots and a log, then exits with the number of failures.
/// </summary>
internal static class Smoke
{
    static string _dir = "";
    static int _errors;

    public static void Prepare(string dir)
    {
        _dir = dir;
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(Path.Combine(dir, "shots"));
        Log("smoke start " + DateTime.Now.ToString("s"));
    }

    static void Log(string line) => File.AppendAllText(Path.Combine(_dir, "smoke.log"), line + Environment.NewLine);

    public static void Fail(string step, Exception ex)
    {
        _errors++;
        Log($"FAIL {step}: {ex}");
        // a broken template fails on every layout pass: stop early instead of looping until the CI timeout
        if (_errors > 20) Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown(_errors));
    }

    static void Check(string what, bool ok)
    {
        if (ok) Log("ok   " + what);
        else { _errors++; Log("FAIL " + what); }
    }

    // ---------- demo data ----------

    public static void Seed()
    {
        var store = AppState.Store;
        var day = new DateTime(2026, 9, 1, 10, 0, 0);
        store.Clock = () => day;

        var s = store.LoadSettings();
        s.LaborRatePerHour = 12;
        s.OverheadRatePerHour = 6;
        s.DefaultMarkupPct = 120;
        s.UsdPerEur = 1.08m;
        s.BackupFolder = Path.Combine(_dir, "backups");
        store.SaveSettings(s);

        var bags = store.CreateFolder(ListScope.Products, null, "Τσάντες");
        store.CreateFolder(ListScope.Products, bags, "Ώμου");
        var wallets = store.CreateFolder(ListScope.Products, null, "Πορτοφόλια");
        var leathers = store.CreateFolder(ListScope.RawMaterials, null, "Δέρματα");
        var hardware = store.CreateFolder(ListScope.RawMaterials, null, "Αξεσουάρ");
        var abroad = store.CreateFolder(ListScope.Suppliers, null, "Εξωτερικού");
        store.CreateFolder(ListScope.Merchandise, null, "Κεραμικά");

        var crete = store.SaveSupplier(new Supplier { Name = "Δερμάτινα Κρήτης ΑΕ", Country = "Ελλάδα", DiscountPct = 8, ShippingPct = 5, Email = "orders@dermata.gr", Phone = "2810 123456" });
        var usa = store.SaveSupplier(new Supplier { Name = "Leather Works Inc", Country = "USA", Currency = Currency.Usd, ShippingPct = 14, FolderId = abroad, Email = "sales@leatherworks.com" });
        var italy = store.SaveSupplier(new Supplier { Name = "Metalli Srl", Country = "Italia", DiscountPct = 5, ShippingPct = 10, FolderId = abroad });

        long Raw(string code, string name, long? folder, long sup, string supCode, decimal price, decimal qty, QtyUnit unit, decimal? len = null, decimal? area = null) =>
            store.SaveItem(new ItemDocument
            {
                Item = new Item { Kind = ItemKind.RawMaterial, Code = code, Name = name, FolderId = folder, Unit = unit, PieceLengthCm = len, PieceAreaCm2 = area },
                Sources = [new ItemSource { SupplierId = sup, SupplierCode = supCode, PurchasePrice = price, PurchaseQty = qty }],
            });

        var hide = Raw("R-001", "Δέρμα μοσχαρίσιο φυσικό", leathers, crete, "DK-NAT-150", 85, 15000, QtyUnit.Cm2);
        var hideBlack = Raw("R-002", "Δέρμα μοσχαρίσιο μαύρο", leathers, usa, "LW-BLK", 96, 15000, QtyUnit.Cm2);
        var thread = Raw("R-010", "Κλωστή κερωμένη 0,8 mm", hardware, italy, "TH-08", 12, 50000, QtyUnit.Cm);
        var zip = Raw("R-020", "Φερμουάρ μεταλλικό 20 εκ.", hardware, italy, "ZP-20", 35, 100, QtyUnit.Pcs, len: 20);
        var rings = Raw("R-021", "Κρίκοι ορειχάλκινοι", hardware, italy, "RG-BR", 18, 200, QtyUnit.Pcs);

        var belt = store.SaveItem(new ItemDocument
        {
            Item = new Item { Kind = ItemKind.Merchandise, Code = "M-100", Name = "Ιμάντας ώμου ρυθμιζόμενος", UsableAsMaterial = true, MaterialFolderId = hardware, Unit = QtyUnit.Pcs },
            Sources = [new ItemSource { SupplierId = italy, SupplierCode = "ST-120", PurchasePrice = 4.2m, PurchaseQty = 1 }],
        });
        store.SaveItem(new ItemDocument
        {
            Item = new Item { Kind = ItemKind.Merchandise, Code = "M-200", Name = "Κούπα κεραμική 350 ml", Unit = QtyUnit.Pcs, Notes = "Πλύσιμο στο χέρι" },
            Sources =
            [
                new ItemSource { SupplierId = usa, SupplierCode = "MUG-350", PurchasePrice = 3.1m, PurchaseQty = 1, Weight = 2 },
                new ItemSource { SupplierId = italy, SupplierCode = "TZ-350", PurchasePrice = 2.6m, PurchaseQty = 1, Weight = 3 },
            ],
            Variants = [new ItemVariant { Name = "Λευκή", Code = "MUG-350-W" }, new ItemVariant { Name = "Μαύρη", Code = "MUG-350-B" }, new ItemVariant { Name = "Κόκκινη", Code = "MUG-350-R" }],
        });

        long Product(string code, string name, long? folder, (long, decimal, QtyUnit, string?)[] lines, (int, string?)[] labor) =>
            store.SaveItem(new ItemDocument
            {
                Item = new Item { Kind = ItemKind.Product, Code = code, Name = name, FolderId = folder },
                Lines = lines.Select(l => new RecipeLine { MaterialId = l.Item1, Quantity = l.Item2, Unit = l.Item3, Comment = l.Item4 }).ToList(),
                Labor = labor.Select(l => new LaborEntry { Seconds = l.Item1, Comment = l.Item2 }).ToList(),
            });

        Product("P-100", "Τσάντα ώμου «Αριάδνη»", bags,
            [(hide, 1800, QtyUnit.Cm2, "σώμα + καπάκι"), (thread, 600, QtyUnit.Cm, "διπλή ραφή"), (zip, 1, QtyUnit.Pcs, null), (rings, 4, QtyUnit.Pcs, null), (belt, 1, QtyUnit.Pcs, null)],
            [(1500, "κοπή"), (2400, "ραφή"), (600, "φινίρισμα")]);
        Product("P-101", "Τσάντα χειρός «Δήμητρα»", bags,
            [(hideBlack, 1400, QtyUnit.Cm2, null), (thread, 450, QtyUnit.Cm, null), (zip, 25, QtyUnit.Cm, "κομμένο στο μήκος")],
            [(1200, "κοπή"), (2100, "ραφή")]);
        Product("P-200", "Πορτοφόλι ανδρικό", wallets, [(hide, 600, QtyUnit.Cm2, null), (thread, 250, QtyUnit.Cm, null)], [(900, null), (600, "βαφή άκρων")]);
        Product("P-201", "Καρτοθήκη", wallets, [(hideBlack, 220, QtyUnit.Cm2, null), (thread, 90, QtyUnit.Cm, null)], [(420, null)]);
        Product("P-300", "Μπρελόκ δερμάτινο", null, [(hide, 40, QtyUnit.Cm2, null), (rings, 1, QtyUnit.Pcs, null), (zip, 1, QtyUnit.Kg, "λάθος μονάδα για έλεγχο")], [(180, null)]);
        for (var i = 0; i < 400; i++)
            Product($"X-{i:000}", $"Δοκιμαστικό προϊόν {i}", null, [(hide, 100 + i, QtyUnit.Cm2, null)], [(60 + i, null)]);

        // a few price changes on later days, to fill the history and the bell
        day = day.AddDays(9);
        var sup = store.GetSupplier(crete)!;
        sup.DiscountPct = 15;
        store.SaveSupplier(sup);
        day = day.AddDays(12);
        s = store.LoadSettings();
        s.LaborRatePerHour = 13.5m;
        store.SaveSettings(s);
        day = day.AddDays(7);
        s.UsdPerEur = 1.02m;
        store.SaveSettings(s);
        store.Clock = () => DateTime.Now;
        AppState.ReloadSettings();
        Log("seeded");
    }

    // ---------- tour ----------

    static async Task Settle(int ms = 350)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(ms);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    static void Shot(FrameworkElement el, string name)
    {
        el.UpdateLayout();
        var w = (int)Math.Ceiling(el.ActualWidth);
        var h = (int)Math.Ceiling(el.ActualHeight);
        if (w <= 0 || h <= 0)
        {
            Check($"{name}: has size", false);
            return;
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle((Brush)Application.Current.FindResource("WindowBrush"), null, new Rect(0, 0, w, h));
            // map the element's own layout box (not its content bounds) onto the image
            dc.DrawRectangle(new VisualBrush(el)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, w, h),
            }, null, new Rect(0, 0, w, h));
        }
        rtb.Render(dv);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(rtb));
        using (var f = File.Create(Path.Combine(_dir, "shots", name + ".png"))) png.Save(f);

        // small JPEG copy as base64, so screenshots can be read from the CI log
        // check-run text is limited to 64 KB, so shrink until it fits
        var ms = new MemoryStream();
        foreach (var (scale, quality) in new[] { (0.62, 70), (0.55, 62), (0.48, 55), (0.4, 50), (0.32, 45) })
        {
            ms = new MemoryStream();
            var jpg = new JpegBitmapEncoder { QualityLevel = quality };
            jpg.Frames.Add(BitmapFrame.Create(new TransformedBitmap(rtb, new ScaleTransform(scale, scale))));
            jpg.Save(ms);
            if (ms.Length * 4 / 3 < 60000) break;
        }
        File.WriteAllText(Path.Combine(_dir, "shots", name + ".b64"), Convert.ToBase64String(ms.ToArray()));
        Log($"shot {name} {w}x{h}");
    }

    static async Task Step(string name, Func<Task> body)
    {
        try
        {
            await body();
            Log("ok   " + name);
        }
        catch (Exception ex)
        {
            Fail(name, ex);
        }
    }

    static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var x in Find<T>(c)) yield return x;
        }
    }

    static FrameworkElement? OpenPopup(Window w) =>
        Find<Popup>(w).FirstOrDefault(p => p.IsOpen)?.Child as FrameworkElement;

    public static async Task<int> Run(MainWindow w)
    {
        var vm = MainViewModel.Instance;
        var store = AppState.Store;
        // the size the app is designed for; one shot at the minimum size checks that nothing breaks
        w.Width = 1440;
        w.Height = 900;
        var root = (FrameworkElement)w.Content;
        long Id(string code) => store.Search(code).First(h => h.Code == code).Id;

        await Settle(800);
        await Step("products list", async () => { Shot(root, "01-products"); await Task.CompletedTask; });
        await Step("products folder", async () =>
        {
            var list = (ListPageViewModel)vm.Current!;
            list.OpenFolder(store.GetFolders(ListScope.Products).First(f => f.Name == "Τσάντες").Id);
            await Settle();
            Shot(root, "02-products-folder");
            list.OpenFolder(null);
        });
        await Step("filter", async () =>
        {
            var list = (ListPageViewModel)vm.Current!;
            list.Filter = "τσαντα";
            await Settle();
            Check("filter finds 2 bags", list.Rows.Count == 2);
            Shot(root, "03-filter");
            list.Filter = "";
        });
        await Step("raw materials", async () => { vm.GoCommand.Execute("raw"); await Settle(); Shot(root, "04-raw"); });
        await Step("merchandise", async () => { vm.GoCommand.Execute("merchandise"); await Settle(); Shot(root, "05-merchandise"); });
        await Step("suppliers", async () => { vm.GoCommand.Execute("suppliers"); await Settle(); Shot(root, "06-suppliers"); });

        await Step("product editor", async () =>
        {
            vm.OpenItem(Id("P-100"));
            await Settle(500);
            Shot(root, "07-product-editor");
            var ed = (ItemEditorViewModel)vm.Current!;
            Check("editor clean after load", !ed.IsDirty);
            var name = ed.Name;
            ed.Name = name + " x";
            Check("editor dirty after edit", ed.IsDirty);
            ed.Name = name;
            Check("editor clean after undoing the edit", !ed.IsDirty);
            ed.Lines[0].Quantity.Text = "1900";
            await Settle();
            Shot(root, "08-product-editor-edited");
            Check("dirty after quantity edit", ed.IsDirty);
            ed.CancelCommand.Execute(null);
            Check("clean after cancel", !ed.IsDirty);
        });
        await Step("product editor at minimum size", async () =>
        {
            w.Width = w.MinWidth;
            w.Height = w.MinHeight;
            await Settle(500);
            Shot(root, "08b-product-editor-min");
            w.Width = 1440;
            w.Height = 900;
            await Settle();
        });
        await Step("product with warning", async () => { vm.OpenItem(Id("P-300")); await Settle(); Shot(root, "09-product-warning"); });
        await Step("merchandise editor", async () => { vm.OpenItem(Id("M-200")); await Settle(500); Shot(root, "10-merchandise-editor"); });
        await Step("raw material editor", async () => { vm.OpenItem(Id("R-001")); await Settle(500); Shot(root, "11-raw-editor"); });
        await Step("new product", async () => { vm.NewItem(ItemKind.Product, null); await Settle(); Shot(root, "12-new-product"); vm.Current!.Discard(); });
        await Step("supplier editor", async () =>
        {
            vm.OpenSupplier(store.GetSuppliers().First(s => s.Name.StartsWith("Metalli")).Id);
            await Settle();
            Shot(root, "13-supplier-editor");
        });
        await Step("settings", async () => { vm.GoCommand.Execute("settings"); await Settle(); Shot(root, "14-settings"); });

        await Step("search", async () =>
        {
            vm.GoCommand.Execute("products");
            vm.Search.Query = "δερμα";
            await Settle(700);
            Check("search has results", vm.Search.Results.Count > 0);
            Shot(root, "15-search-page");
            if (OpenPopup(w) is { } p) Shot(p, "15-search-popup");
            else Check("search popup open", false);
            vm.Search.Clear();
        });
        await Step("notifications", async () =>
        {
            vm.Notifications.Refresh();
            Check("notifications exist", vm.Notifications.Count > 0);
            vm.Notifications.ToggleCommand.Execute(null);
            await Settle(600);
            if (OpenPopup(w) is { } p) Shot(p, "16-notifications");
            else Check("notifications popup open", false);
            var item = vm.Notifications.Items.First(i => i.CanAdjust);
            vm.Notifications.AdjustCommand.Execute(item);
            await Settle(500);
            if (OpenPopup(w) is { } p2) Shot(p2, "17-notifications-adjusted");
            vm.Notifications.IsOpen = false;
            File.WriteAllBytes(Path.Combine(_dir, "price-changes.pdf"),
                PdfReports.PriceChanges(store.GetNotifications(), "€", DateTime.Now));
        });
        await Step("price history", async () =>
        {
            var win = new PriceHistoryWindow(Id("P-100"), "P-100 · Τσάντα ώμου") { Owner = w };
            win.Show();
            await Settle(500);
            Shot((FrameworkElement)win.Content, "18-history");
            win.Close();
        });
        await Step("order", async () =>
        {
            var sup = store.GetSuppliers().First(s => s.Name.StartsWith("Leather"));
            var win = new OrderWindow(sup) { Owner = w };
            win.Show();
            await Settle(500);
            Shot((FrameworkElement)win.Content, "19-order");
            win.Close();
            File.WriteAllBytes(Path.Combine(_dir, "order.pdf"), PdfReports.SupplierOrder(sup,
                [new OrderLine("M-200", "MUG-350-W", "Κούπα κεραμική 350 ml", "Λευκή", 24, QtyUnit.Pcs)], DateTime.Now));
        });
        await Step("dialog", async () =>
        {
            var d = new DialogWindow(Loc.T("leave.title"), Loc.T("leave.body"), DialogKind.Warning,
                [("stay", Loc.T("leave.stay"), "GhostButton"), ("discard", Loc.T("leave.discard"), "DangerButton"), ("save", Loc.T("leave.save"), "PrimaryButton")]) { Owner = w };
            d.Show();
            await Settle();
            Shot((FrameworkElement)d.Content, "20-dialog");
            d.Close();
        });
        await Step("margin expansion", async () =>
        {
            var n = store.MarginExpansion();
            Log($"margin expansion adjusted {n}");
            await Settle();
        });
        await Step("backup", async () =>
        {
            var b = AppState.Backups.BackupNow();
            Check("backup listed", AppState.Backups.ListBackups().Any(x => x.Path == b.Path));
            await Task.CompletedTask;
        });
        await Step("english", async () =>
        {
            Loc.I.SetLanguage("en");
            vm.ResetPages();
            await Settle(500);
            Shot(root, "21-english");
            vm.OpenItem(Id("M-200"));
            await Settle(500);
            Shot(root, "22-english-editor");
            Loc.I.SetLanguage("el");
        });

        Log($"done, {_errors} error(s)");
        return _errors;
    }
}
