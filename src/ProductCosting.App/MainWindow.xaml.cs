using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.ViewModels;

namespace ProductCosting.App;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;

    public IRelayCommand FocusSearchCommand { get; }
    public IRelayCommand SaveCommand { get; }

    public MainWindow()
    {
        FocusSearchCommand = new RelayCommand(() => { SearchBox.Focus(); SearchBox.SelectAll(); });
        SaveCommand = new RelayCommand(() =>
        {
            // commit the focused box first, then save the current page
            if (Keyboard.FocusedElement is TextBox tb) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (_vm?.Current is { IsDirty: true } page) page.TrySave();
        });
        InitializeComponent();
        NativeWindow.UseDarkTitleBar(this);
        DataContext = _vm = new MainViewModel();
        PreviewKeyDown += OnShortcut;
        Closing += OnClosing;
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_vm.CanLeave()) e.Cancel = true;
    }

    /// <summary>
    /// App-wide keys (listed in Settings): Ctrl+Z undo, Ctrl+1…4 new records, Ctrl+B backup,
    /// ← / → previous / next record in a form, ↑ / ↓ previous / next list, Esc settings.
    /// Plain keys are left alone while typing, in tables, dropdowns and popups.
    /// </summary>
    void OnShortcut(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var focus = Keyboard.FocusedElement as DependencyObject;
        // a shortcut being recorded in Settings does nothing else
        if (focus is HotkeyBox) return;
        // the keys come from Settings (physical keys: Ctrl+F is also Ctrl+Φ on the Greek layout)
        if (Hotkeys.Matches("search", e)) { FocusSearchCommand.Execute(null); e.Handled = true; return; }
        if (Hotkeys.Matches("save", e)) { SaveCommand.Execute(null); e.Handled = true; return; }
        if (Hotkeys.Matches("undo", e))
        {
            if (focus != SearchBox && _vm.Current is { SupportsUndo: true } page) e.Handled = page.Undo();
            return;
        }
        string[] news = ["newProduct", "newMerchandise", "newRaw", "newSupplier", "newOrder"];
        for (var i = 0; i < news.Length; i++)
            if (Hotkeys.Matches(news[i], e)) { _vm.NewByShortcut(i + 1); e.Handled = true; return; }
        if (Hotkeys.Matches("backup", e)) { _vm.BackupNow(); e.Handled = true; return; }
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        var busy = InPopup(focus) || _vm.Search.IsOpen || _vm.Notifications.IsOpen;
        var typing = focus is TextBoxBase or PasswordBox or ComboBox or ComboBoxItem;
        var inList = focus is DataGrid or DataGridCell or DataGridRow or ListBox or ListBoxItem;
        switch (key)
        {
            case Key.Left or Key.Right when !busy && !typing && !inList:
                _vm.Step(key == Key.Left ? -1 : 1);
                e.Handled = true;
                break;
            case Key.Up or Key.Down when !busy && !typing && !inList:
                _vm.StepSection(key == Key.Up ? -1 : 1);
                e.Handled = true;
                break;
            case Key.Escape when !busy && !(focus is TextBox { Style: var st } && st == FindResource("SearchBox")):
                _vm.OpenSettings();
                e.Handled = true;
                break;
        }
    }

    /// <summary>True for elements shown in a popup (a dropdown's list, the search results…).</summary>
    bool InPopup(DependencyObject? d) =>
        d is Visual v && PresentationSource.FromVisual(v) is { } src && src.RootVisual is not Window;

    void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var s = _vm.Search;
        switch (e.Key)
        {
            case Key.Down: s.Move(1); e.Handled = true; break;
            case Key.Up: s.Move(-1); e.Handled = true; break;
            case Key.Enter: s.Open(null); e.Handled = true; break;
            case Key.Escape: s.Clear(); e.Handled = true; break;
        }
    }

    void SearchResults_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement((ListBox)sender, (DependencyObject)e.OriginalSource) is ListBoxItem { DataContext: SearchResult r })
            _vm.Search.Open(r);
    }
}
