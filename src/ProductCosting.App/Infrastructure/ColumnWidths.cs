using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ProductCosting.App.Views;

namespace ProductCosting.App.Infrastructure;

/// <summary>
/// Column widths a person sets on a table (by dragging a column edge or from the right-click menu of a column title),
/// kept under a name shared by every table of the same kind (e.g. all product tables, in any folder).
/// </summary>
public sealed class ColumnWidths
{
    readonly DataGrid _grid;
    readonly string _key;
    readonly Dictionary<DataGridColumn, DataGridLength> _defaults = [];
    readonly DispatcherTimer _save;
    bool _applying;

    ColumnWidths(DataGrid grid, string key)
    {
        _grid = grid;
        _key = key;
        _save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _save.Tick += (_, _) => { _save.Stop(); Save(); };
        foreach (var col in grid.Columns)
        {
            _defaults[col] = col.Width;
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn))
                .AddValueChanged(col, (_, _) => { if (!_applying) { _save.Stop(); _save.Start(); } });
        }
        grid.CanUserResizeColumns = true;
        grid.PreviewMouseRightButtonUp += OnRightClick;
    }

    static readonly Dictionary<DataGrid, ColumnWidths> Attached = [];

    /// <summary>Remembers the table's widths under <paramref name="key"/> (switching keys applies that kind's widths).</summary>
    public static void Use(DataGrid grid, string key)
    {
        if (!Attached.TryGetValue(grid, out var w) || w._key != key)
        {
            if (w is not null) w.Detach();
            Attached[grid] = w = new ColumnWidths(grid, key);
        }
        w.Apply();
    }

    void Detach()
    {
        _grid.PreviewMouseRightButtonUp -= OnRightClick;
        Reset(save: false);
    }

    void Apply()
    {
        _applying = true;
        try
        {
            var stored = Parse(AppState.Store.GetUiValue("columns." + _key));
            for (var i = 0; i < _grid.Columns.Count; i++)
            {
                var col = _grid.Columns[i];
                col.Width = stored.TryGetValue(i, out var px) ? new DataGridLength(px) : _defaults[col];
            }
        }
        finally
        {
            _applying = false;
        }
    }

    static Dictionary<int, double> Parse(string? value)
    {
        var map = new Dictionary<int, double>();
        foreach (var part in (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && int.TryParse(kv[0], out var i)
                && double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w >= 20)
                map[i] = w;
        }
        return map;
    }

    /// <summary>Only the columns whose width differs from the default are kept.</summary>
    void Save()
    {
        var parts = new List<string>();
        for (var i = 0; i < _grid.Columns.Count; i++)
        {
            var col = _grid.Columns[i];
            if (col.Width == _defaults[col]) continue;
            var px = col.Width.IsAbsolute ? col.Width.Value : col.ActualWidth;
            if (px >= 20) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{i}={Math.Round(px)}"));
        }
        AppState.Store.SetUiValue("columns." + _key, parts.Count == 0 ? null : string.Join(";", parts));
    }

    void Reset(bool save)
    {
        _applying = true;
        foreach (var (col, w) in _defaults) col.Width = w;
        _applying = false;
        if (save) AppState.Store.SetUiValue("columns." + _key, null);
    }

    // ---------- right-click on a column title ----------

    void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        var header = Ancestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject);
        if (header?.Column is not { } col) return;
        e.Handled = true;
        var menu = new ContextMenu { PlacementTarget = header };
        var set = new MenuItem { Header = Loc.T("columns.setWidth") };
        set.Click += (_, _) =>
        {
            var text = Dialogs.Prompt(Loc.F("columns.widthTitle", col.Header as string ?? ""), Math.Round(col.ActualWidth).ToString(CultureInfo.CurrentCulture));
            if (text is null) return;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var px) || px < 20 || px > 2000)
            {
                Dialogs.Error(Loc.T("common.invalidNumber"));
                return;
            }
            col.Width = new DataGridLength(px);
            Save();
        };
        var one = new MenuItem { Header = Loc.T("columns.resetOne") };
        one.Click += (_, _) => { col.Width = _defaults[col]; Save(); };
        var all = new MenuItem { Header = Loc.T("columns.resetAll") };
        all.Click += (_, _) => Reset(save: true);
        menu.Items.Add(set);
        menu.Items.Add(one);
        menu.Items.Add(new Separator());
        menu.Items.Add(all);
        menu.IsOpen = true;
    }

    static T? Ancestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null and not T) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }
}
