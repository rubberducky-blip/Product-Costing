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
        AppState.LogEvent($"start v{typeof(App).Assembly.GetName().Version} {Environment.ProcessPath} {string.Join(' ', e.Args)}");
        AppDomain.CurrentDomain.UnhandledException += (_, a) => AppState.LogEvent($"fatal: {a.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, a) => { AppState.LogEvent($"task error: {a.Exception}"); a.SetObserved(); };
        SessionEnding += (_, a) => AppState.LogEvent($"windows session ending ({a.ReasonSessionEnding})");

        // --smoke <folder>: start on demo data, visit every screen, save screenshots and exit (used by CI)
        var smokeAt = Array.IndexOf(e.Args, "--smoke");
        if (smokeAt >= 0) _smokeDir = Path.GetFullPath(e.Args.Length > smokeAt + 1 ? e.Args[smokeAt + 1] : "smoke");

        _single = new Mutex(true, @"Local\ProductCosting.SingleInstance", out var first);
        if (!first && _smokeDir is null)
        {
            AppState.LogEvent("already running: this second copy closes");
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
        main.Closed += (_, _) => AppState.LogEvent("main window closed");
        if (_smokeDir is null) Watchdog.Watch(main);
        main.Show();
        AppState.LogEvent($"main window shown (graphics tier {System.Windows.Media.RenderCapability.Tier >> 16})");
        if (_smokeDir is null) Watchdog.Start(Dispatcher);

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
            var b = await Task.Run(() => AppState.Backups.RunAutoBackupIfDue());
            if (b is not null) AppState.LogEvent($"daily backup: {b.FileName}");
        }
        catch (Exception ex)
        {
            AppState.LogError(ex);
        }
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        AppState.LogEvent($"error: {e.Exception.GetType().Name}: {e.Exception.Message}");
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
        AppState.LogEvent($"exit (code {e.ApplicationExitCode})");
        _backupTimer?.Stop();
        _single?.Dispose();
        base.OnExit(e);
    }
}
