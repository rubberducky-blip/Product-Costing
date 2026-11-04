using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProductCosting.App.Infrastructure;

/// <summary>
/// The app's keyboard shortcuts, changeable in Settings. Stored as "id=Ctrl+D;…"; anything not stored keeps its default.
/// </summary>
public static class Hotkeys
{
    public sealed record Gesture(ModifierKeys Modifiers, Key Key)
    {
        public override string ToString()
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            parts.Add(KeyName(Key));
            return string.Join(" + ", parts);
        }
    }

    /// <summary>Changeable shortcuts in the order Settings lists them, with their defaults and descriptions.</summary>
    public static IReadOnlyList<(string Id, Gesture Default, string LabelKey)> All { get; } =
    [
        ("search", new(ModifierKeys.Control, Key.F), "keys.search"),
        ("save", new(ModifierKeys.Control, Key.S), "keys.save"),
        ("undo", new(ModifierKeys.Control, Key.Z), "keys.undo"),
        ("newProduct", new(ModifierKeys.Control, Key.D1), "list.newProduct"),
        ("newMerchandise", new(ModifierKeys.Control, Key.D2), "list.newMerchandise"),
        ("newRaw", new(ModifierKeys.Control, Key.D3), "list.newRaw"),
        ("newSupplier", new(ModifierKeys.Control, Key.D4), "list.newSupplier"),
        ("newOrder", new(ModifierKeys.Control, Key.D5), "supplier.newOrder"),
        ("backup", new(ModifierKeys.Control, Key.B), "keys.backup"),
        ("duplicate", new(ModifierKeys.Control, Key.D), "keys.duplicate"),
        ("history", new(ModifierKeys.Control, Key.H), "keys.history"),
        ("purchase", new(ModifierKeys.Control, Key.Q), "keys.purchase"),
        ("price", new(ModifierKeys.Control, Key.W), "keys.price"),
    ];

    static Dictionary<string, Gesture?> _current = All.ToDictionary(a => a.Id, a => (Gesture?)a.Default);

    /// <summary>Takes the stored setting ("" = all defaults).</summary>
    public static void Load(string? stored) => _current = Parse(stored);

    public static Dictionary<string, Gesture?> Parse(string? stored)
    {
        var map = All.ToDictionary(a => a.Id, a => (Gesture?)a.Default);
        foreach (var part in (stored ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var id = part[..eq].Trim();
            if (map.ContainsKey(id)) map[id] = FromText(part[(eq + 1)..]);
        }
        return map;
    }

    /// <summary>Only the shortcuts that differ from their defaults are stored.</summary>
    public static string Serialize(IReadOnlyDictionary<string, Gesture?> map) => string.Join(";",
        All.Where(a => map.TryGetValue(a.Id, out var g) && g != a.Default).Select(a => $"{a.Id}={map[a.Id]?.ToString() ?? ""}"));

    public static Gesture? Get(string id) => _current.GetValueOrDefault(id);

    /// <summary>"Ctrl + D", or "" when the shortcut is switched off.</summary>
    public static string Text(string id) => Get(id)?.ToString() ?? "";

    public static bool Matches(string id, KeyEventArgs e) => Get(id) is { } g && g == FromEvent(e);

    /// <summary>The shortcut a key press makes (keypad digits count as the digits above the letters), or null for a bare modifier.</summary>
    public static Gesture? FromEvent(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is >= Key.NumPad0 and <= Key.NumPad9) key = Key.D0 + (key - Key.NumPad0);
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin
            or Key.None or Key.ImeProcessed or Key.DeadCharProcessed) return null;
        return new Gesture(Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt), key);
    }

    public static Gesture? FromText(string text)
    {
        var mods = ModifierKeys.None;
        Key? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = raw.Trim();
            switch (p.ToLowerInvariant())
            {
                case "ctrl": mods |= ModifierKeys.Control; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                default:
                    if (p.Length == 1 && char.IsDigit(p[0])) key = Key.D0 + (p[0] - '0');
                    else if (Enum.TryParse<Key>(p, true, out var k)) key = k;
                    break;
            }
        }
        return key is { } kk ? new Gesture(mods, kk) : null;
    }

    static string KeyName(Key k) => k switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (k - Key.D0))).ToString(),
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        _ => k.ToString(),
    };

    /// <summary>A usable shortcut: with Ctrl or Alt, or a function key (plain letters are for typing).</summary>
    public static bool IsAllowed(Gesture g) =>
        g.Modifiers.HasFlag(ModifierKeys.Control) || g.Modifiers.HasFlag(ModifierKeys.Alt) || g.Key is >= Key.F1 and <= Key.F24;

    // ---------- menu items show their shortcut: l:Hotkeys.Id="duplicate" ----------

    public static readonly DependencyProperty IdProperty = DependencyProperty.RegisterAttached(
        "Id", typeof(string), typeof(Hotkeys), new PropertyMetadata(null, (d, e) =>
        {
            if (d is MenuItem mi && e.NewValue is string id) mi.InputGestureText = Text(id);
        }));

    public static string? GetId(DependencyObject d) => (string?)d.GetValue(IdProperty);
    public static void SetId(DependencyObject d, string? value) => d.SetValue(IdProperty, value);
}

/// <summary>Records a shortcut: press the keys while the box has focus; Backspace or Delete clears it.</summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty GestureTextProperty = DependencyProperty.Register(
        nameof(GestureText), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((HotkeyBox)d).Text = (string)e.NewValue));

    public string GestureText
    {
        get => (string)GetValue(GestureTextProperty);
        set => SetValue(GestureTextProperty, value);
    }

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Tab still moves on
        if (key == Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift) return;
        e.Handled = true;
        if (Keyboard.Modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
        {
            GestureText = "";
            return;
        }
        if (Hotkeys.FromEvent(e) is { } g && Hotkeys.IsAllowed(g)) GestureText = g.ToString();
    }
}
