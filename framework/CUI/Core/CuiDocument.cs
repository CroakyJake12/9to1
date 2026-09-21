using Avalonia.Metadata;
using CakeOS.Cui.Language;

namespace CakeOS.Cui;

public sealed record CuiResourceDefinition(string Key, CuiValue Value, CuiSourceSpan Span);

public sealed record CuiStyleSetter(string Property, CuiValue Value, CuiSourceSpan Span);

public sealed record CuiStyleDefinition(
    string Selector,
    IReadOnlyList<CuiStyleSetter> Setters,
    CuiSourceSpan Span);

public sealed record CuiActionDefinition(
    string Name,
    string Command,
    CuiValue? Parameter,
    CuiSourceSpan Span);

public sealed record CuiTemplateDefinition(
    string Name,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<CuiComponent> Content,
    CuiSourceSpan Span);

public sealed class CuiComponent
{
    public CuiComponent(
        string type,
        string? name,
        IReadOnlyList<string> classes,
        IReadOnlyDictionary<string, CuiValue> properties,
        IReadOnlyDictionary<string, CuiActionReference> actions,
        IReadOnlyList<CuiComponent> children,
        CuiCondition? condition,
        CuiListDefinition? list,
        CuiSourceSpan span,
        string? defaultTheme = null)
    {
        Type = type;
        Name = name;
        Classes = classes;
        Properties = properties;
        Actions = actions;
        Children = children;
        Condition = condition;
        List = list;
        Span = span;
        DefaultTheme = defaultTheme;
    }

    public string Type { get; }
    public string? Name { get; }
    public IReadOnlyList<string> Classes { get; }
    public IReadOnlyDictionary<string, CuiValue> Properties { get; }
    public IReadOnlyDictionary<string, CuiActionReference> Actions { get; }

    // This upstream Avalonia metadata contract is source-built by the CUI fork.
    [Content]
    public IReadOnlyList<CuiComponent> Children { get; }

    public CuiCondition? Condition { get; }
    public CuiListDefinition? List { get; }
    public CuiSourceSpan Span { get; }

    /// <summary>
    /// When set, this component is a DefaultTheme scope. All children inherit this theme.
    /// The value is the theme name: "Default", "Glow", "Bubble", "Retro", "Playful", "Cinematic".
    /// </summary>
    public string? DefaultTheme { get; }

    /// <summary>Whether this component is a theme scope (DefaultTheme element).</summary>
    public bool IsThemeScope => DefaultTheme is not null;

    public IEnumerable<CuiComponent> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
        foreach (var descendant in child.DescendantsAndSelf())
            yield return descendant;
    }
}

public sealed record CuiDocument(
    string SourceName,
    IReadOnlyDictionary<string, CuiResourceDefinition> Resources,
    IReadOnlyList<CuiStyleDefinition> Styles,
    IReadOnlyDictionary<string, CuiActionDefinition> Actions,
    IReadOnlyDictionary<string, CuiTemplateDefinition> Templates,
    IReadOnlyList<CuiComponent> Components,
    CuiSourceSpan Span);
