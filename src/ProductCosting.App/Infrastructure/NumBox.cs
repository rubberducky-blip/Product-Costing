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

    public NumBox()
    {
        HorizontalContentAlignment = HorizontalAlignment.Right;
        GotKeyboardFocus += (_, _) => SelectAll();
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
    }

    void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NumField.IsValid)) UpdateValidity();
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
