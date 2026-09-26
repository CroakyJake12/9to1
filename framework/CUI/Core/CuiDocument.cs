using CakeOS.Cui.Language;
using System.Security.Cryptography;
using System.Text;

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
        string? defaultTheme = null,
        CuiRepeatDefinition? repeat = null,
        IReadOnlyList<CuiComponent>? elseChildren = null,
        IReadOnlyList<string>? groups = null,
        bool isDefinition = false)
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
        Repeat = repeat;
        ElseChildren = elseChildren ?? Array.Empty<CuiComponent>();
        Groups = groups ?? Array.Empty<string>();
        IsDefinition = isDefinition;
    }

    public string Type { get; }
    public string? Name { get; }
    public string? AuthoredId => Name;
    public string StableId => Name is not null ? $"id:{Name}" : CreateGeneratedStableId();
    public IReadOnlyList<string> Classes { get; }
    public IReadOnlyDictionary<string, CuiValue> Properties { get; }
    public IReadOnlyDictionary<string, CuiActionReference> Actions { get; }

    public IReadOnlyList<CuiComponent> Children { get; }

    public CuiCondition? Condition { get; }
    public CuiListDefinition? List { get; }
    public CuiRepeatDefinition? Repeat { get; }
    public IReadOnlyList<CuiComponent> ElseChildren { get; }
    public IReadOnlyList<string> Groups { get; }
    public bool IsDefinition { get; }
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
        if (Groups.Count > 0)
            yield return "group";
        if (IsDefinition)
            yield return "definition";
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
        foreach (var child in ElseChildren)
        foreach (var descendant in child.DescendantsAndSelf())
            yield return descendant;
    }

    private string CreateGeneratedStableId()
    {
        var semanticParts = new List<string>
        {
            Type,
            DefaultTheme ?? string.Empty,
            IsDefinition ? "definition" : "instance",
            Text,
            string.Join("\u001f", Classes.Order(StringComparer.Ordinal)),
            string.Join("\u001f", Groups.Order(StringComparer.Ordinal)),
        };

        semanticParts.AddRange(Properties
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"property:{pair.Key}={DescribeValue(pair.Value)}"));
        semanticParts.AddRange(Actions
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"action:{pair.Key}={pair.Value.Name}"));

        if (Condition is not null)
        {
            semanticParts.Add($"condition:{DescribeValue(Condition.Test)}:{Condition.Negate}:{Condition.IsLive}");
        }
        if (Repeat is not null)
        {
            semanticParts.Add($"repeat:{Repeat.ItemName}:{DescribeValue(Repeat.Source)}:{DescribeValue(Repeat.Key)}");
        }
        if (List is not null)
        {
            semanticParts.Add($"list:{DescribeValue(List.Items)}:{List.ItemTemplate}:{List.EmptyTemplate}");
        }

        semanticParts.AddRange(Children.Select(child => child.StableId).Order(StringComparer.Ordinal));
        semanticParts.AddRange(ElseChildren.Select(child => child.StableId).Order(StringComparer.Ordinal));

        var canonical = string.Join("\u001e", semanticParts.Select(part => $"{part.Length}:{part}"));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"gen:v1:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    private static string DescribeValue(CuiValue value) => value switch
    {
        CuiLiteralValue literal => $"literal:{literal.Value}",
        CuiBindingValue binding => $"binding:{binding.Path}:{binding.Mode}:{binding.Fallback}:{binding.TargetType}",
        CuiResourceValue resource => $"resource:{resource.Key}",
        CuiInvalidValue invalid => $"invalid:{invalid.DiagnosticCode}:{invalid.RawValue}",
        _ => value.GetType().FullName ?? value.GetType().Name,
    };
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
