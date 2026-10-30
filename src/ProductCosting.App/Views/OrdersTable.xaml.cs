using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.ViewModels;

namespace ProductCosting.App.Views;

/// <summary>Saved orders in three tables (draft, executed, archived) with double-click and right-click actions.</summary>
public partial class OrdersTable : UserControl
{
    public sealed record SectionData(string Title, IList Rows)
    {
        public bool HasRows => Rows.Count > 0;
    }

    public static readonly DependencyProperty DraftsProperty = Register(nameof(Drafts));
    public static readonly DependencyProperty ExecutedProperty = Register(nameof(Executed));
    public static readonly DependencyProperty ArchivedProperty = Register(nameof(Archived));
    public static readonly DependencyProperty ShowSupplierProperty = DependencyProperty.Register(
        nameof(ShowSupplier), typeof(bool), typeof(OrdersTable), new PropertyMetadata(false));

    static DependencyProperty Register(string name) => DependencyProperty.Register(
        name, typeof(IList), typeof(OrdersTable), new PropertyMetadata(null, (d, _) => ((OrdersTable)d).Rebuild()));

    public IList? Drafts { get => (IList?)GetValue(DraftsProperty); set => SetValue(DraftsProperty, value); }
    public IList? Executed { get => (IList?)GetValue(ExecutedProperty); set => SetValue(ExecutedProperty, value); }
    public IList? Archived { get => (IList?)GetValue(ArchivedProperty); set => SetValue(ArchivedProperty, value); }
    public bool ShowSupplier { get => (bool)GetValue(ShowSupplierProperty); set => SetValue(ShowSupplierProperty, value); }

    public OrdersTable() => InitializeComponent();

    void Rebuild() => Sections.ItemsSource = new List<SectionData>
    {
        new(Loc.T("order.drafts"), Drafts ?? new List<SavedOrderRow>()),
        new(Loc.T("order.executed"), Executed ?? new List<SavedOrderRow>()),
        new(Loc.T("order.archived"), Archived ?? new List<SavedOrderRow>()),
    };

    // the supplier column is shown on the Orders page only
    void Grid_Loaded(object sender, RoutedEventArgs e) =>
        ((DataGrid)sender).Columns[0].Visibility = ShowSupplier ? Visibility.Visible : Visibility.Collapsed;

    static DataGridRow? RowOf(object source)
    {
        var d = source as DependencyObject;
        while (d is not null and not DataGridRow) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as DataGridRow;
    }

    void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowOf(e.OriginalSource) is { DataContext: SavedOrderRow row }) OrderActions.Open(row);
    }

    void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var grid = (DataGrid)sender;
        if (RowOf(e.OriginalSource) is not { DataContext: SavedOrderRow row }) { e.Handled = true; return; }
        grid.SelectedItem = row;
        foreach (var mi in grid.ContextMenu.Items.OfType<MenuItem>())
            mi.Visibility = (mi.Tag as string) switch
            {
                "draft" => row.IsDraft ? Visibility.Visible : Visibility.Collapsed,
                "executed" => row.IsExecuted ? Visibility.Visible : Visibility.Collapsed,
                "archived" => row.IsArchived ? Visibility.Visible : Visibility.Collapsed,
                _ => Visibility.Visible,
            };
    }

    static SavedOrderRow? Selected(object sender) =>
        sender is MenuItem { Parent: ContextMenu { PlacementTarget: DataGrid { SelectedItem: SavedOrderRow row } } } ? row : null;

    void Menu_Open(object sender, RoutedEventArgs e) { if (Selected(sender) is { } r) OrderActions.Open(r); }
    void Menu_Export(object sender, RoutedEventArgs e) { if (Selected(sender) is { } r) OrderActions.Export(r); }
    void Menu_Execute(object sender, RoutedEventArgs e) { if (Selected(sender) is { } r) OrderActions.Execute(r); }
    void Menu_Archive(object sender, RoutedEventArgs e) { if (Selected(sender) is { } r) OrderActions.Archive(r); }
    void Menu_Unarchive(object sender, RoutedEventArgs e) { if (Selected(sender) is { } r) OrderActions.Unarchive(r); }
    void Menu_Delete(object sender, RoutedEventArgs e) { if (Selected(sender) is { } r) OrderActions.Delete(r); }
}
