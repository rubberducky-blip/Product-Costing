using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using ProductCosting.Core.Model;

namespace ProductCosting.Core.Data;

[Flags]
public enum DataChange
{
    None = 0,
    Items = 1,
    Suppliers = 2,
    Folders = 4,
    Settings = 8,
    Notifications = 16,
    /// <summary>The shopping list of items to order.</summary>
    Wish = 32,
    All = Items | Suppliers | Folders | Settings | Notifications | Wish,
}

public sealed class DataChangedEventArgs(DataChange change) : EventArgs
{
    public DataChange Change { get; } = change;
}

public sealed class RecipeCycleException() : Exception("A product can't be part of its own recipe.");

public sealed class DuplicateCodeException(string code) : Exception($"Code '{code}' already exists.")
{
    public string Code { get; } = code;
}

/// <summary>
/// All reads and writes of the app. Every write runs in one transaction together with the
/// recalculation of the prices it affects, so the database never holds stale prices.
/// </summary>
public sealed partial class Store
{
    readonly object _writeLock = new();

    public Db Db { get; private set; }
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;
    public event EventHandler<DataChangedEventArgs>? Changed;

    public Store(Db db)
    {
        Db = db;
        if (db.Migrate()) RepriceAfterUpgrade();
    }

    /// <summary>After an upgrade that changed how prices are worked out: every price again, into the history only.</summary>
    internal void RepriceAfterUpgrade() => Write(DataChange.Items, (c, t) =>
        new PricingEngine(this, c, t).Run(new RecalcRequest { AllItems = true, PriceUnitAll = true, Quiet = true }));

    internal string Now => Clock().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    internal string Today => Clock().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal T Read<T>(Func<SqliteConnection, T> f)
    {
        using var c = Db.Open();
        return f(c);
    }

    internal T Write<T>(DataChange change, Func<SqliteConnection, SqliteTransaction, T> f)
    {
        T result;
        lock (_writeLock)
        {
            using var c = Db.Open();
            using var t = c.BeginTransaction();
            result = f(c, t);
            t.Commit();
        }
        if (change != DataChange.None) Changed?.Invoke(this, new DataChangedEventArgs(change));
        return result;
    }

    internal void Write(DataChange change, Action<SqliteConnection, SqliteTransaction> f) =>
        Write<bool>(change, (c, t) => { f(c, t); return true; });

    /// <summary>Lets the backup service swap the database file while no write is running.</summary>
    internal void Exclusive(Action a)
    {
        lock (_writeLock) a();
        Changed?.Invoke(this, new DataChangedEventArgs(DataChange.All));
    }

    // ---------- settings ----------

    public AppSettings LoadSettings() => Read(c => LoadSettings(c, null));

    internal static AppSettings LoadSettings(SqliteConnection c, SqliteTransaction? t)
    {
        var map = c.Query<(string Key, string Value)>("SELECT key, value FROM settings", transaction: t)
            .ToDictionary(r => r.Key, r => r.Value);
        var s = new AppSettings();
        decimal D(string k, decimal def) => map.TryGetValue(k, out var v) && decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;
        string S(string k, string def) => map.TryGetValue(k, out var v) ? v : def;
        s.LaborRatePerHour = D("labor_rate", s.LaborRatePerHour);
        s.OverheadRatePerHour = D("overhead_rate", s.OverheadRatePerHour);
        s.DefaultMarkupPct = D("default_markup", s.DefaultMarkupPct);
        s.RoundingStep = D("rounding_step", s.RoundingStep);
        s.RoundingMode = (RoundingMode)(int)D("rounding_mode", (int)s.RoundingMode);
        s.CurrencySymbol = S("currency_symbol", s.CurrencySymbol);
        s.Language = S("language", s.Language);
        s.Theme = S("theme", s.Theme);
        s.UiScale = D("ui_scale", s.UiScale);
        s.UsdPerEur = D("usd_per_eur", s.UsdPerEur);
        s.SalesOverheadPct = D("sales_overhead", s.SalesOverheadPct);
        s.Hotkeys = S("hotkeys", s.Hotkeys);
        s.OrderPdf = OrderPdfOptions.Parse(S("order_pdf", "")) ?? OrderPdfOptions.Default;
        s.Company = new CompanyInfo(S("company_name", ""), S("company_address", ""), S("company_vat", ""), S("company_tax_office", ""),
            S("company_phone", ""), S("company_email", ""), S("company_name_en", ""), S("company_address_en", ""));
        s.BackupFolder = S("backup_folder", s.BackupFolder);
        s.AutoBackup = S("auto_backup", "1") == "1";
        s.BackupKeepCount = (int)D("backup_keep", s.BackupKeepCount);
        s.LastAutoBackupDay = S("last_auto_backup", "");
        return s;
    }

