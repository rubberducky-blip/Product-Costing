# Κοστολόγηση Προϊόντων · Product Costing

Windows desktop app (Windows 10 and 11) for calculating the cost, selling price and margin of products.
Single user, data stored locally in SQLite, backups saved to a folder you choose.
The interface is in Greek, with an English option, uses a dark theme, and shows prices in euro.

## Download and run

Every push builds the app on GitHub Actions:

1. Open **Actions → CI**, select the latest run, and download the **ProductCosting-win-x64** artifact.
2. Unzip it and run `ProductCosting.exe`. Nothing needs to be installed, because the .NET runtime is included in the file.

The data lives in `%LOCALAPPDATA%\ProductCosting\data.db`. Backups go to `Documents\Product Costing Backups` until you choose another folder in Settings.

## Features

**Item types**
- **Products (Προϊόντα)**: made in-house from a recipe of raw materials plus labor time.
- **Merchandise (Εμπορεύματα)**: bought from a supplier and resold. Can be marked *also used as raw material*, so it can go into recipes and gets its own place in the raw-materials folders.
- **Raw materials (Πρώτες ύλες)**: bought from a supplier and used in recipes.
- **Suppliers (Προμηθευτές)**: name, country, default discount %, shipping %, price currency (€ or $), email, phone, notes, and the list of items bought from them.

**Costing**
- Bought items: purchase price, quantity per purchase and unit (pcs, cm, cm², kg). Cost = (price → €) − discount + shipping, ÷ quantity. Discount, shipping and currency come from the supplier unless set on the item.
- Dollar prices are converted to euro first, using the rate set in Settings (1 € = X $).
- An item can have several suppliers, each with its own supplier code, price and **weight**. For example, weights 2 and 3 mean that 40% of the stock comes from A and 60% from B, so the cost is the weighted average.
- Products: cost = raw materials + labor time × labor rate + labor time × production overhead rate.
- Labor time is entered in seconds, as several entries with optional comments. The total is shown in minutes and seconds.
- Recipe lines: **Add raw material** (raw materials and merchandise marked as raw material) or **Add product** (products and merchandise, always counted in pieces, no waste) — (e.g. a finished product plus an extra), quantity, unit and optional comment. Units convert through the piece size (length / area / weight) set on the material. When a product inside a recipe changes price, every product built on it is repriced too. A product can never contain itself.
- **Waste**: a raw material can be marked *has waste* with a percentage. 30% waste means the remaining 70% carries the whole purchase price. In a recipe, such a material gets an extra waste % per line: 30 cm² with 10% waste counts as 33 cm².
- A live cost breakdown is shown on every item while editing, with warnings for units that can't be converted and for missing materials.
- Each raw material shows which products use it.

**Pricing**
- Price = cost × (1 + markup %), rounded to a step (default 0.05 €), to the nearest step, up or down. Profit and margin % are shown.
- Markup and rounding step can be set per item, otherwise the defaults from Settings apply.
- Any change to settings, a material or a supplier updates every price that depends on it.

**Price change notifications**
- When a price changes because of another change, the item gets a glowing red dot next to its code until you hover over it. The bell lists the change (increase/decrease, old → new price, reason).
- From the bell you can hide a notification, or adjust the item's markup so the price goes back to the previous one. **Margin expansion** does this for every item whose price dropped.
- **Export PDF** lists every price change in the bell: code, name, type, old price, new price, change %, final price (after any markup adjustment) and reason.

**Price history**
- One entry per item per day (the last price of the day), with every reason it changed that day: supplier cost increase/decrease, raw material cost increase/decrease, labor/overhead increase/decrease, euro/dollar rate change, margin adjustment and rounding change.
- **Price history** on every item shows a chart (hover a point for details) and a table.

