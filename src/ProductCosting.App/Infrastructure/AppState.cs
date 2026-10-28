using System.IO;
using ProductCosting.Core.Data;
using ProductCosting.Core.Model;
using ProductCosting.Core.Services;

namespace ProductCosting.App.Infrastructure;

/// <summary>The app's services, created once at start-up.</summary>
public static class AppState
{
    public static Store Store { get; private set; } = null!;
    public static BackupService Backups { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = new();
    public static string DataFolder { get; private set; } = "";
    public static string DatabasePath => Store.Db.Path;

    public static void Init(string? dataFolder = null)
    {
        DataFolder = dataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProductCosting");
        Directory.CreateDirectory(DataFolder);
        Store = new Store(new Db(Path.Combine(DataFolder, "data.db")));
        Backups = new BackupService(Store);
        ReloadSettings();
        if (string.IsNullOrWhiteSpace(Settings.BackupFolder))
        {
            Settings.BackupFolder = BackupService.DefaultFolder;
            Store.SaveNonPricingSettings(Settings);
        }
    }

    public static void ReloadSettings() => Settings = Store.LoadSettings();

    public static void LogError(Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(DataFolder, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch (IOException) { }
    }
}