    static void SaveSettingsRows(SqliteConnection c, SqliteTransaction t, AppSettings s)
    {
        string N(decimal d) => d.ToString(CultureInfo.InvariantCulture);
        var rows = new (string, string)[]
        {
            ("labor_rate", N(s.LaborRatePerHour)), ("overhead_rate", N(s.OverheadRatePerHour)),
            ("default_markup", N(s.DefaultMarkupPct)), ("rounding_step", N(s.RoundingStep)),
            ("rounding_mode", ((int)s.RoundingMode).ToString(CultureInfo.InvariantCulture)),
            ("currency_symbol", s.CurrencySymbol), ("language", s.Language), ("theme", s.Theme), ("ui_scale", N(s.UiScale)), ("usd_per_eur", N(s.UsdPerEur)),
            ("sales_overhead", N(s.SalesOverheadPct)), ("hotkeys", s.Hotkeys), ("order_pdf", s.OrderPdf.Serialize()),
            ("company_name", s.Company.Name), ("company_address", s.Company.Address), ("company_vat", s.Company.VatNumber),
            ("company_tax_office", s.Company.TaxOffice), ("company_phone", s.Company.Phone), ("company_email", s.Company.Email),
            ("company_name_en", s.Company.NameEn), ("company_address_en", s.Company.AddressEn),
            ("backup_folder", s.BackupFolder), ("auto_backup", s.AutoBackup ? "1" : "0"),
            ("backup_keep", s.BackupKeepCount.ToString(CultureInfo.InvariantCulture)), ("last_auto_backup", s.LastAutoBackupDay),
        };
        foreach (var (k, v) in rows)
            c.Execute("INSERT INTO settings(key, value) VALUES(@k, @v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                new { k, v }, t);
    }

    /// <summary>Saves the settings and updates every price that depends on what changed.</summary>
    public RecalcSummary SaveSettings(AppSettings s) => Write(DataChange.Settings | DataChange.Items | DataChange.Notifications, (c, t) =>
    {
        var old = LoadSettings(c, t);
        SaveSettingsRows(c, t, s);
        var req = new RecalcRequest();
        if (old.LaborRatePerHour != s.LaborRatePerHour || old.OverheadRatePerHour != s.OverheadRatePerHour) req.AllProducts = true;
        if (old.UsdPerEur != s.UsdPerEur) { req.UsdItems = true; req.ExchangeRate = true; }
        if (old.DefaultMarkupPct != s.DefaultMarkupPct || old.RoundingStep != s.RoundingStep || old.RoundingMode != s.RoundingMode)
            req.AllItems = true;
        return req.IsEmpty ? new RecalcSummary() : new PricingEngine(this, c, t).Run(req);
    });

    /// <summary>Saves settings that never affect prices (language, backups…).</summary>
    public void SaveNonPricingSettings(AppSettings s) => Write(DataChange.Settings, (c, t) =>
    {
        var old = LoadSettings(c, t);
        old.CurrencySymbol = s.CurrencySymbol;
        old.Language = s.Language;
        old.Theme = s.Theme;
        old.UiScale = s.UiScale;
        old.Hotkeys = s.Hotkeys;
        old.OrderPdf = s.OrderPdf;
        old.Company = s.Company;
        old.BackupFolder = s.BackupFolder;
        old.AutoBackup = s.AutoBackup;
        old.BackupKeepCount = s.BackupKeepCount;
        old.LastAutoBackupDay = s.LastAutoBackupDay;
        SaveSettingsRows(c, t, old);
    });

    /// <summary>Our logo for order PDFs (PNG or JPEG bytes), or null.</summary>
    public byte[]? GetLogo() => Read(c => c.ExecuteScalar<byte[]?>("SELECT data FROM blobs WHERE key='logo'"));

    /// <summary>Sets (or with null removes) the logo.</summary>
    public void SetLogo(byte[]? data) => Write(DataChange.Settings, (c, t) =>
    {
        if (data is null) c.Execute("DELETE FROM blobs WHERE key='logo'", transaction: t);
        else c.Execute("INSERT INTO blobs(key, data) VALUES('logo', @data) ON CONFLICT(key) DO UPDATE SET data = excluded.data", new { data }, t);
    });

    /// <summary>Recomputes every price from scratch (repair tool; normally never needed).</summary>
    public RecalcSummary RecalculateAll() => Write(DataChange.Items | DataChange.Notifications,
        (c, t) => new PricingEngine(this, c, t).Run(new RecalcRequest { AllItems = true }));

    // ---------- helpers ----------

    internal static void FillTemp(SqliteConnection c, SqliteTransaction t, string table, IEnumerable<long> ids)
    {
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = t;
            cmd.CommandText = $"CREATE TEMP TABLE IF NOT EXISTS {table}(id INTEGER PRIMARY KEY); DELETE FROM {table};";
            cmd.ExecuteNonQuery();
        }
        using var ins = c.CreateCommand();
        ins.Transaction = t;
        ins.CommandText = $"INSERT OR IGNORE INTO {table}(id) VALUES($id)";
        var p = ins.Parameters.Add("$id", SqliteType.Integer);
        ins.Prepare();
        foreach (var id in ids)
        {
            p.Value = id;
            ins.ExecuteNonQuery();
        }
    }

    internal static int NextPosition(SqliteConnection c, SqliteTransaction t, string sql, object args) =>
        (int)(c.ExecuteScalar<long?>(sql, args, t) ?? -1) + 1;
}