**Orders to suppliers**
- **New order** on a supplier opens a form with every item bought from them. Items with variations (e.g. colours, each with its own supplier code) get one line per variation.
- Each item can have a **minimum order quantity** and a **pack size** per supplier. The order form shows them, flags quantities below the minimum or not in whole packs, and *Fix quantities* rounds them up.
- Variations can belong to one supplier or to all suppliers of the item; an order shows only the ones of its supplier.
- Orders can be **saved**. Saved orders are listed on the supplier: double-click to continue editing, right-click to export to PDF or delete.
- Export the order to PDF. Lines without a quantity are left out.

**Organising**
- Nested folders in every list, shown as tiles above the table, each sorted by code (by name for suppliers) or manually.
- Drag rows or folders onto a folder tile or onto the folder path to move them. With manual sorting, drag rows to reorder them, and drop a folder on the left or right edge of another folder to reorder folders.
- Right-click a row to open, **duplicate** or delete it, see its **price history**, **set its selling price** (the markup is adjusted, after rounding the price to the item's step) or **change its purchase price** (items with one supplier). On suppliers, right-click starts a **new order**. A duplicated item gets the next free code (P-100 → P-100-2).
- A filter box on every list searches all folders. The supplier, raw-material and folder fields use search-as-you-type pickers that show the folder tree.
- **Global search** (Ctrl+F) across codes, names, supplier codes, suppliers, notes and recipe/labor comments. It ignores Greek accents and case. When the match is in a note or comment, that text is shown under the result.

**Editing**
- Save is enabled only when something actually changed, so a value changed and then put back counts as no change. Ctrl+S saves.
- *Cancel changes* restores the values from before editing.
- Leaving with unsaved changes asks whether to discard, save or stay. Every delete asks for confirmation.

**Backups**
- An automatic daily backup runs while the app is open, and the newest N are kept. There is also a **Back up now** button.
- You can restore from a listed backup or from any backup file. A safety backup is always taken first.

## Architecture

```
src/ProductCosting.Core     .NET 10 library, no UI
  Calc/Costing.cs           all cost, currency, unit-conversion, rounding and markup rules (pure functions)
  Data/Db.cs                SQLite connection, WAL mode, schema and migrations
  Data/PricingEngine.cs     incremental repricing of exactly the affected items, history and notifications
  Data/Store*.cs            all queries and writes, each write one transaction
  Services/BackupService.cs SQLite online backups, daily auto-backup, restore
  Pdf/PdfReports.cs         price-change report and supplier order (QuestPDF, embedded Inter font)
src/ProductCosting.App      WPF (.NET 10, Fluent dark theme), MVVM
tests/ProductCosting.Tests  xUnit tests: costing rules, engine, notifications, history, search, backups, PDFs, performance
```

**Data and performance**
- SQLite with an indexed relational schema: items, item sources (suppliers with weights), recipe lines, labor entries, variants, folders, notifications and history. There are indexes on every foreign key and list path, `(kind, folder)` for lists, and `material_id` for "used in" and repricing.
- Each item's cost, cost components and price are stored on the item and kept up to date by the pricing engine. Lists therefore read a single indexed table and never recalculate.
- A change reprices only what it affects: a supplier change reprices its items, then only the products whose recipes use a material whose cost changed. Only rows whose values changed are written, using prepared statements inside one transaction.
- Price history has one row per item per day (`WITHOUT ROWID`, keyed by item and day), and the bell keeps at most one open notification per item. Both stay small however many price changes you make.
- Global search uses an SQLite FTS5 trigram index over accent-folded text.
- Every table is virtualized (row recycling), so lists with tens of thousands of rows scroll smoothly.
- In the test suite, a supplier change that reprices 1,000 materials and 3,000 products takes about 0.1 s, and so does margin expansion over 4,000 items.

## Development

Requires the .NET 10 SDK.

```bash
dotnet test tests/ProductCosting.Tests     # runs anywhere
dotnet run --project src/ProductCosting.App   # Windows
```

`ProductCosting.exe --smoke <folder>` starts the app on demo data in that folder, opens every screen, and saves screenshots and a log there. CI runs it on Windows for every push.

PDFs are made with [QuestPDF](https://www.questpdf.com) under its Community license, which is free for individuals and for companies with under $1M annual revenue. The Inter font is embedded under the SIL Open Font License.
