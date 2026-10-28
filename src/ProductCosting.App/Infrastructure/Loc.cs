using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using ProductCosting.Core.Localization;

namespace ProductCosting.App.Infrastructure;

/// <summary>Bindable texts: <c>{l:T nav.products}</c> updates when the language changes.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Strings.Get(key);

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("el-GR");

    public static string Lang => Strings.Language;

    public void SetLanguage(string lang)
    {
        Strings.Language = lang == "en" ? "en" : "el";
        Culture = CultureInfo.GetCultureInfo(Strings.Language == "en" ? "en-IE" : "el-GR");
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        Thread.CurrentThread.CurrentCulture = Culture;
        Thread.CurrentThread.CurrentUICulture = Culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }

    public static string T(string key) => Strings.Get(key);
    public static string F(string key, params object?[] args) => string.Format(Culture, Strings.Get(key), args);
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
