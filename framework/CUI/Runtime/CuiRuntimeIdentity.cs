using Avalonia;
using Avalonia.Controls;

namespace CakeOS.Cui.Runtime;

/// <summary>Runtime identity metadata used when a CUI tree is reconciled atomically.</summary>
public static class CuiRuntimeIdentity
{
    public static readonly AttachedProperty<string?> StableIdProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("StableId", typeof(CuiRuntimeIdentity));

    public static string? GetStableId(Control control) => control.GetValue(StableIdProperty);

    public static void SetStableId(Control control, string? value) => control.SetValue(StableIdProperty, value);
}
