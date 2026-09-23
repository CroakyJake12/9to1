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

    public IReadOnlyList<CuiComponent> Children { get; }

    public CuiCondition? Condition { get; }
    public CuiListDefinition? List { get; }
    public CuiSourceSpan Span { get; }

    /// <summary>
    /// When set, this component is a DefaultTheme scope. All children inherit this theme.
    /// The value is the theme name: "Default", "Glow", "Bubble", "Retro", "Playful", "Cinematic".
    /// </summary>
    public string? DefaultTheme { get; }

    /// <summary>
    /// Direct text authored inside the element.  CUI intentionally keeps this in the
    /// language model instead of requiring each platform adapter to re-read XML.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Whether this component is a theme scope (DefaultTheme element).</summary>
    public bool IsThemeScope => DefaultTheme is not null;

    /// <summary>
    /// Returns a literal authored attribute value when the attribute maps directly
    /// onto the CUI language model. Bindings and resources intentionally return
    /// false because platform adapters must not silently flatten dynamic values.
    /// </summary>
    public bool TryGetLiteralAttribute(string name, out string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "name", StringComparison.OrdinalIgnoreCase))
        {
            value = Name;
            return value is not null;
        }

        if (Actions.TryGetValue(name, out var action))
        {
            value = action.Name;
            return true;
        }

        if (Properties.TryGetValue(name, out var property)
            && property is CuiLiteralValue literal)
        {
            value = literal.Value;
            return true;
        }

        value = null;
        return false;
    }

    public IEnumerable<string> AuthoredAttributeNames()
    {
        if (Name is not null)
            yield return "id";
        foreach (var property in Properties.Keys)
            yield return property;
        foreach (var action in Actions.Keys)
            yield return action;
    }

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
    CuiSourceSpan Span)
{
    /// <summary>
    /// Attributes authored on the document's <Cui> root. These are document metadata
    /// (for example id, product, and version), not a second parser-specific tree.
    /// </summary>
    public IReadOnlyDictionary<string, CuiValue> RootProperties { get; init; } =
        new Dictionary<string, CuiValue>(StringComparer.Ordinal);
}
