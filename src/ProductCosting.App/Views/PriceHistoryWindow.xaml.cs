using System.Windows;
using System.Windows.Media;
using ProductCosting.App.Infrastructure;
using ProductCosting.Core.Localization;
using ProductCosting.Core.Model;

namespace ProductCosting.App.Views;

public sealed class HistoryRow(PriceHistoryEntry e)
{
    public string DateText => e.Date.ToString("d", Loc.Culture);
    public string PriceText => Fmt.Money(e.Price);
    public string ChangeText => e.OldPrice is decimal o && o != 0
        ? $"{(e.Price > o ? "+" : "−")}{Fmt.Money(Math.Abs(e.Price - o))}  ({Fmt.Signed((e.Price - o) / o * 100)})"
        : "";
    public Brush ChangeBrush => (Brush)Application.Current.FindResource(e.OldPrice is decimal o && e.Price > o ? "DangerTextBrush" : "SuccessTextBrush");
    public string ReasonsText => Strings.Reasons(e.Reasons);
    public string MarginText => HistoryText.Margin(e);
    /// <summary>Green when the margin grew, red when it shrank.</summary>
    public Brush MarginBrush => (Brush)Application.Current.FindResource(e.MarginTrend switch
    {
        > 0 => "SuccessTextBrush",
        < 0 => "DangerTextBrush",
        _ => "TextMutedBrush",
    });
}

public static class HistoryText
{
    /// <summary>"32,5% → 35,1%", or just the margin when it did not change (empty for history from before margins were kept).</summary>
    public static string Margin(PriceHistoryEntry e) => e.Margin is not decimal m ? ""
        : e.MarginTrend != 0 ? $"{Fmt.Pct(Math.Round(e.OldMargin!.Value, 1))} → {Fmt.Pct(Math.Round(m, 1))}"
        : Fmt.Pct(Math.Round(m, 1));
}

public partial class PriceHistoryWindow : Window
{
    public PriceHistoryWindow(long itemId, string title)
    {
        InitializeComponent();
        NativeWindow.UseDarkTitleBar(this);
        Title = TitleText.Text = Loc.F("history.title", title);
        var entries = AppState.Store.GetHistory(itemId);
        Chart.Entries = entries;
        Table.ItemsSource = entries.AsEnumerable().Reverse().Select(e => new HistoryRow(e)).ToList();
        if (entries.Count == 0) TableCard.Visibility = Visibility.Collapsed;
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
