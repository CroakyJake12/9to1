namespace Haven.CUI.DevTools;

public sealed record CuiAuthoredControlTrace(
    CuiSourceLocation Source, string ComponentType, string? AuthoredId);

public sealed record CuiBindingTrace(
    string Property, string Path, string Mode, string? Fallback,
    string? ResolvedValue, string? NativeValue, string? SourceType,
    BindingStatus Status, bool UsedFallback, string? Error);

/// <summary>Unknown availability is distinct from an explicitly unavailable route.</summary>
public sealed record CuiActionTrace(
    string Attribute, string Command, CuiSourceLocation Source,
    bool DispatcherConnected, bool DispatchWired, bool NativeEnabled,
    bool? HandlerRegistered, bool? HostAvailable);

public sealed record CuiVisualPropertyTrace(
    string Property, string EffectiveValue, string? AuthoredExpression, string? AuthoredAt);

/// <summary>Observed geometry and visibility; frame timings and draw counts are not inferred.</summary>
public sealed record CuiRenderingTrace(
    CuiRect Bounds, double Opacity, bool IsVisible, bool IsLoaded,
    double RenderScaling, string? Transform, string? Clip);
