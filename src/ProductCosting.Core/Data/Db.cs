using Dapper;
using Microsoft.Data.Sqlite;

namespace ProductCosting.Core.Data;

/// <summary>Opens connections to the local SQLite database and keeps its schema up to date.</summary>
public sealed class Db
{
    public const int SchemaVersion = 3;

    static int _initialized;

    public string Path { get; }
    readonly string _cs;

    public Db(string path)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            SQLitePCL.Batteries_V2.Init();
            DefaultTypeMap.MatchNamesWithUnderscores = true;
        }
        Path = path;
        _cs = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true, ForeignKeys = true }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-16000; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    public void Migrate()
    {
        using var c = Open();
        Migrate(c);
    }

    public static void Migrate(SqliteConnection c)
    {
        var version = c.ExecuteScalar<long>("PRAGMA user_version");
        if (version > SchemaVersion)
            throw new InvalidOperationException("The database was created by a newer version of the app.");
        if (version < 1)
        {
            using var t = c.BeginTransaction();
            c.Execute(Schema.V1, transaction: t);
            c.Execute("PRAGMA user_version = 1", transaction: t);
            t.Commit();
            version = 1;
        }
        if (version < 2)
        {
            using var t = c.BeginTransaction();
            c.Execute(Schema.V2, transaction: t);
            c.Execute("PRAGMA user_version = 2", transaction: t);
            t.Commit();
        }
        if (version < 3)
        {
            using var t = c.BeginTransaction();
            c.Execute(Schema.V3, transaction: t);
            c.Execute("PRAGMA user_version = 3", transaction: t);
            t.Commit();
        }
    }

    /// <summary>True when the file is a SQLite database with this app's tables.</summary>
    public static bool LooksLikeAppDatabase(string file)
    {
        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            using var c = new SqliteConnection(cs);
            c.Open();
            var tables = c.ExecuteScalar<long>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('items','suppliers','settings','folders')");
            var v = c.ExecuteScalar<long>("PRAGMA user_version");
            return tables == 4 && v >= 1 && v <= SchemaVersion;
        }
        catch
        {
            return false;
        }
    }
}

