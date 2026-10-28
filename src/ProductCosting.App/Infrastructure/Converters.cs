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
        return b ^ Invert ? Visibility.Visible : Visibility.Collapsed;
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
