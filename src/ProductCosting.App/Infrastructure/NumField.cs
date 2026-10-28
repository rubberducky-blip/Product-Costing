using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ProductCosting.App.Infrastructure;

/// <summary>
/// A number typed by the user. Keeps the raw text (so typing "1," doesn't jump) and the parsed value.
/// Accepts both "," and "." as decimal separator.
/// </summary>
public sealed partial class NumField : ObservableObject
{
    readonly bool _allowEmpty;
    readonly decimal _min;

    public NumField(decimal? value = null, bool allowEmpty = false, decimal min = 0)
    {
        _allowEmpty = allowEmpty;
        _min = min;
        _text = Fmt.Num(value);
        Parse();
    }

    public event Action? Changed;

    [ObservableProperty]
    private string _text;

    public decimal? Value { get; private set; }
    public bool IsValid { get; private set; }

    /// <summary>The value, or 0 when empty/invalid.</summary>
    public decimal Or0 => Value ?? 0;

    partial void OnTextChanged(string value)
    {
        Parse();
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsValid));
        Changed?.Invoke();
    }

    void Parse()
    {
        var s = (Text ?? "").Trim().Replace(" ", "").Replace("€", "").Replace("%", "").Replace("$", "");
        if (s.Length == 0)
        {
            Value = null;
            IsValid = _allowEmpty;
            return;
        }
        s = s.Replace(',', '.');
        if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= _min)
        {
            Value = d;
            IsValid = true;
        }
        else
        {
            Value = null;
            IsValid = false;
        }
    }

    public void Set(decimal? value) => Text = Fmt.Num(value);
}
