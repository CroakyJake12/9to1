using CakeOS.Cui.Language;

namespace CakeOS.Cui;

/// <summary>Structured admission failure from the typed V2 transport codec. It is deliberately a
/// Core-local type so the Core transport remains independent of the Runtime assembly; consumers
/// translate it into their runtime load exception boundary as needed.</summary>
public sealed class CuiV2AdmissionException : Exception
{
    public CuiV2AdmissionException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>A stable diagnostic code (for example "CUIR070"), matching the canonical CUI
    /// runtime load diagnostic naming without depending on Runtime types.</summary>
    public string Code { get; }
}

/// <summary>Value-kind discriminators for the allowlisted typed payload. These are the only
/// node kinds the transport accepts; unknown/missing kinds are rejected before any effects.</summary>
public static class CuiV2ValueKind
{
    public const string Literal = "literal";
    public const string Binding = "binding";
    public const string Resource = "resource";
    public const string Invalid = "invalid";
}

public static class CuiV2TextPartKind
{
    public const string Literal = "literal";
    public const string Expression = "expression";
}

public static class CuiV2DependencyKind
{
    public const string Definition = "Definition";
    public const string Template = "Template";
    public const string Resource = "Resource";
    public const string Variable = "Variable";
    public const string Action = "Action";
    public const string Import = "Import";
}

/// <summary>Wire-safe mirror of <see cref="CuiSourceSpan"/>.</summary>
public sealed record CuiV2Span(string SourceName, int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    public static CuiV2Span From(CuiSourceSpan span) =>
        new(span.SourceName, span.Start.Line, span.Start.Column, span.End.Line, span.End.Column);

    public CuiSourceSpan ToCuiSourceSpan() =>
        new(SourceName, new(StartLine, StartColumn), new(EndLine, EndColumn));
}

/// <summary>Allowlisted typed value payload. Exactly one semantic sub-shape is populated
/// according to <see cref="Kind"/>; unknown kinds are rejected on decode.</summary>
public sealed record CuiV2Value(
    string Kind,
    string? LiteralValue = null,
    string? BindingPath = null,
    int? BindingMode = null,
    string? BindingFallback = null,
    string? BindingTargetType = null,
    string? ResourceKey = null,
    string? InvalidRaw = null,
    string? InvalidCode = null,
    string? InvalidMessage = null,
    CuiV2Span? Span = null)
{
    public static CuiV2Value From(CuiValue value) => value switch
    {
        CuiLiteralValue literal => new(CuiV2ValueKind.Literal,
            LiteralValue: literal.Value, Span: CuiV2Span.From(literal.Span)),
        CuiBindingValue binding => new(CuiV2ValueKind.Binding,
            BindingPath: binding.Path,
            BindingMode: (int)binding.Mode,
            BindingFallback: binding.Fallback,
            BindingTargetType: binding.TargetType,
            Span: CuiV2Span.From(binding.Span)),
        CuiResourceValue resource => new(CuiV2ValueKind.Resource,
            ResourceKey: resource.Key, Span: CuiV2Span.From(resource.Span)),
        CuiInvalidValue invalid => new(CuiV2ValueKind.Invalid,
            InvalidRaw: invalid.RawValue,
            InvalidCode: invalid.DiagnosticCode,
            InvalidMessage: invalid.Message,
            Span: CuiV2Span.From(invalid.Span)),
        _ => throw new NotSupportedException($"Unsupported CUI value type '{value.GetType().Name}'.")
    };

    public CuiValue ToCuiValue() => Kind switch
    {
        CuiV2ValueKind.Literal => new CuiLiteralValue(LiteralValue ?? "", Span.RequiredValue("literal value")),
        CuiV2ValueKind.Binding => new CuiBindingValue(
            BindingPath.RequiredValue("binding path"),
            BindingMode is { } mode && Enum.IsDefined(typeof(CuiBindingMode), mode)
                ? (CuiBindingMode)mode
                : throw new CuiV2AdmissionException("CUIR071", $"Unknown CUI binding mode {BindingMode}."),
            BindingFallback,
            Span.RequiredValue("binding span"),
            BindingTargetType),
        CuiV2ValueKind.Resource => new CuiResourceValue(ResourceKey.RequiredValue("resource key"), Span.RequiredValue("resource span")),
        CuiV2ValueKind.Invalid => new CuiInvalidValue(
            InvalidRaw ?? "", InvalidCode ?? "", InvalidMessage ?? "", Span.RequiredValue("invalid span")),
        _ => throw new CuiV2AdmissionException("CUIR071", $"Unsupported CUI value kind '{Kind}'.")
    };
}

