using Avalonia.Controls;
using CakeOS.Cui.Language;

namespace CakeOS.Cui.Runtime;

/// <summary>Host integration for live, in-surface backdrop modifier rendering.</summary>
public interface ICuiBackdropRenderer
{
    /// <summary>Attach a live backdrop effect to a target control or reject the chain with a reason.</summary>
    bool TryApply(Control target, string modifierChain, CuiSourceSpan source, out string? error);
}
