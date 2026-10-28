using System.Windows;
using System.Windows.Controls;
using ProductCosting.App.ViewModels;

namespace ProductCosting.App.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    void Keep_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.SaveKeepCommand.Execute(null);
    }
}
