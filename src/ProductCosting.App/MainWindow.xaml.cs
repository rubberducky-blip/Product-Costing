using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
        Closing += OnClosing;
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_vm.CanLeave()) e.Cancel = true;
    }

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
