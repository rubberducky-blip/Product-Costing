using System.Globalization;
using System.Windows;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.Views;
using ProductCosting.Core.Model;

namespace ProductCosting.App.ViewModels;

/// <summary>A saved order as listed on the supplier and on the Orders page.</summary>
public sealed class SavedOrderRow(PurchaseOrder o)
{
    public PurchaseOrder Model { get; } = o;
    public string SupplierName => Model.SupplierName;
    /// <summary>Drafts: last change; executed / archived: the day they were executed.</summary>
    public string DateText => Date(Model.Status == OrderStatus.Draft ? Model.UpdatedAt : Model.ExecutedAt ?? Model.UpdatedAt);
    public int LineCount => Model.LineCount;
    public string TotalText => Fmt.Num(Model.TotalQuantity);
    public string ValueText => Fmt.InCurrency(Model.TotalValue, Model.SupplierCurrency);
    public bool IsDraft => Model.Status == OrderStatus.Draft;
    public bool IsExecuted => Model.Status == OrderStatus.Executed;
    public bool IsArchived => Model.Status == OrderStatus.Archived;

    static string Date(string s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("g", Loc.Culture) : s;

    public static (List<SavedOrderRow> Drafts, List<SavedOrderRow> Executed, List<SavedOrderRow> Archived) Split(IEnumerable<PurchaseOrder> orders)
    {
        var rows = orders.Select(o => new SavedOrderRow(o)).ToList();
        return (rows.Where(r => r.IsDraft).ToList(), rows.Where(r => r.IsExecuted).ToList(), rows.Where(r => r.IsArchived).ToList());
    }
}

/// <summary>What can be done with a saved order (from any list of orders).</summary>
public static class OrderActions
{
    static Window Owner => Application.Current.MainWindow;

    public static void Open(SavedOrderRow row)
    {
        if (AppState.Store.GetSupplier(row.Model.SupplierId) is { } s)
            new OrderWindow(s, row.Model.Id) { Owner = Owner }.ShowDialog();
    }

    public static void Export(SavedOrderRow row)
    {
        if (AppState.Store.GetSupplier(row.Model.SupplierId) is { } s) OrderWindow.ExportSaved(s, row.Model, Owner);
    }

    public static void Execute(SavedOrderRow row)
    {
        if (!Dialogs.Confirm(Loc.T("order.executeConfirm"), Loc.T("order.execute"))) return;
        AppState.Store.SetOrderStatus(row.Model.Id, OrderStatus.Executed);
        if (Dialogs.Confirm(Loc.T("order.executedPdf"), Loc.T("order.export"))) Export(row);
    }

    public static void Archive(SavedOrderRow row) => AppState.Store.SetOrderStatus(row.Model.Id, OrderStatus.Archived);

    public static void Unarchive(SavedOrderRow row) => AppState.Store.SetOrderStatus(row.Model.Id, OrderStatus.Executed);

    public static void Delete(SavedOrderRow row)
    {
        if (Dialogs.Confirm(Loc.F("order.confirmDelete", row.DateText), Loc.T("common.delete"), danger: true))
            AppState.Store.DeleteOrder(row.Model.Id);
    }

    /// <summary>Ctrl+5 / "New order": asks for the supplier, then opens a new order.</summary>
    public static void New()
    {
        var pick = new SupplierPickWindow { Owner = Owner };
        if (pick.ShowDialog() == true && pick.SupplierId is long id && AppState.Store.GetSupplier(id) is { } s)
            new OrderWindow(s) { Owner = Owner }.ShowDialog();
    }
}
