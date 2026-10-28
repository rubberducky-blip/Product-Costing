using ProductCosting.Core.Model;

namespace ProductCosting.App.Infrastructure;

public static class Fmt
{
    public static string Symbol => AppState.Settings.CurrencySymbol;

    public static string Money(decimal v) => v.ToString("N2", Loc.Culture) + " " + Symbol;
    public static string Money4(decimal v) => (Math.Abs(v) < 1 && v != 0 ? v.ToString("N4", Loc.Culture) : v.ToString("N2", Loc.Culture)) + " " + Symbol;
    public static string Pct(decimal v) => v.ToString("0.##", Loc.Culture) + "%";
    public static string Num(decimal v) => v.ToString("0.#####", Loc.Culture);
    public static string Num(decimal? v) => v is null ? "" : Num(v.Value);
    public static string Usd(decimal v) => v.ToString("N2", Loc.Culture) + " $";
    public static string InCurrency(decimal v, Currency c) => c == Currency.Usd ? Usd(v) : Money(v);
    public static string Signed(decimal v) => (v > 0 ? "+" : "") + v.ToString("N1", Loc.Culture) + "%";
}