/// <summary>Allowlisted typed text part payload (literal text or an expression value).</summary>
public sealed record CuiV2TextPart(
    string Kind,
    string? LiteralValue = null,
    CuiV2Value? ExpressionValue = null,
    CuiV2Span? Span = null)
{
    public static CuiV2TextPart From(CuiTextPart part) => part switch
    {
        CuiLiteralTextPart literal => new(CuiV2TextPartKind.Literal,
            LiteralValue: literal.Value, Span: CuiV2Span.From(literal.Span)),
        CuiExpressionTextPart expression => new(CuiV2TextPartKind.Expression,
            ExpressionValue: CuiV2Value.From(expression.Value), Span: CuiV2Span.From(expression.Span)),
        _ => throw new NotSupportedException($"Unsupported CUI text part type '{part.GetType().Name}'.")
    };

    public CuiTextPart ToCuiTextPart() => Kind switch
    {
        CuiV2TextPartKind.Literal => new CuiLiteralTextPart(LiteralValue ?? "", Span.RequiredValue("literal text span")),
        CuiV2TextPartKind.Expression => new CuiExpressionTextPart(
            ExpressionValue?.ToCuiValue() ?? throw new CuiV2AdmissionException("CUIR072", "Expression text part is missing its value."),
            Span.RequiredValue("expression text span")),
        _ => throw new CuiV2AdmissionException("CUIR071", $"Unsupported CUI text part kind '{Kind}'.")
    };
}

public sealed record CuiV2Resource(string Key, CuiV2Value Value, CuiV2Span Span)
{
    public static CuiV2Resource From(CuiResourceDefinition resource) =>
        new(resource.Key, CuiV2Value.From(resource.Value), CuiV2Span.From(resource.Span));

    public CuiResourceDefinition ToCuiResource() => new(Key, Value.ToCuiValue(), Span.ToCuiSourceSpan());
}

public sealed record CuiV2StyleSetter(string Property, CuiV2Value Value, CuiV2Span Span)
{
    public static CuiV2StyleSetter From(CuiStyleSetter setter) =>
        new(setter.Property, CuiV2Value.From(setter.Value), CuiV2Span.From(setter.Span));

    public CuiStyleSetter ToCuiStyleSetter() => new(Property, Value.ToCuiValue(), Span.ToCuiSourceSpan());
}

public sealed record CuiV2Style(string Selector, IReadOnlyList<CuiV2StyleSetter> Setters, CuiV2Span Span)
{
    public static CuiV2Style From(CuiStyleDefinition style) =>
        new(style.Selector, style.Setters.Select(CuiV2StyleSetter.From).ToArray(), CuiV2Span.From(style.Span));

    public CuiStyleDefinition ToCuiStyle() =>
        new(Selector, Setters.Select(setter => setter.ToCuiStyleSetter()).ToArray(), Span.ToCuiSourceSpan());
}

