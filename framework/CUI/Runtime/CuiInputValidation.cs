using Avalonia;
using Avalonia.Controls;

namespace CakeOS.Cui.Runtime;

/// <summary>Runtime validation state for an Input whose source value rejected write-back.</summary>
public sealed class CuiInputValidationProperties : AvaloniaObject
{
    public static readonly AttachedProperty<bool> HasErrorProperty =
        AvaloniaProperty.RegisterAttached<CuiInputValidationProperties, Control, bool>("HasError");

    public static bool GetHasError(Control control) => control.GetValue(HasErrorProperty);

    public static void SetHasError(Control control, bool value)
    {
        control.SetValue(HasErrorProperty, value);
        if (value)
            control.Classes.Add("cui-validation-error");
        else
            control.Classes.Remove("cui-validation-error");
    }
}