static class Schema
{
    public const string V1 = """
        CREATE TABLE settings(
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        ) WITHOUT ROWID;

        -- sort mode of the top level of each list
        CREATE TABLE scope_settings(
            scope     INTEGER PRIMARY KEY,
            sort_mode INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE folders(
            id        INTEGER PRIMARY KEY,
            scope     INTEGER NOT NULL,
            parent_id INTEGER REFERENCES folders(id) ON DELETE CASCADE,
            name      TEXT NOT NULL,
            sort_mode INTEGER NOT NULL DEFAULT 0,
            position  INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX ix_folders_scope_parent ON folders(scope, parent_id);

        CREATE TABLE suppliers(
            id           INTEGER PRIMARY KEY,
            name         TEXT NOT NULL,
            folder_id    INTEGER REFERENCES folders(id) ON DELETE SET NULL,
            position     INTEGER NOT NULL DEFAULT 0,
            country      TEXT,
            discount_pct REAL NOT NULL DEFAULT 0,
            shipping_pct REAL NOT NULL DEFAULT 0,
            currency     INTEGER NOT NULL DEFAULT 0,
            email        TEXT,
            phone        TEXT,
            notes        TEXT,
            list_key     TEXT NOT NULL DEFAULT '',
            created_at   TEXT NOT NULL,
            updated_at   TEXT NOT NULL
        );
        CREATE INDEX ix_suppliers_folder ON suppliers(folder_id);

        CREATE TABLE items(
            id                 INTEGER PRIMARY KEY,
            kind               INTEGER NOT NULL,
            code               TEXT NOT NULL COLLATE NOCASE UNIQUE,
            name               TEXT NOT NULL,
            usable_as_material INTEGER NOT NULL DEFAULT 0,
            folder_id          INTEGER REFERENCES folders(id) ON DELETE SET NULL,
            material_folder_id INTEGER REFERENCES folders(id) ON DELETE SET NULL,
            position           INTEGER NOT NULL DEFAULT 0,
            material_position  INTEGER NOT NULL DEFAULT 0,
            unit               INTEGER NOT NULL DEFAULT 0,
            piece_length_cm    REAL,
            piece_area_cm2     REAL,
            piece_weight_kg    REAL,
            markup_pct         REAL,
            rounding_step      REAL,
            notes              TEXT,
            -- cached calculation results, maintained by the pricing engine
            cost               REAL NOT NULL DEFAULT 0,
            cost_materials     REAL NOT NULL DEFAULT 0,
            cost_labor         REAL NOT NULL DEFAULT 0,
            cost_overhead      REAL NOT NULL DEFAULT 0,
            labor_seconds      INTEGER NOT NULL DEFAULT 0,
            eff_markup         REAL NOT NULL DEFAULT 0,
            eff_step           REAL NOT NULL DEFAULT 0,
            price              REAL NOT NULL DEFAULT 0,
            warnings           INTEGER NOT NULL DEFAULT 0,
            has_unseen_change  INTEGER NOT NULL DEFAULT 0,
            list_key           TEXT NOT NULL DEFAULT '',
            created_at         TEXT NOT NULL,
            updated_at         TEXT NOT NULL
        );
        CREATE INDEX ix_items_kind_folder ON items(kind, folder_id);
        CREATE INDEX ix_items_material_folder ON items(material_folder_id) WHERE usable_as_material = 1;

        CREATE TABLE item_sources(
            id             INTEGER PRIMARY KEY,
            item_id        INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
            supplier_id    INTEGER REFERENCES suppliers(id) ON DELETE SET NULL,
            supplier_code  TEXT,
            purchase_price REAL NOT NULL DEFAULT 0,
            purchase_qty   REAL NOT NULL DEFAULT 1,
            currency       INTEGER,
            discount_pct   REAL,
            shipping_pct   REAL,
            weight         REAL NOT NULL DEFAULT 1,
            position       INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX ix_sources_item ON item_sources(item_id, position);
        CREATE INDEX ix_sources_supplier ON item_sources(supplier_id);

        CREATE TABLE item_variants(
            id       INTEGER PRIMARY KEY,
            item_id  INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
            name     TEXT NOT NULL,
            code     TEXT,
            position INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX ix_variants_item ON item_variants(item_id, position);

        CREATE TABLE recipe_lines(
            id          INTEGER PRIMARY KEY,
            product_id  INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
            material_id INTEGER REFERENCES items(id) ON DELETE SET NULL,
            quantity    REAL NOT NULL DEFAULT 0,
            unit        INTEGER NOT NULL DEFAULT 0,
            comment     TEXT,
            position    INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX ix_recipe_product ON recipe_lines(product_id, position);
        CREATE INDEX ix_recipe_material ON recipe_lines(material_id);

        CREATE TABLE labor_entries(
            id         INTEGER PRIMARY KEY,
            product_id INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
            seconds    INTEGER NOT NULL DEFAULT 0,
            comment    TEXT,
            position   INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX ix_labor_product ON labor_entries(product_id, position);

        -- at most one open notification per item: later changes merge into it
        CREATE TABLE price_notifications(
            id         INTEGER PRIMARY KEY,
            item_id    INTEGER NOT NULL UNIQUE REFERENCES items(id) ON DELETE CASCADE,
            old_price  REAL NOT NULL,
            new_price  REAL NOT NULL,
            reasons    INTEGER NOT NULL DEFAULT 0,
            adjusted   INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );
        CREATE INDEX ix_notifications_updated ON price_notifications(updated_at);

        -- one row per item per day: the last price of that day and every reason it changed
        CREATE TABLE price_history(
            item_id   INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
            day       TEXT NOT NULL,
            old_price REAL,
            price     REAL NOT NULL,
            reasons   INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY(item_id, day)
        ) WITHOUT ROWID;

        -- global search: trigram index over normalized text (rowid = id*2 for items, id*2+1 for suppliers)
        CREATE VIRTUAL TABLE search_index USING fts5(body, tokenize='trigram');
        """;

    // wastage on materials and recipe lines, MOQ and package size per supplier
    public const string V2 = """
        ALTER TABLE items ADD COLUMN has_waste INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE items ADD COLUMN waste_pct REAL;
        ALTER TABLE recipe_lines ADD COLUMN waste_pct REAL;
        ALTER TABLE item_sources ADD COLUMN moq REAL;
        ALTER TABLE item_sources ADD COLUMN pack_qty REAL;
        """;

    // variations that belong to one supplier, saved purchase orders
    public const string V3 = """
        ALTER TABLE item_variants ADD COLUMN supplier_id INTEGER REFERENCES suppliers(id) ON DELETE SET NULL;

        CREATE TABLE purchase_orders(
            id          INTEGER PRIMARY KEY,
            supplier_id INTEGER NOT NULL REFERENCES suppliers(id) ON DELETE CASCADE,
            created_at  TEXT NOT NULL,
            updated_at  TEXT NOT NULL
        );
        CREATE INDEX ix_orders_supplier ON purchase_orders(supplier_id, updated_at);

        -- lines keep a copy of code / name, so an old order still prints the same after items change
        CREATE TABLE purchase_order_lines(
            id            INTEGER PRIMARY KEY,
            order_id      INTEGER NOT NULL REFERENCES purchase_orders(id) ON DELETE CASCADE,
            item_id       INTEGER REFERENCES items(id) ON DELETE SET NULL,
            variant       TEXT,
            code          TEXT NOT NULL,
            supplier_code TEXT,
            name          TEXT NOT NULL,
            unit          INTEGER NOT NULL DEFAULT 0,
            quantity      REAL NOT NULL,
            position      INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX ix_order_lines_order ON purchase_order_lines(order_id, position);
        """;
}
