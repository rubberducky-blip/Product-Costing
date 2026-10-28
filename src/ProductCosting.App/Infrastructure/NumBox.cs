using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ProductCosting.App.Infrastructure;

/// <summary>TextBox for a <see cref="NumField"/>: right-aligned, red border while the number is invalid.</summary>
public sealed class NumBox : TextBox
{
    public static readonly DependencyProperty FieldProperty = DependencyProperty.Register(
        nameof(Field), typeof(NumField), typeof(NumBox), new PropertyMetadata(null, OnFieldChanged));

    public NumField? Field
    {
        get => (NumField?)GetValue(FieldProperty);
        set => SetValue(FieldProperty, value);
    }

    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
        nameof(Decimals), typeof(int), typeof(NumBox), new PropertyMetadata(-1, (d, _) => ((NumBox)d).Pretty()));

    /// <summary>Minimum decimals shown while the box isn't being edited (2 for prices); -1 shows the number as typed.</summary>
    public int Decimals
    {
        get => (int)GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    public NumBox()
    {
        HorizontalContentAlignment = HorizontalAlignment.Right;
        GotKeyboardFocus += (_, _) => SelectAll();
        LostKeyboardFocus += (_, _) => Pretty();
    }

    /// <summary>Shows e.g. "12,50" instead of "12,5" when not editing. The value doesn't change, so nothing becomes "unsaved".</summary>
    void Pretty()
    {
        if (Decimals < 0 || IsKeyboardFocusWithin || Field is not { IsValid: true, Value: decimal v }) return;
        var text = v.ToString("0." + new string('0', Decimals) + "#####", Loc.Culture);
        if (Field.Text != text) Field.Text = text;
    }

    static void OnFieldChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (NumBox)d;
        if (e.OldValue is NumField old) old.PropertyChanged -= box.OnFieldPropertyChanged;
        if (e.NewValue is NumField f)
        {
            box.SetBinding(TextProperty, new Binding(nameof(NumField.Text)) { Source = f, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            f.PropertyChanged += box.OnFieldPropertyChanged;
        }
        else BindingOperations.ClearBinding(box, TextProperty);
        box.UpdateValidity();
        box.Pretty();
    }

    void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NumField.IsValid)) UpdateValidity();
        else if (e.PropertyName == nameof(NumField.Text)) Pretty();
    }

    void UpdateValidity()
    {
        if (Field is { IsValid: false })
        {
            BorderBrush = (Brush)FindResource("DangerBrush");
            ToolTip = Loc.T("common.invalidNumber");
        }
        else
        {
            ClearValue(BorderBrushProperty);
            ClearValue(ToolTipProperty);
        }
    }
}
