using CakeOS.Cui.Language;

namespace CakeOS.Cui;

public abstract record CuiValue(CuiSourceSpan Span);

public abstract record CuiTextPart(CuiSourceSpan Span);

public sealed record CuiLiteralTextPart(string Value, CuiSourceSpan Span) : CuiTextPart(Span);

public sealed record CuiExpressionTextPart(CuiValue Value, CuiSourceSpan Span) : CuiTextPart(Span);

public sealed record CuiLiteralValue(string Value, CuiSourceSpan Span) : CuiValue(Span);

public sealed record CuiInvalidValue(
    string RawValue,
    string DiagnosticCode,
    string Message,
    CuiSourceSpan Span) : CuiValue(Span);

public enum CuiBindingMode
{
    Invalid = -1,
    OneWay = 0,
    TwoWay = 1,
    OneTime = 2
}

public sealed record CuiBindingValue(
    string Path,
    CuiBindingMode Mode,
    string? Fallback,
    CuiSourceSpan Span,
    string? TargetType = null) : CuiValue(Span);

public sealed record CuiResourceValue(string Key, CuiSourceSpan Span) : CuiValue(Span);

public sealed record CuiActionReference(string Name, CuiSourceSpan Span);

public sealed record CuiCondition(
    CuiValue Test,
    bool Negate,
    CuiSourceSpan Span,
    bool IsLive = true);

public sealed record CuiRepeatDefinition(
    CuiValue Source,
    string ItemName,
    CuiValue Key,
    CuiSourceSpan Span);

public sealed record CuiPropertyRegionDefinition(
    string PropertyName,
    CuiValue Value,
    CuiSourceSpan Span);

public sealed record CuiListDefinition(
    CuiValue Items,
    string ItemTemplate,
    string? EmptyTemplate,
    CuiSourceSpan Span);
