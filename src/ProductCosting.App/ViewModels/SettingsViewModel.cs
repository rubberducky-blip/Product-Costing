using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.Views;
using ProductCosting.Core.Data;
using ProductCosting.Core.Model;

namespace ProductCosting.App.ViewModels;

public sealed class BackupRow(BackupInfo b)
{
    public BackupInfo Info { get; } = b;
    public string FileName => Info.FileName;
    public string DateText => Info.CreatedAt.ToString("g", Loc.Culture);
    public string KindText => Loc.T("backup." + (Info.Kind is "auto" or "manual" or "safety" ? Info.Kind : "manual"));
    public long SizeBytes => Info.SizeBytes;
}

public sealed partial class SettingsViewModel : PageViewModel
{
    readonly MainViewModel _main;
    string _original = "";
    bool _loading;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        LaborRate = Track();
        OverheadRate = Track();
        DefaultMarkup = Track();
        RoundingStep = Track();
        UsdPerEur = Track();
        KeepCount = new NumField(30);
        Load();
        LoadBackups();
    }

    public override string Section => "settings";

    public NumField LaborRate { get; }
    public NumField OverheadRate { get; }
    public NumField DefaultMarkup { get; }
    public NumField RoundingStep { get; }
    public NumField UsdPerEur { get; }
    public NumField KeepCount { get; }

    [ObservableProperty] private RoundingMode _roundingMode;
    [ObservableProperty] private string _currencySymbol = "€";
    [ObservableProperty] private string _language = "el";
    [ObservableProperty] private string _theme = "system";
    [ObservableProperty] private decimal _uiScale = 1m;
    [ObservableProperty] private bool _dirty;

    [ObservableProperty] private string _backupFolder = "";
    [ObservableProperty] private bool _autoBackup;
    [ObservableProperty] private string _lastAutoText = "";

    public List<Choice<RoundingMode>> RoundingModes { get; } = Choices.RoundingModes();

    public sealed record Shortcut(string Keys, string Description);

    public List<Shortcut> Shortcuts { get; } =
    [
        new("Ctrl + F", Loc.T("keys.search")),
        new("Ctrl + S", Loc.T("keys.save")),
        new("Ctrl + Z", Loc.T("keys.undo")),
        new("Ctrl + 1", Loc.T("list.newProduct")),
        new("Ctrl + 2", Loc.T("list.newMerchandise")),
        new("Ctrl + 3", Loc.T("list.newRaw")),
        new("Ctrl + 4", Loc.T("list.newSupplier")),
        new("Ctrl + 5", Loc.T("supplier.newOrder")),
        new("← / →", Loc.T("keys.prevNext")),
        new("↑ / ↓", Loc.T("keys.sections")),
        new("Esc", Loc.T("keys.settings")),
        new("Ctrl + B", Loc.T("keys.backup")),
    ];
    public List<Choice<string>> Languages { get; } = [new("el", "Ελληνικά"), new("en", "English")];
    public List<Choice<string>> Themes { get; } = ThemeManager.Themes.Select(t => new Choice<string>(t.Id, Loc.T(t.LabelKey))).ToList();
    public List<Choice<decimal>> Sizes { get; } = Infrastructure.UiScale.Sizes.Select(s => new Choice<decimal>(s.Scale, Loc.T(s.LabelKey))).ToList();
    public ObservableCollection<BackupRow> Backups { get; } = [];
    public bool HasBackups => Backups.Count > 0;
    public string DatabasePath => AppState.DatabasePath;
    public override bool IsDirty => Dirty;

    NumField Track()
    {
        var f = new NumField(0);
        f.Changed += Edited;
        return f;
    }

    void Load()
    {
        _loading = true;
        var s = AppState.Store.LoadSettings();
        LaborRate.Set(s.LaborRatePerHour);
        OverheadRate.Set(s.OverheadRatePerHour);
        DefaultMarkup.Set(s.DefaultMarkupPct);
        RoundingStep.Set(s.RoundingStep);
        UsdPerEur.Set(s.UsdPerEur);
        RoundingMode = s.RoundingMode;
        CurrencySymbol = s.CurrencySymbol;
        Language = s.Language;
        Theme = s.Theme;
        UiScale = s.UiScale;
        BackupFolder = AppState.Backups.Folder(s);
        _autoBackup = s.AutoBackup;
        OnPropertyChanged(nameof(AutoBackup));
        KeepCount.Set(s.BackupKeepCount);
        LastAutoText = string.IsNullOrEmpty(s.LastAutoBackupDay) ? "" : Loc.F("settings.lastAuto",
            DateTime.ParseExact(s.LastAutoBackupDay, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("D", Loc.Culture));
        _loading = false;
        _original = Snapshot();
        Dirty = false;
        ResetUndo();
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(RoundingMode) or nameof(CurrencySymbol) or nameof(Language) or nameof(Theme) or nameof(UiScale)) Edited();
    }

    void Edited()
    {
        if (_loading) return;
        Dirty = Snapshot() != _original;
        NoteEdit();
    }

    sealed record FormState(string Labor, string Overhead, string Markup, string Step, string Usd, RoundingMode Mode, string Symbol, string Language,
        string Theme, decimal Scale);

    protected override object? CaptureState() => new FormState(LaborRate.Text, OverheadRate.Text, DefaultMarkup.Text, RoundingStep.Text,
        UsdPerEur.Text, RoundingMode, CurrencySymbol, Language, Theme, UiScale);

    protected override void RestoreState(object state)
    {
        var f = (FormState)state;
        _loading = true;
        LaborRate.Text = f.Labor; OverheadRate.Text = f.Overhead; DefaultMarkup.Text = f.Markup; RoundingStep.Text = f.Step;
        UsdPerEur.Text = f.Usd; RoundingMode = f.Mode; CurrencySymbol = f.Symbol; Language = f.Language; Theme = f.Theme; UiScale = f.Scale;
        _loading = false;
        Edited();
    }

    protected override string StateKey() => Snapshot();

    string Snapshot()
    {
        string N(NumField f) => f.IsValid ? f.Value?.ToString(CultureInfo.InvariantCulture) ?? "" : "!" + f.Text;
        return string.Join("|", N(LaborRate), N(OverheadRate), N(DefaultMarkup), N(RoundingStep), N(UsdPerEur), RoundingMode, CurrencySymbol.Trim(), Language,
            Theme, UiScale.ToString(CultureInfo.InvariantCulture));
    }

    [RelayCommand]
    void Save() => TrySave();

    [RelayCommand]
    void Cancel() => Load();

    public override void Discard() => Load();

    public override bool TrySave()
    {
        if (!LaborRate.IsValid || !OverheadRate.IsValid || !DefaultMarkup.IsValid || !RoundingStep.IsValid || UsdPerEur.Value is not > 0)
        {
            Dialogs.Error(Loc.T("common.invalidNumber"));
            return false;
        }
        var s = AppState.Store.LoadSettings();
        var langChanged = s.Language != Language;
        var looksChanged = s.Theme != Theme || s.UiScale != UiScale;
        s.Theme = Theme;
        s.UiScale = UiScale;
        s.LaborRatePerHour = LaborRate.Or0;
        s.OverheadRatePerHour = OverheadRate.Or0;
        s.DefaultMarkupPct = DefaultMarkup.Or0;
        s.RoundingStep = RoundingStep.Or0;
        s.UsdPerEur = UsdPerEur.Value!.Value;
        s.RoundingMode = RoundingMode;
        s.CurrencySymbol = string.IsNullOrWhiteSpace(CurrencySymbol) ? "€" : CurrencySymbol.Trim();
        s.Language = Language;
        var sum = AppState.Store.SaveSettings(s);
        AppState.ReloadSettings();
        Load();
        if (looksChanged)
        {
            ThemeManager.Apply(Theme);
            Infrastructure.UiScale.Apply(UiScale);
        }
        if (langChanged) Loc.I.SetLanguage(Language);
        // rebuild pages so colours worked out in code (margins, charts) and texts follow
        if (langChanged || looksChanged) _main.ResetPages(this);
        _main.ShowToast(Loc.F("settings.saved", sum.PriceChanges));
        return true;
    }

    [RelayCommand]
    void RecalculateAll()
    {
        var sum = AppState.Store.RecalculateAll();
        _main.ShowToast(Loc.F("settings.saved", sum.PriceChanges));
    }

    // ---------- backups (saved immediately) ----------

    partial void OnAutoBackupChanged(bool value)
    {
        if (_loading) return;
        var s = AppState.Store.LoadSettings();
        s.AutoBackup = value;
        AppState.Store.SaveNonPricingSettings(s);
    }

    [RelayCommand]
    void SaveKeep()
    {
        if (KeepCount.Value is not { } k || k < 1) return;
        var s = AppState.Store.LoadSettings();
        s.BackupKeepCount = (int)k;
        AppState.Store.SaveNonPricingSettings(s);
    }

    [RelayCommand]
    void ChooseFolder()
    {
        var dlg = new OpenFolderDialog { InitialDirectory = Directory.Exists(BackupFolder) ? BackupFolder : null };
        if (dlg.ShowDialog() != true) return;
        var s = AppState.Store.LoadSettings();
        s.BackupFolder = dlg.FolderName;
        AppState.Store.SaveNonPricingSettings(s);
        BackupFolder = dlg.FolderName;
        LoadBackups();
    }

    [RelayCommand]
    void OpenFolder()
    {
        Directory.CreateDirectory(BackupFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BackupFolder}\"") { UseShellExecute = true });
    }

    void LoadBackups()
    {
        Backups.Clear();
        try
        {
            foreach (var b in AppState.Backups.ListBackups()) Backups.Add(new BackupRow(b));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        OnPropertyChanged(nameof(HasBackups));
    }

    [RelayCommand]
    async Task BackupNow()
    {
        try
        {
            BackupInfo? b = null;
            await _main.RunBusy(async () => b = await Task.Run(() => AppState.Backups.BackupNow()));
            LoadBackups();
            _main.ShowToast(Loc.F("settings.backupCreated", b!.FileName));
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.F("settings.backupFailed", ex.Message));
        }
    }

    [RelayCommand]
    void Restore(BackupRow row) => RestoreFrom(row.Info.Path);

    [RelayCommand]
    void RestoreFile()
    {
        var dlg = new OpenFileDialog { Filter = "Backup (*.db)|*.db|*.*|*.*", InitialDirectory = Directory.Exists(BackupFolder) ? BackupFolder : null };
        if (dlg.ShowDialog() == true) RestoreFrom(dlg.FileName);
    }

    void RestoreFrom(string path)
    {
        if (!Db.LooksLikeAppDatabase(path))
        {
            Dialogs.Error(Loc.T("settings.invalidBackup"));
            return;
        }
        if (!Dialogs.Confirm(Loc.F("settings.confirmRestore", Path.GetFileName(path)), Loc.T("settings.restore"), danger: true)) return;
        try
        {
            var safety = AppState.Backups.Restore(path);
            AppState.ReloadSettings();
            Loc.I.SetLanguage(AppState.Settings.Language);
            Load();
            LoadBackups();
            _main.ResetPages(this);
            Dialogs.Success(Loc.F("settings.restored", safety.FileName));
        }
        catch (Exception ex)
        {
            Dialogs.Error(ex.Message);
        }
    }

    public override void OnDataChanged(DataChange change)
    {
        if (!Dirty) Load();
        LoadBackups();
    }
}