public sealed record CuiV2Action(
    string Name,
    string Command,
    CuiV2Value? Parameter,
    CuiV2Span Span,
    string? LocalTarget = null)
{
    public static CuiV2Action From(CuiActionDefinition action) =>
        new(action.Name, action.Command,
            action.Parameter is { } parameter ? CuiV2Value.From(parameter) : null,
            CuiV2Span.From(action.Span), action.LocalTarget);

    public CuiActionDefinition ToCuiAction() =>
        new(Name, Command,
            Parameter is { } parameter ? parameter.ToCuiValue() : null,
            Span.ToCuiSourceSpan(), LocalTarget);
}

public sealed record CuiV2ActionReference(string Name, CuiV2Span Span)
{
    public static CuiV2ActionReference From(CuiActionReference reference) =>
        new(reference.Name, CuiV2Span.From(reference.Span));

    public CuiActionReference ToCuiActionReference() => new(Name, Span.ToCuiSourceSpan());
}

public sealed record CuiV2Condition(CuiV2Value Test, bool Negate, CuiV2Span Span, bool IsLive = true)
{
    public static CuiV2Condition From(CuiCondition condition) =>
        new(CuiV2Value.From(condition.Test), condition.Negate, CuiV2Span.From(condition.Span), condition.IsLive);

    public CuiCondition ToCuiCondition() =>
        new(Test.ToCuiValue(), Negate, Span.ToCuiSourceSpan(), IsLive);
}

public sealed record CuiV2Repeat(CuiV2Value Source, string ItemName, CuiV2Value Key, CuiV2Span Span, CuiV2Value? Template = null)
{
    public static CuiV2Repeat From(CuiRepeatDefinition repeat) =>
        new(CuiV2Value.From(repeat.Source), repeat.ItemName, CuiV2Value.From(repeat.Key),
            CuiV2Span.From(repeat.Span),
            repeat.Template is { } template ? CuiV2Value.From(template) : null);

    public CuiRepeatDefinition ToCuiRepeat() =>
        new(Source.ToCuiValue(), ItemName, Key.ToCuiValue(), Span.ToCuiSourceSpan(),
            Template is { } template ? template.ToCuiValue() : null);
}

public sealed record CuiV2PropertyRegion(string PropertyName, CuiV2Value Value, CuiV2Span Span)
{
    public static CuiV2PropertyRegion From(CuiPropertyRegionDefinition region) =>
        new(region.PropertyName, CuiV2Value.From(region.Value), CuiV2Span.From(region.Span));

    public CuiPropertyRegionDefinition ToCuiPropertyRegion() =>
        new(PropertyName, Value.ToCuiValue(), Span.ToCuiSourceSpan());
}

public sealed record CuiV2List(CuiV2Value Items, string ItemTemplate, string? EmptyTemplate, CuiV2Span Span)
{
    public static CuiV2List From(CuiListDefinition list) =>
        new(CuiV2Value.From(list.Items), list.ItemTemplate, list.EmptyTemplate, CuiV2Span.From(list.Span));

    public CuiListDefinition ToCuiList() =>
        new(Items.ToCuiValue(), ItemTemplate, EmptyTemplate, Span.ToCuiSourceSpan());
}

public sealed record CuiV2Import(string Source, string? Alias, CuiV2Span Span)
{
    public static CuiV2Import From(CuiImportDefinition import) =>
        new(import.Source, import.Alias, CuiV2Span.From(import.Span));

    public CuiImportDefinition ToCuiImport() => new(Source, Alias, Span.ToCuiSourceSpan());
}

public sealed record CuiV2Variable(string Name, string Type, string Value, CuiV2Span Span, string? Source = null, string? Selection = null)
{
    public static CuiV2Variable From(CuiVariableDeclaration declaration) =>
        new(declaration.Name, declaration.Type, declaration.Value,
            CuiV2Span.From(declaration.Span), declaration.Source, declaration.Selection);

    public CuiVariableDeclaration ToCuiVariable() =>
        new(Name, Type, Value, Span.ToCuiSourceSpan(), Source, Selection);
}

