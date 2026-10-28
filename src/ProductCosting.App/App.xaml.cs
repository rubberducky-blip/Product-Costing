using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.Views;

namespace ProductCosting.App;

public partial class App : Application
{
    Mutex? _single;
    DispatcherTimer? _backupTimer;
    string? _smokeDir;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        // --smoke <folder>: start on demo data, visit every screen, save screenshots and exit (used by CI)
        var smokeAt = Array.IndexOf(e.Args, "--smoke");
        if (smokeAt >= 0) _smokeDir = Path.GetFullPath(e.Args.Length > smokeAt + 1 ? e.Args[smokeAt + 1] : "smoke");

        _single = new Mutex(true, @"Local\ProductCosting.SingleInstance", out var first);
        if (!first && _smokeDir is null)
        {
            MessageBox.Show("Η εφαρμογή είναι ήδη ανοιχτή.\nThe app is already running.", "Product Costing", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            if (_smokeDir is not null)
            {
                Smoke.Prepare(_smokeDir);
                AppState.Init(Path.Combine(_smokeDir, "data"));
                Smoke.Seed();
            }
            else AppState.Init();
        }
        catch (Exception ex)
        {
            if (_smokeDir is not null) Smoke.Fail("init", ex);
            else MessageBox.Show(ex.Message, "Product Costing", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        Loc.I.SetLanguage(AppState.Settings.Language);
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(Loc.Culture.IetfLanguageTag)));

        var main = new MainWindow();
        MainWindow = main;
        main.Show();

        if (_smokeDir is not null)
        {
            Dispatcher.BeginInvoke(async () => Shutdown(await Smoke.Run(main)), DispatcherPriority.ApplicationIdle);
            return;
        }

        // daily backup: now (if due) and then every hour while the app stays open
        _backupTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _backupTimer.Tick += (_, _) => RunAutoBackup();
        _backupTimer.Start();
        Dispatcher.BeginInvoke(RunAutoBackup, DispatcherPriority.ApplicationIdle);
    }

    static async void RunAutoBackup()
    {
        try
        {
            await Task.Run(() => AppState.Backups.RunAutoBackupIfDue());
        }
        catch (Exception ex)
        {
            AppState.LogError(ex);
        }
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (_smokeDir is not null)
        {
            Smoke.Fail("unhandled", e.Exception);
            return;
        }
        AppState.LogError(e.Exception);
        try
        {
            Dialogs.Error(e.Exception.Message);
        }
        catch
        {
            MessageBox.Show(e.Exception.Message, "Product Costing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _backupTimer?.Stop();
        _single?.Dispose();
        base.OnExit(e);
    }
}
