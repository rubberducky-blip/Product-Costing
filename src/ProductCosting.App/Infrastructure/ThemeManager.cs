using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ProductCosting.App.Infrastructure;

/// <summary>
/// Colour themes. A theme is a Fluent base (light / dark controls) plus our palette of named brushes;
/// every brush in the app is a DynamicResource, so switching applies at once without a restart.
/// </summary>
public static class ThemeManager
{
    public sealed record Palette(
        bool Dark,
        string Window, string Sidebar, string Card, string CardHover, string Inset, string Popup, string Line, string LineStrong,
        string Text, string TextMuted, string TextFaint, string OnAccent,
        string Accent, string AccentHover, string AccentPressed, string AccentSoft, string AccentText,
        string Danger, string DangerText, string DangerSoft, string DangerLine,
        string Success, string SuccessText, string SuccessSoft,
        string Warning, string WarningText, string WarningSoft,
        string Hover, string Pressed, string Selected);

    static readonly Palette Dark = new(true,
        "#0E1014", "#14171C", "#181C22", "#1F242C", "#121519", "#1B1F26", "#262B33", "#363C47",
        "#E8EBF0", "#9BA3B0", "#69717E", "#FFFFFF",
        "#3B82F6", "#5B97F8", "#2563EB", "#1A2B47", "#82B1FF",
        "#EF4444", "#F87171", "#3A1A1D", "#5A2A2E",
        "#22C55E", "#4ADE80", "#15301F",
        "#F59E0B", "#FBBF24", "#36290F",
        "#14FFFFFF", "#0AFFFFFF", "#223B82F6");

    static readonly Palette Light = new(false,
        "#F3F5F8", "#FFFFFF", "#FFFFFF", "#F0F2F5", "#F7F8FA", "#FFFFFF", "#E2E6EC", "#CBD2DB",
        "#1B2230", "#556070", "#8A93A2", "#FFFFFF",
        "#2563EB", "#3B82F6", "#1D4ED8", "#E3ECFD", "#1D4ED8",
        "#DC2626", "#C81E1E", "#FDE8E8", "#F3B4B4",
        "#16A34A", "#15803D", "#E2F5E9",
        "#D97706", "#B45309", "#FEF1DD",
        "#0F000000", "#08000000", "#1F2563EB");

    static readonly Palette Contrast = new(true,
        "#000000", "#000000", "#0A0A0A", "#1C1C1C", "#000000", "#0A0A0A", "#8A8A8A", "#FFFFFF",
        "#FFFFFF", "#E6E6E6", "#C8C8C8", "#000000",
        "#FFD400", "#FFE14D", "#E6BF00", "#3A3000", "#FFE14D",
        "#FF4D4D", "#FF7070", "#3A0000", "#FF4D4D",
        "#3CFF6B", "#6BFF8E", "#003A10",
        "#FFB000", "#FFC640", "#3A2800",
        "#33FFFFFF", "#22FFFFFF", "#66FFD400");

    // the colours of the sample image: slate panels with a bright teal accent (dark text on accent fills)
    static readonly Palette SlateTeal = new(true,
        "#111418", "#1A1F26", "#222833", "#2A313D", "#161A20", "#232A35", "#2E3642", "#3B4452",
        "#E6EAF0", "#9AA4B2", "#6B7584", "#111418",
        "#4FD1C5", "#6EDBD1", "#3BB8AD", "#173634", "#7FE3D9",
        "#EF4444", "#F87171", "#3A1A1D", "#5A2A2E",
        "#22C55E", "#4ADE80", "#15301F",
        "#F59E0B", "#FBBF24", "#36290F",
        "#14FFFFFF", "#0AFFFFFF", "#334FD1C5");

    static readonly Palette Nord = new(true,
        "#2E3440", "#2A2F3A", "#3B4252", "#434C5E", "#313744", "#3B4252", "#434C5E", "#4C566A",
        "#ECEFF4", "#C3CAD6", "#8C96A8", "#FFFFFF",
        "#5E81AC", "#81A1C1", "#4C6A91", "#34405A", "#88C0D0",
        "#BF616A", "#D7848C", "#4A3439", "#6A4048",
        "#A3BE8C", "#B6CEA2", "#3A4636",
        "#EBCB8B", "#EBCB8B", "#4A4434",
        "#14FFFFFF", "#0AFFFFFF", "#335E81AC");