public sealed record CuiV2Template(string Name, IReadOnlyList<string> Parameters, IReadOnlyList<CuiV2Component> Content, CuiV2Span Span)
{
    public static CuiV2Template From(CuiTemplateDefinition template) =>
        new(template.Name, template.Parameters.ToArray(),
            template.Content.Select(CuiV2Component.From).ToArray(), CuiV2Span.From(template.Span));

    public CuiTemplateDefinition ToCuiTemplate() =>
        new(Name, Parameters.ToArray(), Content.Select(component => component.ToCuiComponent()).ToArray(), Span.ToCuiSourceSpan());
}

/// <summary>Wire mirror of <see cref="CuiComponent"/> preserving every semantic distinction the
/// canonical language model carries (text vs children, regions, groups, definitions, repeats,
/// conditionals, lists, theme scopes and action references).</summary>
public sealed record CuiV2Component(
    string Type,
    string? Name,
    IReadOnlyList<string> Classes,
    IReadOnlyDictionary<string, CuiV2Value> Properties,
    IReadOnlyDictionary<string, CuiV2ActionReference> Actions,
    IReadOnlyList<CuiV2Component> Children,
    CuiV2Condition? Condition,
    CuiV2List? List,
    CuiV2Span Span,
    string? DefaultTheme = null,
    CuiV2Repeat? Repeat = null,
    IReadOnlyList<CuiV2Component>? ElseChildren = null,
    IReadOnlyList<string>? Groups = null,
    bool IsDefinition = false,
    CuiV2PropertyRegion? PropertyRegion = null,
    string Text = "",
    IReadOnlyList<CuiV2TextPart>? TextParts = null)
{
    public static CuiV2Component From(CuiComponent component) => new(
        component.Type,
        component.Name,
        component.Classes.ToArray(),
        component.Properties.ToDictionary(pair => pair.Key, pair => CuiV2Value.From(pair.Value), StringComparer.OrdinalIgnoreCase),
        component.Actions.ToDictionary(pair => pair.Key, pair => CuiV2ActionReference.From(pair.Value), StringComparer.OrdinalIgnoreCase),
        component.Children.Select(From).ToArray(),
        component.Condition is { } condition ? CuiV2Condition.From(condition) : null,
        component.List is { } list ? CuiV2List.From(list) : null,
        CuiV2Span.From(component.Span),
        component.DefaultTheme,
        component.Repeat is { } repeat ? CuiV2Repeat.From(repeat) : null,
        component.ElseChildren.Select(From).ToArray(),
        component.Groups.ToArray(),
        component.IsDefinition,
        component.PropertyRegion is { } region ? CuiV2PropertyRegion.From(region) : null,
        component.Text,
        component.TextParts.Select(CuiV2TextPart.From).ToArray());

    public CuiComponent ToCuiComponent() => new(
        Type,
        Name,
        Classes.ToArray(),
        Properties.ToDictionary(pair => pair.Key, pair => pair.Value.ToCuiValue(), StringComparer.OrdinalIgnoreCase),
        Actions.ToDictionary(pair => pair.Key, pair => pair.Value.ToCuiActionReference(), StringComparer.OrdinalIgnoreCase),
        Children.Select(child => child.ToCuiComponent()).ToArray(),
        Condition is { } condition ? condition.ToCuiCondition() : null,
        List is { } list ? list.ToCuiList() : null,
        Span.ToCuiSourceSpan(),
        DefaultTheme,
        Repeat is { } repeat ? repeat.ToCuiRepeat() : null,
        ElseChildren?.Select(child => child.ToCuiComponent()).ToArray() ?? Array.Empty<CuiComponent>(),
        Groups ?? Array.Empty<string>(),
        IsDefinition,
        PropertyRegion is { } region ? region.ToCuiPropertyRegion() : null)
    {
        Text = Text,
        TextParts = TextParts ?? Array.Empty<CuiTextPart>(),
    };
}

