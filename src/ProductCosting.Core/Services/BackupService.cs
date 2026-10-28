using System.Globalization;
using Microsoft.Data.Sqlite;
using ProductCosting.Core.Data;
using ProductCosting.Core.Model;

namespace ProductCosting.Core.Services;

/// <summary>
/// Backups are complete copies of the database (SQLite online backup, safe while the app runs),
/// written as <c>ProductCosting_yyyy-MM-dd_HHmmss_kind.db</c> into the folder chosen in settings.
/// </summary>
public sealed class BackupService(Store store)
{
    public const string Prefix = "ProductCosting_";

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Product Costing Backups");

    public string Folder(AppSettings s) => string.IsNullOrWhiteSpace(s.BackupFolder) ? DefaultFolder : s.BackupFolder;

    public BackupInfo BackupNow(string kind = "manual")
    {
        var s = store.LoadSettings();
        var dir = Folder(s);
        Directory.CreateDirectory(dir);
        var now = store.Clock();
        var file = Path.Combine(dir, $"{Prefix}{now:yyyy-MM-dd_HHmmss}_{kind}.db");
        for (var i = 2; File.Exists(file); i++)
            file = Path.Combine(dir, $"{Prefix}{now:yyyy-MM-dd_HHmmss}_{kind}-{i}.db");

        using (var src = store.Db.Open())
        using (var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString()))
        {
            dst.Open();
            src.BackupDatabase(dst);
        }
        return Describe(file)!;
    }

    public List<BackupInfo> ListBackups()
    {
        var dir = Folder(store.LoadSettings());
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, Prefix + "*.db")
            .Select(Describe).OfType<BackupInfo>()
            .OrderByDescending(b => b.CreatedAt).ToList();
    }

    static BackupInfo? Describe(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (!name.StartsWith(Prefix)) return null;
        var parts = name[Prefix.Length..].Split('_');
        if (parts.Length < 3 || !DateTime.TryParseExact(parts[0] + "_" + parts[1], "yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            at = File.GetLastWriteTime(file);
        var kind = parts.Length >= 3 ? parts[2].Split('-')[0] : "manual";
        return new BackupInfo { Path = file, CreatedAt = at, Kind = kind, SizeBytes = new FileInfo(file).Length };
    }

    /// <summary>Takes the daily automatic backup if it is on, due today, and keeps only the newest ones.</summary>
    public BackupInfo? RunAutoBackupIfDue()
    {
        var s = store.LoadSettings();
        var today = store.Clock().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!s.AutoBackup || s.LastAutoBackupDay == today) return null;
        var b = BackupNow("auto");
        s.LastAutoBackupDay = today;
        store.SaveNonPricingSettings(s);
        Prune(s.BackupKeepCount);
        return b;
    }

    public void Prune(int keepAuto)
    {
        if (keepAuto <= 0) return;
        foreach (var old in ListBackups().Where(b => b.Kind == "auto").Skip(keepAuto))
        {
            try { File.Delete(old.Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Replaces all data with a backup file. A safety backup of the current data is taken first.
    /// Returns the safety backup.
    /// </summary>
    public BackupInfo Restore(string file)
    {
        if (!Db.LooksLikeAppDatabase(file)) throw new InvalidDataException("Not a valid backup file.");
        var safety = BackupNow("safety");
        // keep where backups go and when the last auto backup ran, they describe this computer, not the data
        var keep = store.LoadSettings();
        store.Exclusive(() =>
        {
            using (var src = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            using (var dst = store.Db.Open())
            {
                src.Open();
                src.BackupDatabase(dst);
            }
            SqliteConnection.ClearAllPools();
            using var c = store.Db.Open();
            Db.Migrate(c);
        });
        var s = store.LoadSettings();
        s.BackupFolder = keep.BackupFolder;
        s.LastAutoBackupDay = keep.LastAutoBackupDay;
        store.SaveNonPricingSettings(s);
        return safety;
    }
}
