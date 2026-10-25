using Avalonia.Controls;
using Avalonia.Input;

namespace CakeOS.Cui.Runtime;

[Flags]
public enum CuiInputSubmitModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Meta = 8
}

/// <summary>The exact text input and accepted Enter key gesture that initiated a CUI submit
/// action. This object is supplied as the action's <c>{Binding Event}</c> parameter.</summary>
public sealed record CuiInputSubmitRequest(TextBox Sender, Key Key, CuiInputSubmitModifiers Modifiers)
{
    public bool Shift => Modifiers.HasFlag(CuiInputSubmitModifiers.Shift);
    public bool Control => Modifiers.HasFlag(CuiInputSubmitModifiers.Control);
    public bool Alt => Modifiers.HasFlag(CuiInputSubmitModifiers.Alt);
    public bool Meta => Modifiers.HasFlag(CuiInputSubmitModifiers.Meta);
}