/// <summary>Full typed semantic document payload (wire-safe, no source text). This is the value
/// the real consumer reconstructs into a <see cref="CuiDocument"/> without reparsing XML or
/// invoking the compiler.</summary>
public sealed record CuiV2Semantics(
    string SourceName,
    IReadOnlyDictionary<string, CuiV2Resource> Resources,
    IReadOnlyList<CuiV2Style> Styles,
    IReadOnlyDictionary<string, CuiV2Action> Actions,
    IReadOnlyDictionary<string, CuiV2Template> Templates,
    IReadOnlyList<CuiV2Component> Components,
    CuiV2Span Span,
    IReadOnlyDictionary<string, CuiV2Value> RootProperties,
    IReadOnlyList<CuiV2Import> Imports,
    IReadOnlyDictionary<string, CuiV2Variable> Variables)
{
    public static CuiV2Semantics From(CuiDocument document) => new(
        document.SourceName,
        document.Resources.ToDictionary(pair => pair.Key, pair => CuiV2Resource.From(pair.Value), StringComparer.OrdinalIgnoreCase),
        document.Styles.Select(CuiV2Style.From).ToArray(),
        document.Actions.ToDictionary(pair => pair.Key, pair => CuiV2Action.From(pair.Value), StringComparer.OrdinalIgnoreCase),
        document.Templates.ToDictionary(pair => pair.Key, pair => CuiV2Template.From(pair.Value), StringComparer.OrdinalIgnoreCase),
        document.Components.Select(CuiV2Component.From).ToArray(),
        CuiV2Span.From(document.Span),
        document.RootProperties.ToDictionary(pair => pair.Key, pair => CuiV2Value.From(pair.Value), StringComparer.OrdinalIgnoreCase),
        document.Imports.Select(CuiV2Import.From).ToArray(),
        document.Variables.ToDictionary(pair => pair.Key, pair => CuiV2Variable.From(pair.Value), StringComparer.OrdinalIgnoreCase));

    public CuiDocument ToCuiDocument() => new(
        SourceName,
        Resources.ToDictionary(pair => pair.Key, pair => pair.Value.ToCuiResource(), StringComparer.OrdinalIgnoreCase),
        Styles.Select(style => style.ToCuiStyle()).ToArray(),
        Actions.ToDictionary(pair => pair.Key, pair => pair.Value.ToCuiAction(), StringComparer.OrdinalIgnoreCase),
        Templates.ToDictionary(pair => pair.Key, pair => pair.Value.ToCuiTemplate(), StringComparer.OrdinalIgnoreCase),
        Components.Select(component => component.ToCuiComponent()).ToArray(),
        Span.ToCuiSourceSpan())
    {
        RootProperties = RootProperties.ToDictionary(pair => pair.Key, pair => pair.Value.ToCuiValue(), StringComparer.OrdinalIgnoreCase),
        Imports = Imports.Select(import => import.ToCuiImport()).ToArray(),
        Variables = Variables.ToDictionary(pair => pair.Key, pair => pair.Value.ToCuiVariable(), StringComparer.OrdinalIgnoreCase),
    };
}

/// <summary>Wire-safe source-map entry for one compiled stable identity.</summary>
public sealed record CuiV2SourceMapEntry(string StableId, CuiV2Span Source, string? AuthoredId, bool IsUnambiguous);

/// <summary>Wire-safe dependency edge (stable identity -> referenced symbol).</summary>
public sealed record CuiV2Dependency(string StableId, string Kind, string Name);

/// <summary>Wire-safe dependency graph.</summary>
public sealed record CuiV2DependencyGraph(IReadOnlyList<CuiV2Dependency> Entries);

/// <summary>Small helpers used by the typed transactions (kept internal).</summary>
internal static class CuiV2SpanExtensions
{
    public static CuiV2Span RequiredValue(this CuiV2Span? span, string what) =>
        span ?? throw new CuiV2AdmissionException("CUIR072", $"A required {what} span is missing.");
}