    static readonly Palette Dracula = new(true,
        "#21222C", "#1E1F29", "#282A36", "#343746", "#22232D", "#2B2D3A", "#3A3D4E", "#44475A",
        "#F8F8F2", "#C5C8D6", "#6272A4", "#1E1F29",
        "#BD93F9", "#CBA6FA", "#A77BF0", "#3B3354", "#D6BCFC",
        "#FF5555", "#FF7B7B", "#4A2A33", "#6A3540",
        "#50FA7B", "#7BFB9C", "#24402F",
        "#FFB86C", "#FFC98F", "#4A3A2A",
        "#14FFFFFF", "#0AFFFFFF", "#33BD93F9");

    static readonly Palette SolarizedLight = new(false,
        "#EEE8D5", "#FDF6E3", "#FDF6E3", "#F5EFDC", "#F7F1DE", "#FDF6E3", "#E4DCC6", "#D3CBB4",
        "#073642", "#586E75", "#93A1A1", "#FFFFFF",
        "#268BD2", "#3A9BE0", "#1F75B3", "#DCE8F0", "#1F6FA8",
        "#DC322F", "#C02B28", "#F6DDD6", "#E8B4AE",
        "#859900", "#6C7D00", "#E9EBCF",
        "#B58900", "#9A7400", "#F3E7C4",
        "#0F073642", "#08073642", "#22268BD2");

    // eye-friendly themes: low contrast, warm or muted colours, no pure black or white
    static readonly Palette Gruvbox = new(true,
        "#1D2021", "#232526", "#282828", "#32302F", "#202222", "#2C2A29", "#3C3836", "#504945",
        "#EBDBB2", "#BDAE93", "#928374", "#1D2021",
        "#D79921", "#E5AC3A", "#B57F17", "#3A3020", "#FABD2F",
        "#CC241D", "#FB4934", "#3C2220", "#5A2C28",
        "#98971A", "#B8BB26", "#2F3220",
        "#D65D0E", "#FE8019", "#3D2A1C",
        "#14EBDBB2", "#0AEBDBB2", "#33D79921");

    static readonly Palette Everforest = new(true,
        "#232A2E", "#272F33", "#2D353B", "#343F44", "#283035", "#313A3F", "#3D484D", "#4F585E",
        "#D3C6AA", "#A6A08C", "#7A8478", "#232A2E",
        "#A7C080", "#B6CC93", "#93AC6C", "#34402F", "#B9CF95",
        "#E67E80", "#EC9496", "#45303A", "#5C3B42",
        "#83C092", "#9BD0A8", "#2F3F36",
        "#DBBC7F", "#E4CB97", "#45403A",
        "#14D3C6AA", "#0AD3C6AA", "#33A7C080");

    static readonly Palette Sepia = new(false,
        "#EFE6D3", "#F7F0E1", "#FAF5EA", "#F2EADA", "#F5EEDF", "#FAF5EA", "#E2D7C0", "#CDBEA2",
        "#3B3226", "#6B5F4F", "#998B76", "#FFFFFF",
        "#8B5E34", "#9E6E40", "#744C28", "#EBDDC8", "#744C28",
        "#B23A2E", "#9A3026", "#F3DCD3", "#E0B3A8",
        "#5E7D2E", "#4D6824", "#E3E8CF",
        "#B07A12", "#93650E", "#F2E3C2",
        "#0F3B3226", "#083B3226", "#228B5E34");

    /// <summary>Theme ids (stored in settings) with their label keys, in menu order.</summary>
    public static IReadOnlyList<(string Id, string LabelKey)> Themes { get; } =
    [
        ("system", "theme.system"), ("dark", "theme.dark"), ("light", "theme.light"), ("contrast", "theme.contrast"),
        ("slate", "theme.slate"), ("nord", "theme.nord"), ("dracula", "theme.dracula"), ("solarized", "theme.solarized"),
        ("gruvbox", "theme.gruvbox"), ("everforest", "theme.everforest"), ("sepia", "theme.sepia"),
    ];

    public static Palette Current { get; private set; } = Dark;
    static string _theme = "system";
    static bool _listening;
    static ResourceDictionary? _palette;
    static ResourceDictionary? _fluentDark, _fluentLight;
    static ResourceDictionary FluentDark => _fluentDark ??= FindFluent("Dark") ?? Fluent("Dark");
    static ResourceDictionary FluentLight => _fluentLight ??= Fluent("Light");

