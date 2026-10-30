using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProductCosting.App.Infrastructure;
using ProductCosting.Core.Data;

namespace ProductCosting.App.ViewModels;

/// <summary>Every saved order of every supplier, by state.</summary>
public sealed partial class OrdersPageViewModel : PageViewModel
{
    public override string Section => "orders";

    [ObservableProperty] private List<SavedOrderRow> _drafts = [];
    [ObservableProperty] private List<SavedOrderRow> _executed = [];
    [ObservableProperty] private List<SavedOrderRow> _archived = [];

    public override void OnShown() => Refresh();

    public override void OnDataChanged(DataChange change) => Refresh();

    void Refresh()
    {
        (Drafts, Executed, Archived) = SavedOrderRow.Split(AppState.Store.GetOrders());
    }

    [RelayCommand]
    void New() => OrderActions.New();
}
