using System.Windows.Media;
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
        value is ItemRow { PurchasePrice: decimal price } r ? Fmt.Exact(price, r.PurchaseCurrency) : "";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>A supplier item's purchase price with its currency symbol.</summary>
public sealed class SupplierPriceConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is SupplierItemRow r ? Fmt.Exact(r.PurchasePrice, r.Currency) + Per(r) : "";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;

    // "88,00 € / 1,5 μ²": what the price buys, unless it is one piece
    static string Per(SupplierItemRow r) => r.EntryUnit == MeasureUnit.Pcs && r.EntryQty == 1 ? ""
        : $" / {(r.EntryQty == 1 ? "" : Fmt.Num(r.EntryQty) + " ")}{Strings.Measure(r.EntryUnit)}";
}

/// <summary>A supplier item's minimum order (parameter "moq") or pack size ("pack") in the unit it is bought in.</summary>
public sealed class SupplierQtyConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is SupplierItemRow r && ((p as string) == "pack" ? r.EntryPack : r.EntryMoq) is decimal q
            ? $"{Fmt.Num(q)} {Strings.Measure(r.EntryUnit)}" : "";
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

/// <summary>One of two sizes depending on a flag (e.g. a wider field when it carries a unit picker).</summary>
public sealed class BoolToDoubleConverter : IValueConverter
{
    public double True { get; set; }
    public double False { get; set; }
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is true ? True : False;
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
/// <summary>
/// Colour of a net margin (adjusted margin of bought items, margin of products, whose cost already carries labour and
/// overhead): below 0% dark red, 0–10% red, 10–15% orange, 15–25% yellow, 25–35% lime, 35% and above deep green.
/// Parameter "soft" gives the matching background.
/// </summary>
public sealed class AdjMarginBrushConverter : IValueConverter
{
    static readonly string[] DarkColors = ["#FF4D4F", "#F87171", "#FB923C", "#FACC15", "#A3E635", "#22C55E"];
    static readonly string[] LightColors = ["#991B1B", "#DC2626", "#EA580C", "#CA8A04", "#65A30D", "#15803D"];
    static readonly Dictionary<(int, bool, bool), Brush> Cache = [];

    public static int Band(decimal m) => m switch { < 0 => 0, < 10 => 1, < 15 => 2, < 25 => 3, < 35 => 4, _ => 5 };

    public static Brush For(decimal margin, bool soft = false)
    {
        var dark = ThemeManager.Current.Dark;
        var key = (Band(margin), dark, soft);
        if (Cache.TryGetValue(key, out var b)) return b;
        var color = (Color)ColorConverter.ConvertFromString((dark ? DarkColors : LightColors)[key.Item1]);
        if (soft) color.A = 0x2E;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return Cache[key] = brush;
    }

    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is decimal m ? For(m, (p as string) == "soft") : DependencyProperty.UnsetValue;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

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