    static ResourceDictionary Fluent(string mode) =>
        new() { Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{mode}.xaml") };

    // the dark dictionary App.xaml loaded at start-up, reused rather than parsed again
    static ResourceDictionary? FindFluent(string mode) =>
        Application.Current.Resources.MergedDictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.EndsWith($"Fluent.{mode}.xaml", StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>Applies a theme to the whole app (all open windows update at once).</summary>
    public static void Apply(string theme)
    {
        _theme = theme;
        var p = Resolve(theme);
        var app = Application.Current;
        var merged = app.Resources.MergedDictionaries;
        // Fluent controls: exactly one Fluent dictionary, first in the list, matching the palette. WPF also swaps the
        // Fluent dictionary itself when it sees one change, so stale light copies are removed explicitly every time.
        var fluent = p.Dark ? FluentDark : FluentLight;
        for (var i = merged.Count - 1; i >= 0; i--)
            if (merged[i].Source?.OriginalString.Contains("PresentationFramework.Fluent", StringComparison.OrdinalIgnoreCase) == true
                && !ReferenceEquals(merged[i], fluent))
                merged.RemoveAt(i);
        if (!merged.Contains(fluent)) merged.Insert(0, fluent);
        // our palette is always the last dictionary
        if (_palette is not null) merged.Remove(_palette);
        else merged.RemoveAt(merged.Count - 1); // the default palette from App.xaml
        _palette = Build(p);
        merged.Add(_palette);
        Current = p;
        foreach (Window w in app.Windows) NativeWindow.UseDarkTitleBar(w);

        if (!_listening)
        {
            _listening = true;
            // "same as Windows": follow light / dark / high-contrast changes while the app runs
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (_theme == "system" && e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility)
                    app.Dispatcher.BeginInvoke(() => Apply("system"));
            };
        }
    }

    static Palette Resolve(string theme) => theme switch
    {
        "dark" => Dark,
        "light" => Light,
        "contrast" => Contrast,
        "slate" => SlateTeal,
        "nord" => Nord,
        "dracula" => Dracula,
        "solarized" => SolarizedLight,
        "gruvbox" => Gruvbox,
        "everforest" => Everforest,
        "sepia" => Sepia,
        _ => SystemParameters.HighContrast ? Contrast : SystemUsesLight() ? Light : Dark,
    };

    static bool SystemUsesLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch (Exception) { return false; }
    }

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    static ResourceDictionary Build(Palette p)
    {
        var d = new ResourceDictionary();
        void B(string key, string hex)
        {
            var b = new SolidColorBrush(C(hex));
            b.Freeze();
            d[key] = b;
        }
        d["AccentColor"] = C(p.Accent);
        d["DangerColor"] = C(p.Danger);
        B("WindowBrush", p.Window); B("SidebarBrush", p.Sidebar); B("CardBrush", p.Card); B("CardHoverBrush", p.CardHover);
        B("InsetBrush", p.Inset); B("PopupBrush", p.Popup); B("LineBrush", p.Line); B("LineStrongBrush", p.LineStrong);
        B("TextBrush", p.Text); B("TextMutedBrush", p.TextMuted); B("TextFaintBrush", p.TextFaint); B("OnAccentBrush", p.OnAccent);
        B("AccentBrush", p.Accent); B("AccentHoverBrush", p.AccentHover); B("AccentPressedBrush", p.AccentPressed);
        B("AccentSoftBrush", p.AccentSoft); B("AccentTextBrush", p.AccentText);
        B("DangerBrush", p.Danger); B("DangerTextBrush", p.DangerText); B("DangerSoftBrush", p.DangerSoft); B("DangerLineBrush", p.DangerLine);
        B("SuccessBrush", p.Success); B("SuccessTextBrush", p.SuccessText); B("SuccessSoftBrush", p.SuccessSoft);
        B("WarningBrush", p.Warning); B("WarningTextBrush", p.WarningText); B("WarningSoftBrush", p.WarningSoft);
        B("HoverBrush", p.Hover); B("PressedBrush", p.Pressed); B("SelectedBrush", p.Selected);
        return d;
    }
}

/// <summary>Text size: the whole interface is scaled, so spacing grows with the text and layouts stay balanced.</summary>
public static class UiScale
{
    public static System.Windows.Media.ScaleTransform Transform { get; } = new(1, 1);

    public static IReadOnlyList<(decimal Scale, string LabelKey)> Sizes { get; } =
        [(1.0m, "size.normal"), (1.1m, "size.large"), (1.2m, "size.larger")];

    public static void Apply(decimal scale)
    {
        var s = (double)Math.Clamp(scale, 0.8m, 1.5m);
        Transform.ScaleX = s;
        Transform.ScaleY = s;
    }
}
