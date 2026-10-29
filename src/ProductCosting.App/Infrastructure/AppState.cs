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

    static string LogFolder => string.IsNullOrEmpty(DataFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProductCosting")
        : DataFolder;

    /// <summary>Start-up / shutdown diary (app.log), to find out why the app closed. Kept under ~200 KB.</summary>
    public static void LogEvent(string text)
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            var file = Path.Combine(LogFolder, "app.log");
            if (File.Exists(file) && new FileInfo(file).Length > 200_000) File.Delete(file);
            File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] pid {Environment.ProcessId}: {text}\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void LogError(Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(DataFolder, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch (IOException) { }
    }
}
