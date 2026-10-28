using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.ViewModels;
using ProductCosting.Core.Model;

namespace ProductCosting.App.Views;

public partial class ListPage : UserControl
{
    const string RowsFormat = "ProductCosting.Rows";
    const string FolderFormat = "ProductCosting.Folder";

    ListPageViewModel? _vm;
    Point _start;
    DataGridRow? _pressRow;
    bool _deferredSelect;
    Border? _pressTile;
    bool _tileDragged;
    (long? Folder, string Filter, double Offset)? _scroll;

    public ListPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Bind();
        Unloaded += (_, _) => Unbind();
        Loaded += (_, _) => Bind();
    }

    void Bind()
    {
        Unbind();
        _vm = DataContext as ListPageViewModel;
        if (_vm is null) return;
        _vm.RowsReplacing += SaveScroll;
        _vm.PropertyChanged += OnVmPropertyChanged;
        TypeCol.Visibility = Vis(_vm.IsRaw);
        SupplierCol.Visibility = Vis(_vm.IsBought);
        SupplierCodeCol.Visibility = Vis(_vm.IsBought);
        UnitCol.Visibility = Vis(_vm.IsBought);
        PurchaseCol.Visibility = Vis(_vm.IsBought);
        ProfitCol.Visibility = Vis(_vm.IsProducts);
    }

    void Unbind()
    {
        if (_vm is null) return;
        _vm.RowsReplacing -= SaveScroll;
        _vm.PropertyChanged -= OnVmPropertyChanged;
    }

    void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ListPageViewModel.Rows)) RestoreScroll();
    }

    static Visibility Vis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;

    DataGrid ActiveGrid => _vm?.IsSuppliers == true ? SuppliersGrid : ItemsGrid;

    // ---------- keep the scroll position when the same folder refreshes ----------

    void SaveScroll()
    {
        if (_vm is null) return;
        var sv = FindChild<ScrollViewer>(ActiveGrid);
        _scroll = (_vm.FolderId, _vm.Filter, sv?.VerticalOffset ?? 0);
    }

    void RestoreScroll()
    {
        if (_vm is null || _scroll is not { } s) return;
        _scroll = null;
        if (s.Folder != _vm.FolderId || s.Filter != _vm.Filter || s.Offset <= 0) return;
        Dispatcher.BeginInvoke(() => FindChild<ScrollViewer>(ActiveGrid)?.ScrollToVerticalOffset(s.Offset),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) return t;
            if (FindChild<T>(c) is { } r) return r;
        }
        return null;
    }

    static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null and not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    // ---------- rows ----------

    void Row_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is DataGridRow { DataContext: ItemRow { HasUnseenChange: true } row }) _vm?.MarkSeen(row);
    }

    void Row_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow r) _vm?.OpenRow(r.DataContext);
    }

    void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var grid = (DataGrid)sender;
        if (e.Key == Key.Enter && grid.SelectedItem is { } item)
        {
            _vm?.OpenRow(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && grid.SelectedItems.Count > 0)
        {
            _vm?.DeleteRows(grid.SelectedItems.Cast<object>().ToList());
            e.Handled = true;
        }
    }

    /// <summary>The Open/Delete menu belongs to rows only, not to headers or empty space.</summary>
    void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null) { e.Handled = true; return; }
        var grid = (DataGrid)sender;
        if (!row.IsSelected) grid.SelectedItem = row.DataContext;
    }

    void Menu_Open(object sender, RoutedEventArgs e)
    {
        if (ActiveGrid.SelectedItem is { } row) _vm?.OpenRow(row);
    }

    void Menu_Delete(object sender, RoutedEventArgs e) =>
        _vm?.DeleteRows(ActiveGrid.SelectedItems.Cast<object>().ToList());

    void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(this);
        _pressRow = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
        _deferredSelect = false;
        if (FindParent<ScrollBar>(e.OriginalSource as DependencyObject) is not null) { _pressRow = null; return; }
        // pressing on one of several selected rows: keep the selection so all of them can be dragged
        var grid = (DataGrid)sender;
        if (_pressRow is { IsSelected: true } && grid.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            _deferredSelect = true;
            e.Handled = true;
        }
    }

    void Grid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_deferredSelect && _pressRow is not null)
        {
            var grid = (DataGrid)sender;
            grid.SelectedItem = _pressRow.DataContext;
        }
        _deferredSelect = false;
        _pressRow = null;
    }

    void Grid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_vm is null || _pressRow is null || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(this) - _start;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var grid = (DataGrid)sender;
        var rows = _pressRow.IsSelected ? grid.SelectedItems.Cast<object>().ToList() : [_pressRow.DataContext];
        var ids = rows.Select(ListPageViewModel.IdOf).Where(id => id != 0).ToArray();
        _pressRow = null;
        _deferredSelect = false;
        if (ids.Length == 0) return;
        DragDrop.DoDragDrop(grid, new DataObject(RowsFormat, ids), DragDropEffects.Move);
    }

    void Row_DragOver(object sender, DragEventArgs e)
    {
        var row = (DataGridRow)sender;
        e.Effects = DragDropEffects.None;
        if (_vm is { CanReorder: true } && e.Data.GetData(RowsFormat) is long[])
        {
            var after = e.GetPosition(row).Y > row.ActualHeight / 2;
            SetHint(row, after ? 2 : 1);
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    void Row_Drop(object sender, DragEventArgs e)
    {
        var row = (DataGridRow)sender;
        var after = DropHint.GetPosition(row) == 2;
        DropHint.SetPosition(row, 0);
        if (_vm is not null && e.Data.GetData(RowsFormat) is long[] ids)
            _vm.ReorderRows(ids, ListPageViewModel.IdOf(row.DataContext), after);
        e.Handled = true;
    }

    void Target_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between the target's own children: only clear when really outside
        if (sender is FrameworkElement el)
        {
            var p = e.GetPosition(el);
            if (p.X >= 0 && p.Y >= 0 && p.X < el.ActualWidth && p.Y < el.ActualHeight) return;
            DropHint.SetPosition(el, 0);
        }
    }

    // ---------- folder tiles ----------

    static void SetHint(DependencyObject d, int hint)
    {
        if (DropHint.GetPosition(d) != hint) DropHint.SetPosition(d, hint);
    }

    static long TileId(object sender) => (long)((FrameworkElement)sender).Tag;

    void Tile_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressTile = (Border)sender;
        _tileDragged = false;
        _start = e.GetPosition(this);
    }

    void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pressTile != sender || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(this) - _start;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _tileDragged = true;
        var id = TileId(sender);
        _pressTile = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(FolderFormat, id), DragDropEffects.Move);
    }

    void Tile_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressTile == sender && !_tileDragged) _vm?.OpenFolder(TileId(sender));
        _pressTile = null;
    }

    void Tile_DragOver(object sender, DragEventArgs e)
    {
        var tile = (Border)sender;
        var id = TileId(sender);
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(RowsFormat) is long[])
        {
            SetHint(tile, 3);
            e.Effects = DragDropEffects.Move;
        }
        else if (e.Data.GetData(FolderFormat) is long folder && folder != id)
        {
            var x = e.GetPosition(tile).X / tile.ActualWidth;
            var hint = _vm is { CanReorder: true } ? (x < 0.25 ? 1 : x > 0.75 ? 2 : 3) : 3;
            SetHint(tile, hint);
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    void Tile_Drop(object sender, DragEventArgs e)
    {
        var tile = (Border)sender;
        var hint = DropHint.GetPosition(tile);
        DropHint.SetPosition(tile, 0);
        var id = TileId(sender);
        if (_vm is null) return;
        if (e.Data.GetData(RowsFormat) is long[] ids) _vm.MoveRows(ids, id);
        else if (e.Data.GetData(FolderFormat) is long folder && folder != id)
        {
            if (hint is 1 or 2) _vm.ReorderFolders(folder, id, hint == 2);
            else _vm.MoveFolder(folder, id);
        }
        e.Handled = true;
    }

    void TileMenu_Open(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FolderTile t }) _vm?.OpenFolder(t.Id);
    }

    void TileMenu_Rename(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FolderTile t }) _vm?.RenameFolderCommand.Execute(t.Id);
    }

    void TileMenu_Delete(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FolderTile t }) _vm?.DeleteFolderCommand.Execute(t.Id);
    }

    // ---------- breadcrumb ----------

    void Crumb_Click(object sender, RoutedEventArgs e) => _vm?.OpenFolder((long?)((Button)sender).Tag);

    void Crumb_DragOver(object sender, DragEventArgs e)
    {
        var b = (Button)sender;
        var target = (long?)b.Tag;
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(RowsFormat) is long[] || (e.Data.GetData(FolderFormat) is long f && f != target))
        {
            SetHint(b, 3);
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    void Crumb_Drop(object sender, DragEventArgs e)
    {
        var b = (Button)sender;
        DropHint.SetPosition(b, 0);
        var target = (long?)b.Tag;
        if (_vm is null) return;
        if (e.Data.GetData(RowsFormat) is long[] ids) _vm.MoveRows(ids, target);
        else if (e.Data.GetData(FolderFormat) is long f && f != target) _vm.MoveFolder(f, target);
        e.Handled = true;
    }

    void FilterBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _vm is not null) { _vm.Filter = ""; e.Handled = true; }
        else if (e.Key == Key.Down) { ActiveGrid.Focus(); if (ActiveGrid.Items.Count > 0) ActiveGrid.SelectedIndex = 0; e.Handled = true; }
    }
}
