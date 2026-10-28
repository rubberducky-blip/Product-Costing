using CommunityToolkit.Mvvm.ComponentModel;
using ProductCosting.App.Infrastructure;
using ProductCosting.Core.Localization;
using ProductCosting.Core.Model;

namespace ProductCosting.App.ViewModels;

public sealed record Choice<T>(T Value, string Label);

public static class Choices
{
    public static List<Choice<QtyUnit>> Units() =>
        Enum.GetValues<QtyUnit>().Select(u => new Choice<QtyUnit>(u, Strings.Unit(u))).ToList();

    public static List<Choice<Currency>> Currencies() =>
    [
        new(Currency.Eur, Loc.T("currency.eur")),
        new(Currency.Usd, Loc.T("currency.usd")),
    ];

    public static List<Choice<RoundingMode>> RoundingModes() =>
        Enum.GetValues<RoundingMode>().Select(m => new Choice<RoundingMode>(m, Loc.T("settings.round." + m))).ToList();
}

/// <summary>A line of the live cost breakdown.</summary>
public sealed record BreakdownLine(string Label, string Value, string Kind = "row")
{
    public bool IsHeader => Kind == "header";
    public bool IsTotal => Kind == "total";
    public bool IsBig => Kind == "big";
    public bool IsSub => Kind == "sub";
    public bool IsWarn => Kind == "warn";
    public bool IsSep => Kind == "sep";
}

/// <summary>Base for editable child rows: reports any change to its editor.</summary>
public abstract class EditRow : ObservableObject
{
    public event Action? Changed;

    /// <summary>While true, property changes are display refreshes, not edits.</summary>
    protected bool Quiet { get; set; }

    protected void Touch()
    {
        if (!Quiet) Changed?.Invoke();
    }

    protected NumField Num(decimal? v, bool allowEmpty = false, decimal min = 0)
    {
        var f = new NumField(v, allowEmpty, min);
        f.Changed += Touch;
        return f;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // derived display values don't count as edits
        if (e.PropertyName is { } n && !n.EndsWith("Text") && !n.EndsWith("Hint") && !n.EndsWith("Choices") && !n.StartsWith("Is"))
            Touch();
    }
}
