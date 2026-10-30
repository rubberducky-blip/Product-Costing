using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.Views;
using ProductCosting.Core.Data;
using ProductCosting.Core.Model;

namespace ProductCosting.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public static MainViewModel Instance { get; private set; } = null!;

    readonly Dictionary<ListScope, ListPageViewModel> _lists = [];
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };
    DataChange _pending;
    bool _flushQueued;

    public NotificationsViewModel Notifications { get; }
    public SearchViewModel Search { get; }

    [ObservableProperty] private PageViewModel? _current;
    [ObservableProperty] private string? _toast;
    [ObservableProperty] private bool _isBusy;

    public string Section => Current?.Section ?? "";
    public string Version => "v" + typeof(MainViewModel).Assembly.GetName().Version?.ToString(3);

    public MainViewModel()
    {
        Instance = this;
        Notifications = new NotificationsViewModel(this);
        Search = new SearchViewModel(this);
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast = null; };
        AppState.Store.Changed += (_, e) => Application.Current.Dispatcher.BeginInvoke(() => QueueChange(e.Change));
        Current = List(ListScope.Products);
        Current.OnShown();
        Notifications.Refresh();
    }

    partial void OnCurrentChanged(PageViewModel? value) => OnPropertyChanged(nameof(Section));

    ListPageViewModel List(ListScope scope)
    {
        if (!_lists.TryGetValue(scope, out var vm)) _lists[scope] = vm = new ListPageViewModel(this, scope);
        return vm;
    }

    void QueueChange(DataChange change)
    {
        _pending |= change;
        if (_flushQueued) return;
        _flushQueued = true;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var c = _pending;
            _pending = DataChange.None;
            _flushQueued = false;
            if (c.HasFlag(DataChange.Settings)) AppState.ReloadSettings();
            foreach (var l in _lists.Values) if (l != Current) l.IsStale = true;
            Current?.OnDataChanged(c);
            if (c.HasFlag(DataChange.Notifications) || c.HasFlag(DataChange.Items)) Notifications.Refresh();
        }, DispatcherPriority.Background);
    }

    public void ShowToast(string message)
    {
        Toast = message;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    /// <summary>Asks what to do with unsaved edits. False = stay on the current page.</summary>
    public bool CanLeave()
    {
        if (Current is not { IsDirty: true } page) return true;
        switch (Dialogs.Unsaved())
        {
            case Dialogs.LeaveChoice.Save: return page.TrySave();
            case Dialogs.LeaveChoice.Discard: page.Discard(); return true;
            default: return false;
        }
    }

    // pages visited before the current one, for the back buttons
    readonly List<PageViewModel> _history = [];

    /// <summary>Shows a page. With <paramref name="remember"/>, the current page is kept so Back can return to it.</summary>
    public bool Navigate(PageViewModel page, bool remember = true)
    {
        if (page == Current) return true;
        if (!CanLeave())
        {
            OnPropertyChanged(nameof(Section));   // undo the sidebar click
            return false;
        }
        if (remember && Current is not null)
        {
            _history.Remove(Current);
            _history.Add(Current);
            if (_history.Count > 50) _history.RemoveAt(0);
        }
        Current = page;
        page.OnShown();
        return true;
    }

    /// <summary>Back: returns to the page this one was opened from, or to the given list.</summary>
    public void GoBack(ListScope fallback)
    {
        while (_history.Count > 0)
        {
            var previous = _history[^1];
            if (previous.IsGone) { _history.RemoveAt(_history.Count - 1); continue; }
            if (Navigate(previous, remember: false)) _history.RemoveAt(_history.Count - 1);
            return;
        }
        Navigate(List(fallback), remember: false);
    }

    [RelayCommand]
    void Go(string section)
    {
        PageViewModel page = section switch
        {
            "merchandise" => List(ListScope.Merchandise),
            "raw" => List(ListScope.RawMaterials),
            "suppliers" => List(ListScope.Suppliers),
            "settings" => new SettingsViewModel(this),
            "orders" => Current as OrdersPageViewModel ?? new OrdersPageViewModel(),
            _ => List(ListScope.Products),
        };
        if (page is SettingsViewModel && Current is SettingsViewModel) return;
        // the sidebar starts a fresh trail, and a list always opens at its top level
        if (page is ListPageViewModel list && page == Current)
        {
            list.OpenFolder(null);
            return;
        }
        if (!Navigate(page, remember: false)) return;
        _history.Clear();
        if (page is ListPageViewModel opened && (opened.FolderId is not null || opened.IsFiltering)) opened.OpenFolder(null);
    }

    public void GoToList(ListScope scope) => Navigate(List(scope), remember: false);

    public void OpenItem(long id, ListScope? from = null, IReadOnlyList<long>? trail = null)
    {
        var doc = AppState.Store.LoadItem(id);
        if (doc is null) return;
        var scope = from ?? doc.Item.Kind.ToScope();
        Navigate(new ItemEditorViewModel(this, doc, scope, trail));
    }

    public void NewItem(ItemKind kind, long? folderId)
    {
        var doc = new ItemDocument
        {
            Item = new Item { Kind = kind, FolderId = folderId, Unit = QtyUnit.Pcs },
        };
        if (kind != ItemKind.Product) doc.Sources.Add(new ItemSource { PurchaseQty = 1, Weight = 1 });
        Navigate(new ItemEditorViewModel(this, doc, kind.ToScope()));
    }

    public void OpenSupplier(long id, IReadOnlyList<long>? trail = null)
    {
        var s = AppState.Store.GetSupplier(id);
        if (s is not null) Navigate(new SupplierEditorViewModel(this, s, trail));
    }

    // ---------- keyboard shortcuts ----------

    static readonly string[] Sections = ["products", "merchandise", "raw", "suppliers", "orders"];

    /// <summary>← / →: the previous or next record of the table the form was opened from.</summary>
    public void Step(int dir)
    {
        switch (Current)
        {
            case ItemEditorViewModel { IsNew: false } ed:
            {
                var trail = ed.Trail ?? ListOrder(ed.From);
                var i = trail?.ToList().IndexOf(ed.Id) ?? -1;
                if (trail is null || i < 0 || i + dir < 0 || i + dir >= trail.Count) return;
                if (AppState.Store.LoadItem(trail[i + dir]) is { } doc)
                    Navigate(new ItemEditorViewModel(this, doc, ed.From, trail), remember: false);
                break;
            }
            case SupplierEditorViewModel { IsNew: false } se:
            {
                var trail = se.Trail ?? ListOrder(ListScope.Suppliers);
                var i = trail?.ToList().IndexOf(se.Id) ?? -1;
                if (trail is null || i < 0 || i + dir < 0 || i + dir >= trail.Count) return;
                if (AppState.Store.GetSupplier(trail[i + dir]) is { } s)
                    Navigate(new SupplierEditorViewModel(this, s, trail), remember: false);
                break;
            }
        }
    }

    IReadOnlyList<long>? ListOrder(ListScope scope) =>
        _lists.TryGetValue(scope, out var l) ? l.Rows.Cast<object>().Select(ListPageViewModel.IdOf).ToList() : null;

    /// <summary>↑ / ↓: the previous or next list in the sidebar.</summary>
    public void StepSection(int dir)
    {
        var i = Array.IndexOf(Sections, Section);
        var next = i < 0 ? (dir > 0 ? 0 : Sections.Length - 1) : i + dir;
        if (next < 0 || next >= Sections.Length) return;
        Go(Sections[next]);
    }

    /// <summary>Ctrl+1…4: a new product, merchandise, raw material or supplier (in the folder open in its list).</summary>
    public void NewByShortcut(int n)
    {
        if (n == 5)
        {
            OrderActions.New();
            return;
        }
        var scope = (ListScope)n;
        var folder = Current is ListPageViewModel l && l.Scope == scope ? l.FolderId : null;
        if (scope == ListScope.Suppliers) NewSupplier(folder);
        else NewItem((ItemKind)n, folder);
    }

    public void OpenSettings() => Go("settings");

    /// <summary>Ctrl+B: a backup now, in the folder chosen in Settings.</summary>
    public async void BackupNow()
    {
        try
        {
            BackupInfo? b = null;
            await RunBusy(async () => b = await Task.Run(() => AppState.Backups.BackupNow()));
            ShowToast(Loc.F("settings.backupCreated", b!.FileName));
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.F("settings.backupFailed", ex.Message));
        }
    }

    public void NewSupplier(long? folderId) => Navigate(new SupplierEditorViewModel(this, new Supplier { FolderId = folderId }));

    /// <summary>Rebuilds every page, e.g. after the language changed or a backup was restored.</summary>
    public void ResetPages(PageViewModel? keep = null)
    {
        _lists.Clear();
        _history.Clear();
        if (keep is null)
        {
            Current = List(ListScope.Products);
            Current.OnShown();
        }
        Notifications.Refresh();
    }

    public async Task RunBusy(Func<Task> work)
    {
        IsBusy = true;
        try { await work(); }
        finally { IsBusy = false; }
    }
}
