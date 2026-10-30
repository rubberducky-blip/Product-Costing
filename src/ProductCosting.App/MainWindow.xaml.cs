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
        // Ctrl+F (also Ctrl+Φ: on the Greek layout it is the same key)
        InputBindings.Add(new KeyBinding(FocusSearchCommand, Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(SaveCommand, Key.S, ModifierKeys.Control));
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
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.Z when focus != SearchBox && _vm.Current is { SupportsUndo: true } page:
                    e.Handled = page.Undo();
                    break;
                case >= Key.D1 and <= Key.D5:
                    _vm.NewByShortcut(key - Key.D0);
                    e.Handled = true;
                    break;
                case >= Key.NumPad1 and <= Key.NumPad5:
                    _vm.NewByShortcut(key - Key.NumPad0);
                    e.Handled = true;
                    break;
                case Key.B:
                    _vm.BackupNow();
                    e.Handled = true;
                    break;
            }
            return;
        }
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
