using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.ViewModels;

namespace ProductCosting.App.Views;

/// <summary>
/// Saved orders in one table per state (draft, executed and, when bound, archived) with double-click and right-click
/// actions. The drafts table carries the "new order" button.
/// </summary>
public partial class OrdersTable : UserControl
{
    public sealed record SectionData(string Title, IList Rows, ICommand? NewCommand = null)
    {
        public bool HasRows => Rows.Count > 0;
        public bool HasNew => NewCommand is not null;
    }

    public static readonly DependencyProperty DraftsProperty = Register(nameof(Drafts));
    public static readonly DependencyProperty ExecutedProperty = Register(nameof(Executed));
    public static readonly DependencyProperty ArchivedProperty = Register(nameof(Archived));
    public static readonly DependencyProperty ShowSupplierProperty = DependencyProperty.Register(
        nameof(ShowSupplier), typeof(bool), typeof(OrdersTable), new PropertyMetadata(false));
    public static readonly DependencyProperty NewOrderCommandProperty = DependencyProperty.Register(
        nameof(NewOrderCommand), typeof(ICommand), typeof(OrdersTable), new PropertyMetadata(null, (d, _) => ((OrdersTable)d).Rebuild()));
    public static readonly DependencyProperty SideBySideProperty = DependencyProperty.Register(
        nameof(SideBySide), typeof(bool), typeof(OrdersTable), new PropertyMetadata(false, (d, _) => ((OrdersTable)d).Arrange()));

    static DependencyProperty Register(string name) => DependencyProperty.Register(
        name, typeof(IList), typeof(OrdersTable), new PropertyMetadata(null, (d, _) => ((OrdersTable)d).Rebuild()));

    public IList? Drafts { get => (IList?)GetValue(DraftsProperty); set => SetValue(DraftsProperty, value); }
    public IList? Executed { get => (IList?)GetValue(ExecutedProperty); set => SetValue(ExecutedProperty, value); }
    public IList? Archived { get => (IList?)GetValue(ArchivedProperty); set => SetValue(ArchivedProperty, value); }
    public bool ShowSupplier { get => (bool)GetValue(ShowSupplierProperty); set => SetValue(ShowSupplierProperty, value); }
    public ICommand? NewOrderCommand { get => (ICommand?)GetValue(NewOrderCommandProperty); set => SetValue(NewOrderCommandProperty, value); }
    public bool SideBySide { get => (bool)GetValue(SideBySideProperty); set => SetValue(SideBySideProperty, value); }

    public OrdersTable() => InitializeComponent();

    void Rebuild()
    {
        var sections = new List<SectionData>
        {
            new(Loc.T("order.drafts"), Drafts ?? new List<SavedOrderRow>(), NewOrderCommand),
            new(Loc.T("order.executed"), Executed ?? new List<SavedOrderRow>()),
        };
        // archived orders are shown on the supplier only (the Orders page leaves them unbound)
        if (Archived is not null) sections.Add(new(Loc.T("order.archived"), Archived));
        Sections.ItemsSource = sections;
    }

    void Arrange()
    {
        Sections.ItemsPanel = SideBySide ? (ItemsPanelTemplate)Resources["Columns"] : new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));
        Sections.ItemContainerStyle = SideBySide ? (Style)Resources["ColumnItem"] : null;
        Sections.Margin = SideBySide ? new Thickness(-8, 0, -8, 0) : new Thickness();
    }

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
