using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProductCosting.App.Infrastructure;
using ProductCosting.Core.Data;

namespace ProductCosting.App.ViewModels;

/// <summary>Every open order of every supplier (drafts and executed); archived ones are listed on each supplier only.</summary>
public sealed partial class OrdersPageViewModel : PageViewModel
{
    public override string Section => "orders";

    [ObservableProperty] private List<SavedOrderRow> _drafts = [];
    [ObservableProperty] private List<SavedOrderRow> _executed = [];

    public override void OnShown() => Refresh();

    public override void OnDataChanged(DataChange change) => Refresh();

    void Refresh()
    {
        (Drafts, Executed, _) = SavedOrderRow.Split(AppState.Store.GetOrders());
    }

    [RelayCommand]
    void New() => OrderActions.New();
}
