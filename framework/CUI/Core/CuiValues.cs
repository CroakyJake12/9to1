using CakeOS.Cui.Language;

namespace CakeOS.Cui;

public abstract record CuiValue(CuiSourceSpan Span);

public sealed record CuiLiteralValue(string Value, CuiSourceSpan Span) : CuiValue(Span);

public enum CuiBindingMode
{
    OneWay,
    TwoWay,
    OneTime
}

public sealed record CuiBindingValue(
    string Path,
    CuiBindingMode Mode,
    string? Fallback,
    CuiSourceSpan Span) : CuiValue(Span);

public sealed record CuiResourceValue(string Key, CuiSourceSpan Span) : CuiValue(Span);

public sealed record CuiActionReference(string Name, CuiSourceSpan Span);

public sealed record CuiCondition(CuiValue Test, bool Negate, CuiSourceSpan Span);

public sealed record CuiListDefinition(
    CuiValue Items,
    string ItemTemplate,
    string? EmptyTemplate,
    CuiSourceSpan Span);
