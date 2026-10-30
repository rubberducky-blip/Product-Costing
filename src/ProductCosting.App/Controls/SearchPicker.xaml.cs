using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProductCosting.Core.Calc;

namespace ProductCosting.App.Controls;

/// <summary>Search-as-you-type picker showing the folder tree (suppliers, raw materials, folders).</summary>
public partial class SearchPicker : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IList<PickerEntry>), typeof(SearchPicker), new PropertyMetadata(null, (d, _) => ((SearchPicker)d).UpdateDisplay()));

    public static readonly DependencyProperty SelectedIdProperty = DependencyProperty.Register(
        nameof(SelectedId), typeof(long?), typeof(SearchPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SearchPicker)d).UpdateDisplay()));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(SearchPicker), new PropertyMetadata("", (d, _) => ((SearchPicker)d).UpdateDisplay()));

    public IList<PickerEntry>? ItemsSource
    {
        get => (IList<PickerEntry>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public long? SelectedId
    {
        get => (long?)GetValue(SelectedIdProperty);
        set => SetValue(SelectedIdProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Raised after the user picks a value.</summary>
    public event EventHandler? Picked;

    DateTime _closedAt;

    public SearchPicker()
    {
        InitializeComponent();
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        if (DisplayText is null) return;
        var entry = ItemsSource?.FirstOrDefault(e => !e.IsHeader && e.Id == SelectedId);
        if (entry is null || (entry.Id is null && SelectedId is null && string.IsNullOrEmpty(entry.Text)))
        {
            DisplayText.Text = Placeholder;
            DisplayText.Foreground = (Brush)FindResource("TextFaintBrush");
        }
        else
        {
            DisplayText.Text = entry.Display;
            DisplayText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        }
    }

    /// <summary>Opens the list, e.g. right away in a dialog whose only job is this choice.</summary>
    public void OpenDropDown() => Opener_Click(this, new RoutedEventArgs());

    void Opener_Click(object sender, RoutedEventArgs e)
    {
        // the click that closed the popup must not reopen it
        if ((DateTime.Now - _closedAt).TotalMilliseconds < 250) return;
        PopBorder.MinWidth = Math.Max(ActualWidth, 340);
        FilterBox.Text = "";
        Apply();
        Pop.IsOpen = true;
    }

    void Pop_Opened(object? sender, EventArgs e)
    {
        FilterBox.Focus();
        Keyboard.Focus(FilterBox);
        if (List.SelectedItem is not null) List.ScrollIntoView(List.SelectedItem);
    }

    void Pop_Closed(object? sender, EventArgs e) => _closedAt = DateTime.Now;

    void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => Apply();

    void Apply()
    {
        var all = ItemsSource ?? [];
        var q = TextNorm.Normalize(FilterBox.Text).Trim();
        IList items = q.Length == 0
            ? all.ToList()
            : all.Where(x => !x.IsHeader && x.Key.Contains(q)).Select(x => x.Flat()).ToList();
        List.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var current = items.Cast<PickerEntry>().FirstOrDefault(x => !x.IsHeader && x.Id == SelectedId && (x.Id is not null || q.Length == 0));
        List.SelectedItem = current ?? (q.Length > 0 ? items.Cast<PickerEntry>().FirstOrDefault(x => !x.IsHeader) : null);
        if (List.SelectedItem is not null) List.ScrollIntoView(List.SelectedItem);
    }

    void Move(int dir)
    {
        var items = List.Items.Cast<PickerEntry>().ToList();
        if (items.Count == 0) return;
        var i = List.SelectedIndex;
        do { i += dir; } while (i >= 0 && i < items.Count && items[i].IsHeader);
        if (i < 0 || i >= items.Count) return;
        List.SelectedIndex = i;
        List.ScrollIntoView(List.SelectedItem);
    }

    void FilterBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: Move(1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
            case Key.Enter: Commit(); e.Handled = true; break;
            case Key.Escape: Pop.IsOpen = false; Opener.Focus(); e.Handled = true; break;
        }
    }

    void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(List, (DependencyObject)e.OriginalSource) as ListBoxItem;
        if (item?.DataContext is PickerEntry { IsHeader: false } entry)
        {
            List.SelectedItem = entry;
            Commit();
        }
    }

    void Commit()
    {
        if (List.SelectedItem is not PickerEntry { IsHeader: false } entry) return;
        SelectedId = entry.Id;
        Pop.IsOpen = false;
        Opener.Focus();
        Picked?.Invoke(this, EventArgs.Empty);
    }
}
