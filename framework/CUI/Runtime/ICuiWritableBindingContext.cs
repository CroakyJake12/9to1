using CakeOS.Cui;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Optional explicit write-back contract for host-owned CUI binding contexts.
/// Runtime input never reflects into arbitrary properties or methods.
/// </summary>
public interface ICuiWritableBindingContext : ICuiBindingContext
{
    bool TrySetValue(string path, object? value);
}
