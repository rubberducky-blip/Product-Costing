using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ProductCosting.Core.Localization;
using ProductCosting.Core.Model;

namespace ProductCosting.App.Infrastructure;

public sealed class MoneyConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is decimal d ? Fmt.Money(d) : "";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class Money4Converter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is decimal d ? Fmt.Money4(d) : "";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class PercentConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        decimal d => d.ToString(p as string ?? "0.#", Loc.Culture) + "%",
        _ => "",
    };
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class NumberConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        decimal d => Fmt.Num(d),
        int i => i.ToString(Loc.Culture),
        _ => "",
    };
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class Decimals2Converter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is decimal d ? d.ToString("0.00###", Loc.Culture) : "";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>An item row's purchase price in its own currency, e.g. "3,10 $".</summary>
public sealed class PurchasePriceConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is ItemRow { PurchasePrice: decimal price } r ? Fmt.InCurrency(price, r.PurchaseCurrency) : "";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>A supplier item's purchase price with its currency symbol.</summary>
public sealed class SupplierPriceConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is SupplierItemRow r ? Fmt.InCurrency(r.PurchasePrice, r.Currency) : "";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class UnitConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is QtyUnit u ? Strings.Unit(u) : "";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class KindConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is ItemKind k ? Strings.Kind(k) : "";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class CurrencyConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is Currency cur ? (cur == Currency.Usd ? "USD $" : "EUR €") : "";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class ReasonsConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is PriceReason r ? Strings.Reasons(r) : "";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    /// <summary>Hide with Visibility.Hidden, so the element keeps its place in the layout.</summary>
    public bool KeepSpace { get; set; }
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var b = value switch
        {
            bool x => x,
            int i => i != 0,
            string s => !string.IsNullOrEmpty(s),
            null => false,
            _ => true,
        };
        return b ^ Invert ? Visibility.Visible : KeepSpace ? Visibility.Hidden : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => Equals(value?.ToString(), p?.ToString());
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        value is true && p is not null ? Enum.Parse(t, p.ToString()!) : Binding.DoNothing;
}

public sealed class SizeConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is long b
        ? b >= 1024 * 1024 ? (b / 1024d / 1024d).ToString("0.0", Loc.Culture) + " MB" : (b / 1024d).ToString("0", Loc.Culture) + " KB"
        : "";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class DateTimeConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        DateTime d => d.ToString(p as string ?? "g", Loc.Culture),
        string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) => d.ToString(p as string ?? "g", Loc.Culture),
        _ => "",
    };
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Colour of a margin %: low is bad (red), middling amber, high good (green).</summary>
public sealed class MarginBrushConverter : IValueConverter
{
    public const decimal Low = 20m, Good = 35m;

    public static string Tone(decimal margin) => margin < Low ? "bad" : margin < Good ? "ok" : "good";

    /// <summary>Parameter "soft" gives the matching background colour.</summary>
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is decimal m
        ? Application.Current.FindResource((Tone(m), p as string) switch
        {
            ("bad", "soft") => "DangerSoftBrush",
            ("ok", "soft") => "WarningSoftBrush",
            (_, "soft") => "SuccessSoftBrush",
            ("bad", _) => "DangerTextBrush",
            ("ok", _) => "WarningTextBrush",
            _ => "SuccessTextBrush",
        })
        : DependencyProperty.UnsetValue;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}
