using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProductCosting.App.Infrastructure;
using ProductCosting.Core.Localization;
using ProductCosting.Core.Model;

namespace ProductCosting.App.ViewModels;

public sealed class SearchResult(SearchHit hit)
{
    public SearchHit Hit { get; } = hit;
    public string Code => Hit.IsSupplier ? "" : Hit.Code;
    public string Name => Hit.Name;
    public string KindText => Hit.IsSupplier ? Loc.T("kind.supplier") : Strings.Kind(Hit.Kind);
    public string PriceText => Hit.IsSupplier ? "" : Fmt.Money(Hit.Price);
    public string Glyph => Hit.IsSupplier ? "\uE716" : Hit.Kind switch
    {
        ItemKind.Product => "\uE7B8",
        ItemKind.Merchandise => "\uE719",
        _ => "\uE81E",
    };
}

public sealed partial class SearchViewModel : ObservableObject
{
    readonly MainViewModel _main;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(160) };

    public ObservableCollection<SearchResult> Results { get; } = [];

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private SearchResult? _selected;
    [ObservableProperty] private bool _noResults;

    public SearchViewModel(MainViewModel main)
    {
        _main = main;
        _debounce.Tick += (_, _) => { _debounce.Stop(); Run(); };
    }

    partial void OnQueryChanged(string value)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    void Run()
    {
        Results.Clear();
        var q = Query.Trim();
        if (q.Length == 0)
        {
            IsOpen = false;
            return;
        }
        foreach (var h in AppState.Store.Search(q)) Results.Add(new SearchResult(h));
        Selected = Results.FirstOrDefault();
        NoResults = Results.Count == 0;
        IsOpen = true;
    }

    public void Move(int dir)
    {
        if (Results.Count == 0) return;
        var i = Selected is null ? -1 : Results.IndexOf(Selected);
        Selected = Results[Math.Clamp(i + dir, 0, Results.Count - 1)];
    }

    [RelayCommand]
    public void Open(SearchResult? r)
    {
        r ??= Selected;
        if (r is null) return;
        IsOpen = false;
        if (r.Hit.IsSupplier) _main.OpenSupplier(r.Hit.Id);
        else _main.OpenItem(r.Hit.Id);
    }

    public void Clear()
    {
        Query = "";
        IsOpen = false;
    }
}
