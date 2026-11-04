using System.Windows;
using System.Windows.Controls;
using ProductCosting.App.Infrastructure;
using ProductCosting.App.ViewModels;
using ProductCosting.Core.Import;

namespace ProductCosting.App.Views;

/// <summary>Import: the user matches each field (code, cost, price…) to a column of the file; recognised titles come pre-matched.</summary>
public partial class ImportColumnsWindow : Window
{
    readonly List<string[]> _table;
    readonly Dictionary<ImportField, ComboBox> _boxes = [];
    readonly int _columns;

    public Dictionary<ImportField, int> Map => _boxes.ToDictionary(b => b.Key, b => b.Value.SelectedValue is int i ? i : -1);
    public bool HasHeader => HeaderBox.IsChecked == true;

    public ImportColumnsWindow(List<string[]> table)
    {
        InitializeComponent();
        NativeWindow.UseDarkTitleBar(this);
        Title = Loc.T("import.title");
        _table = table;
        _columns = table.Max(r => r.Length);
        var guess = ItemImport.Guess(table[0]);
        HeaderBox.IsChecked = guess is not null;
        var map = guess ?? ItemImport.DefaultOrder();

        var row = 0;
        foreach (var field in Enum.GetValues<ImportField>())
        {
            Fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = Loc.T("import.field." + field), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) };
            Grid.SetRow(label, row);
            var box = new ComboBox
            {
                SelectedValuePath = "Value", DisplayMemberPath = "Label", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 4, 0, 4),
            };
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            box.SelectionChanged += (_, _) => Validate();
            Fields.Children.Add(label);
            Fields.Children.Add(box);
            _boxes[field] = box;
            row++;
        }
        FillChoices();
        foreach (var (field, box) in _boxes) box.SelectedValue = map.TryGetValue(field, out var i) && i < _columns ? i : -1;
        Validate();
    }

    /// <summary>"B · Τιμή πώλησης" with titles, or "B · 12,50" (the first value) without.</summary>
    void FillChoices()
    {
        var first = _table[0];
        var choices = new List<Choice<int>> { new(-1, Loc.T("import.columns.none")) };
        for (var i = 0; i < _columns; i++)
        {
            var sample = i < first.Length ? first[i] : "";
            if (!HasHeader && sample.Length == 0) sample = _table.Skip(1).Select(r => i < r.Length ? r[i] : "").FirstOrDefault(v => v.Length > 0) ?? "";
            choices.Add(new(i, sample.Length > 0 ? $"{Letter(i)} · {sample}" : Letter(i)));
        }
        foreach (var box in _boxes.Values)
        {
            var keep = box.SelectedValue;
            box.ItemsSource = choices;
            box.SelectedValue = keep ?? -1;
        }
    }

    static string Letter(int i) => i < 26 ? ((char)('A' + i)).ToString() : $"{(char)('A' + i / 26 - 1)}{(char)('A' + i % 26)}";

    void HeaderBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_boxes.Count > 0) FillChoices();
    }

    void Validate()
    {
        var ok = ItemImport.Required.All(f => _boxes.TryGetValue(f, out var b) && b.SelectedValue is int i && i >= 0);
        OkButton.IsEnabled = ok;
        Missing.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
