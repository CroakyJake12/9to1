using CakeOS.Cui.Language;

namespace CakeOS.Cui.Runtime;

/// <summary>A binding value observed by the loader during its last application, never re-evaluated by inspection.</summary>
public sealed record CuiObservedBinding(
    string Property,
    CuiBindingValue Expression,
    string? ResolvedValue,
    string? SourceType,
    bool UsedFallback,
    bool SourceFound,
    string? Error);

/// <summary>Read-only authored and observed metadata attached to a CUI-created native control.</summary>
public sealed record CuiControlDiagnostics(
    CuiSourceSpan Source,
    string ComponentType,
    string? AuthoredId,
    IReadOnlyDictionary<string, CuiValue> Properties,
    IReadOnlyDictionary<string, CuiActionReference> Actions,
    IReadOnlyList<CuiObservedBinding> Bindings,
    bool DispatcherConnected,
    bool ActionsWired,
    bool? ActionAvailable,
    bool? ActionRegistered);
