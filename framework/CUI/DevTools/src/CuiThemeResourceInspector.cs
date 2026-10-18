using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using LanguageSpan = CakeOS.Cui.Language.CuiSourceSpan;

namespace Haven.CUI.DevTools;

public sealed record CuiThemeResourceRule(
    string Code,
    string Title,
    string RequirementId,
    string SpecLines);

public sealed record CuiThemeResourceFinding(
    CuiThemeResourceRule Rule,
    CuiDiagnostic Diagnostic,
    string? SuggestedCorrection);

public sealed record CuiThemeScopeInspection(
    ElementId ElementId,
    string AuthoredThemeName,
    string EffectiveTheme,
    string ResolutionSource,
    int Depth,
    CuiSourceLocation Source);

public sealed record CuiResourceInspection(
    string Key,
    string ValueKind,
    string Value,
    CuiSourceLocation Source,
    IReadOnlyList<ElementId> ReferencedBy,
    IReadOnlyList<string> ReferenceChain,
    bool Resolved);

public sealed record CuiStyleSetterInspection(
    string Property,
    string Value,
    string PrecedenceLevel,
    CuiSourceLocation Source);

public sealed record CuiStyleInspection(
    string Selector,
    string TargetKind,
    string? TargetId,
    string? TargetGroup,
    CuiSourceLocation Source,
    IReadOnlyList<CuiStyleSetterInspection> Setters,
    IReadOnlyList<ElementId> MatchedElements);

public sealed class CuiThemeResourceInspection
{
    private readonly Dictionary<ElementId, string> _effectiveTheme;
    private readonly Dictionary<ElementId, CuiComponent> _components;
    private readonly Dictionary<string, CuiComponent> _definitions;
    private readonly Dictionary<string, CuiResourceInspection> _resources;
    private readonly Dictionary<string, CuiStyleInspection> _styles;

    public CuiThemeResourceInspection(
        CuiDocument document,
        CuiThemeDefinitionCatalog? customThemes,
        IEnumerable<CuiThemeScopeInspection> scopes,
        IReadOnlyDictionary<ElementId, string> effectiveTheme,
        IEnumerable<CuiResourceInspection> resources,
        IEnumerable<CuiStyleInspection> styles,
        IEnumerable<CuiThemeResourceFinding> findings,
        string globalDefaultTheme)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        CustomThemes = customThemes;
        Scopes = DiagnosticsCollections.Copy(scopes);
        Resources = DiagnosticsCollections.Copy(resources);
        Styles = DiagnosticsCollections.Copy(styles);
        Findings = DiagnosticsCollections.Copy(findings);
        GlobalDefaultTheme = globalDefaultTheme;

        _effectiveTheme = new Dictionary<ElementId, string>(effectiveTheme);
        _components = new Dictionary<ElementId, CuiComponent>();
        _definitions = new Dictionary<string, CuiComponent>(StringComparer.Ordinal);
        foreach (var component in document.Components.SelectMany(item => item.DescendantsAndSelf()))
        {
            var id = new ElementId(component.StableId);
            _components[id] = component;
            if (component.IsDefinition) _definitions[component.Name ?? component.Type] = component;
        }
        foreach (var scope in Scopes) _effectiveTheme[scope.ElementId] = scope.EffectiveTheme;
        _resources = Resources.ToDictionary(resource => resource.Key, StringComparer.Ordinal);
        _styles = Styles.ToDictionary(style => style.Selector, StringComparer.Ordinal);
    }

    public CuiDocument Document { get; }

    public CuiThemeDefinitionCatalog? CustomThemes { get; }

    public IReadOnlyList<CuiThemeScopeInspection> Scopes { get; }

    public IReadOnlyList<CuiResourceInspection> Resources { get; }

    public IReadOnlyList<CuiStyleInspection> Styles { get; }

    public IReadOnlyList<CuiThemeResourceFinding> Findings { get; }

    public string GlobalDefaultTheme { get; }

    public string? EffectiveThemeAt(ElementId elementId) => _effectiveTheme.GetValueOrDefault(elementId);

    public CuiResourceInspection? Resource(string key) => _resources.GetValueOrDefault(key);

    public CuiStyleInspection? Style(string selector) => _styles.GetValueOrDefault(selector);

    public IReadOnlyList<CuiResourceInspection> ResourcesReferencedBy(ElementId elementId) =>
        Array.AsReadOnly(Resources.Where(resource => resource.ReferencedBy.Contains(elementId))
            .OrderBy(resource => resource.Key, StringComparer.Ordinal)
            .ToArray());

    /// <summary>
    /// The precedence level a property value sits at for one element, from highest to lowest:
    /// active style overlay, scoped property region, explicit instance property, type/definition
    /// default, inherited theme, built-in default.
    /// </summary>
    public string PrecedenceLevelOf(ElementId elementId, string property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        var component = _components.GetValueOrDefault(elementId)
            ?? throw new ArgumentException($"Element '{elementId}' is not in the inspected document.", nameof(elementId));

        var canonical = CanonicalName(property);

        if (Styles.Any(style => CuiThemeResourceInspector.Matches(style.Selector, component) &&
            style.Setters.Any(setter => EqualsCanonical(setter.Property, canonical))))
            return CuiThemeResourceInspector.PrecedenceStyleOverlay;

        if (component.PropertyRegion is not null && EqualsCanonical(component.PropertyRegion.PropertyName, canonical))
            return CuiThemeResourceInspector.PrecedenceScopedRegion;

        if (!component.IsDefinition && component.Properties.Any(pair => EqualsCanonical(pair.Key, canonical)))
            return CuiThemeResourceInspector.PrecedenceExplicitInstance;

        if (component.IsDefinition ||
            _definitions.TryGetValue(component.Type, out var definition) &&
            definition.Properties.Any(pair => EqualsCanonical(pair.Key, canonical)))
            return CuiThemeResourceInspector.PrecedenceDefinitionDefault;

        var theme = _effectiveTheme.GetValueOrDefault(elementId);
        var type = component.Properties.FirstOrDefault(pair =>
            pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)).Value as CuiLiteralValue;
        if (CustomThemes is not null && theme is not null && CustomThemes.TryResolve(theme, out var resolved) && resolved is not null &&
            resolved.Rules.Any(rule => string.Equals(rule.Key.ElementName, component.Type, StringComparison.OrdinalIgnoreCase) &&
                (rule.Key.TypeName is null || string.Equals(rule.Key.TypeName, type?.Value, StringComparison.Ordinal)) &&
                rule.Value.Keys.Any(name => EqualsCanonical(name, canonical))))
            return CuiThemeResourceInspector.PrecedenceInheritedTheme;

        return CuiThemeResourceInspector.PrecedenceBuiltInDefault;
    }

    public ResourceSnapshot Snapshot() => new(Array.AsReadOnly(Resources.Select(resource =>
        new ResourceValueSnapshot(
            resource.Key,
            resource.Value,
            "document resource scope",
            resource.Resolved)).ToArray()));

    public StyleSnapshot SnapshotFor(ElementId elementId)
    {
        var values = Styles.Where(style => style.MatchedElements.Contains(elementId))
            .SelectMany(style => style.Setters.Select(setter => new StyleValueSnapshot(
                setter.Property,
                setter.Value,
                style.Selector,
                setter.PrecedenceLevel == CuiThemeResourceInspector.PrecedenceStyleOverlay,
                setter.PrecedenceLevel == CuiThemeResourceInspector.PrecedenceInheritedTheme)))
            .OrderBy(value => value.Property, StringComparer.Ordinal)
            .ToArray();

        return new StyleSnapshot(elementId.Value, [], [], Array.AsReadOnly(values));
    }

    internal static string CanonicalName(string property) =>
        CuiControlRegistry.Default.TryGetProperty(property, out var descriptor) ? descriptor.Name : property;

    private static bool EqualsCanonical(string name, string canonical) =>
        string.Equals(CanonicalName(name), canonical, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Theme and resource inspection over the CUI language model. Theme scopes are resolved as
/// inherited visual-design scopes, so a descendant override returns to the parent when it
/// closes. Resources are resolved through their reference chains, and style targets are
/// resolved against the documented ID and Group identity mechanism.
/// </summary>
public sealed class CuiThemeResourceInspector
{
    public const string PrecedenceBuiltInDefault = "built-in default";
    public const string PrecedenceInheritedTheme = "inherited theme";
    public const string PrecedenceDefinitionDefault = "type/definition default";
    public const string PrecedenceExplicitInstance = "explicit instance property";
    public const string PrecedenceScopedRegion = "scoped property region";
    public const string PrecedenceStyleOverlay = "active style overlay";

    private static readonly string[] SpecBuiltInThemes =
        ["Professional", "Glow", "Playful", "Bubble", "Cinematic"];

    private static readonly CuiThemeResourceRule ThemeNameResolution = new(
        "CUIT001", "A theme scope name must resolve to a built-in, declared custom theme or the reserved user_preference reference",
        "FW-017", "development-specification.txt 983-1010");
    private static readonly CuiThemeResourceRule BuiltInThemeParity = new(
        "CUIT002", "The built-in theme catalogue must match the specification's built-in theme names",
        "FW-017", "development-specification.txt 983-1010");
    private static readonly CuiThemeResourceRule CustomThemeInScope = new(
        "CUIT003", "A declared custom theme must be accepted in a Theme scope",
        "FW-017", "development-specification.txt 983-1010");
    private static readonly CuiThemeResourceRule UnresolvedResource = new(
        "CUIT004", "The compiler must not silently accept unknown symbols",
        "FW-011", "development-specification.txt 1321-1328");
    private static readonly CuiThemeResourceRule ResourceReferenceCycle = new(
        "CUIT005", "A resource reference chain must terminate",
        "FW-011", "development-specification.txt 1321-1328");
    private static readonly CuiThemeResourceRule UnreferencedResource = new(
        "CUIT006", "A declared resource that nothing references is dead scope",
        "FW-020", "development-specification.txt 1290-1304");
    private static readonly CuiThemeResourceRule UnmatchedStyleTarget = new(
        "CUIT007", "A style target must resolve to an element in the document",
        "FW-016", "development-specification.txt 1163-1201");
    private static readonly CuiThemeResourceRule IdGroupCollision = new(
        "CUIT008", "An ID and a Group must not use the same identifier in one scope",
        "FW-012", "development-specification.txt 1051-1063");
    private static readonly CuiThemeResourceRule ThemeScopeNotFlagged = new(
        "CUIT009", "A Theme element must create an inherited theme scope",
        "FW-017", "development-specification.txt 983-1010");

    public IReadOnlyList<CuiThemeResourceRule> Rules { get; } =
    [
        ThemeNameResolution, BuiltInThemeParity, CustomThemeInScope, UnresolvedResource,
        ResourceReferenceCycle, UnreferencedResource, UnmatchedStyleTarget, IdGroupCollision,
        ThemeScopeNotFlagged
    ];

    public CuiThemeResourceInspection Inspect(
        CuiDocument document,
        CuiThemeDefinitionCatalog? customThemes = null,
        CuiTheme? globalDefault = null,
        long revision = 0)
    {
        ArgumentNullException.ThrowIfNull(document);
        var global = globalDefault ?? CuiSurfacePaletteCatalog.ActiveTheme;

        var scopes = new List<CuiThemeScopeInspection>();
        var findings = new List<CuiThemeResourceFinding>();
        var effective = new Dictionary<ElementId, string>();

        Walk(document.Components, 0, CuiThemeCatalog.Name(global), "global-default-reference");

        void Walk(IReadOnlyList<CuiComponent> items, int depth, string inheritedTheme, string inheritedSource)
        {
            foreach (var component in items)
            {
                var id = new ElementId(component.StableId);
                var theme = inheritedTheme;
                var source = inheritedSource;

                // A Theme definition declares reusable presentation; it does not
                // activate that theme merely by appearing in the document.
                var isThemeScope = !component.IsDefinition && (component.IsThemeScope
                                    || (string.Equals(component.Type, "Theme", StringComparison.OrdinalIgnoreCase)
                                        && component.Name is not null));

                if (isThemeScope)
                {
                    var authored = component.DefaultTheme ?? component.Name!;

                    if (!component.IsThemeScope)
                        findings.Add(Finding(ThemeScopeNotFlagged, component.Span,
                            $"Element 'Theme' named '{component.Name}' is a theme scope under the specification, but the language model does not flag it as one, so its children do not inherit it.",
                            "Use the DefaultTheme construct, or make the parser flag Theme elements as theme scopes."));

                    var resolved = ResolveTheme(authored, customThemes, global, component, findings);
                    theme = resolved.Theme;
                    source = resolved.Source;
                    scopes.Add(new CuiThemeScopeInspection(id, authored, theme, source, depth,
                        Location(document.SourceName, component.Span, component.Name)));
                }

                effective[id] = theme;

                if (component.Name is not null && component.Groups.Any(group => string.Equals(group, component.Name, StringComparison.Ordinal)))
                    findings.Add(Finding(IdGroupCollision, component.Span,
                        $"Element '{component.Type}' uses '{component.Name}' as both an ID and a Group in the same scope, so a selector for that name is ambiguous.",
                        "Give the ID and the Group different identifiers."));

                Walk(component.Children, depth + 1, theme, source);
                Walk(component.ElseChildren, depth + 1, theme, source);
            }
        }

        return new CuiThemeResourceInspection(
            document,
            customThemes,
            scopes,
            effective,
            ResolveResources(document, findings),
            ResolveStyles(document, findings),
            findings,
            CuiThemeCatalog.Name(global));
    }

    private static (string Theme, string Source) ResolveTheme(
        string authored,
        CuiThemeDefinitionCatalog? customThemes,
        CuiTheme globalDefault,
        CuiComponent scope,
        List<CuiThemeResourceFinding> findings)
    {
        var name = authored.Trim();

        if (name.Length == 0)
        {
            findings.Add(Finding(ThemeNameResolution, scope.Span,
                "A theme scope was authored without a name, so the effective theme is the currently configured global default.",
                "Name the scope explicitly, or use the reserved user_preference reference when the scope should follow the Home preference."));
            return (CuiThemeCatalog.Name(globalDefault), "global-default-reference");
        }

        if (string.Equals(name, "user_preference", StringComparison.Ordinal))
            return (CuiThemeCatalog.Name(globalDefault), "user-preference-resolver");

        if (name is "Default")
            return (CuiThemeCatalog.Name(globalDefault), "global-default-reference");

        var builtIn = CuiThemeCatalog.All.FirstOrDefault(candidate =>
            string.Equals(candidate.DisplayName, name, StringComparison.OrdinalIgnoreCase));

        if (builtIn is not null)
        {
            if (!SpecBuiltInThemes.Contains(builtIn.DisplayName, StringComparer.OrdinalIgnoreCase))
                findings.Add(Finding(BuiltInThemeParity, scope.Span,
                    $"Theme scope '{builtIn.DisplayName}' is present in the theme catalogue but is not one of the built-in theme names listed in the specification.",
                    "Confirm the authoritative built-in theme list before relying on this name, or remove it from the catalogue."));
            return (builtIn.DisplayName, "built-in");
        }

        if (customThemes is not null && customThemes.TryResolve(name, out var resolved) && resolved is not null)
        {
            findings.Add(Finding(CustomThemeInScope, scope.Span,
                $"Theme scope '{name}' resolves through the custom theme definition catalogue, but compiler theme validation accepts built-in names only.",
                "Extend compiler theme validation to accept declared custom themes, or use a built-in name in this scope."));
            return (resolved.Name, "custom-theme-definition");
        }

        findings.Add(Finding(ThemeNameResolution, scope.Span,
            $"Theme scope '{name}' is unavailable or incompatible, so it falls back to Professional.",
            "Declare the theme in a .cui-theme file, use a built-in name, or use user_preference to follow the Home-managed preference."));
        return (CuiThemeCatalog.Name(CuiTheme.Professional), "unresolved-fallback");
    }

    private static List<CuiResourceInspection> ResolveResources(CuiDocument document, List<CuiThemeResourceFinding> findings)
    {
        var referencedBy = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);

        void Record(string key, ElementId id)
        {
            if (!referencedBy.TryGetValue(key, out var list))
            {
                list = [];
                referencedBy[key] = list;
            }
            if (!list.Contains(id)) list.Add(id);
        }

        foreach (var component in document.Components.SelectMany(item => item.DescendantsAndSelf()))
        {
            var id = new ElementId(component.StableId);
            foreach (var value in component.Properties.Values)
                if (value is CuiResourceValue resource) Record(resource.Key, id);
            foreach (var part in component.TextParts)
                if (part is CuiExpressionTextPart expression && expression.Value is CuiResourceValue resource) Record(resource.Key, id);
        }

        foreach (var style in document.Styles)
        foreach (var setter in style.Setters)
            if (setter.Value is CuiResourceValue resource) Record(resource.Key, new ElementId("style:" + style.Selector));

        var result = new List<CuiResourceInspection>();
        var reportedCycles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in document.Resources.Values.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var chain = new List<string> { definition.Key };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { definition.Key };
            var current = definition.Value;
            var resolves = true;

            while (current is CuiResourceValue reference)
            {
                if (!seen.Add(reference.Key))
                {
                    chain.Add(reference.Key);
                    resolves = false;
                    var cycleRoot = seen.OrderBy(key => key, StringComparer.Ordinal).First();
                    if (reportedCycles.Add(cycleRoot))
                        findings.Add(Finding(ResourceReferenceCycle, definition.Span,
                            $"Resource '{definition.Key}' resolves through a reference cycle at '{reference.Key}'.",
                            "Break the chain so every resource reference terminates at a literal value."));
                    break;
                }

                Record(reference.Key, new ElementId("resource:" + definition.Key));
                chain.Add(reference.Key);

                if (!document.Resources.TryGetValue(reference.Key, out var next))
                {
                    resolves = false;
                    findings.Add(Finding(UnresolvedResource, definition.Span,
                        $"Resource '{definition.Key}' references '{reference.Key}', which is not declared in the document resource scope.",
                        "Declare the referenced resource, or point the reference at a declared key."));
                    break;
                }

                current = next.Value;
            }

            var references = referencedBy.GetValueOrDefault(definition.Key) ?? [];
            if (references.Count == 0)
                findings.Add(Finding(UnreferencedResource, definition.Span,
                    $"Resource '{definition.Key}' is declared but nothing in the document references it.",
                    "Remove the resource, or reference it from the element that needs it."));

            result.Add(new CuiResourceInspection(
                definition.Key,
                ValueKind(definition.Value),
                Render(definition.Value),
                Location(document.SourceName, definition.Span, null),
                Array.AsReadOnly(references.OrderBy(item => item.Value, StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(chain.ToArray()),
                resolves));
        }

        return result;
    }

    private static List<CuiStyleInspection> ResolveStyles(CuiDocument document, List<CuiThemeResourceFinding> findings)
    {
        var result = new List<CuiStyleInspection>();

        foreach (var style in document.Styles)
        {
            var selector = style.Selector.Trim();
            var kind = "unqualified";
            var targetId = (string?)null;
            var targetGroup = (string?)null;

            if (selector.StartsWith("{:::", StringComparison.Ordinal) && selector.EndsWith('}'))
            {
                kind = "group";
                targetGroup = selector[4..^1];
            }
            else if (selector.StartsWith("{::", StringComparison.Ordinal) && selector.EndsWith('}'))
            {
                kind = "id";
                targetId = selector[3..^1];
            }

            var matched = document.Components
                .SelectMany(component => component.DescendantsAndSelf())
                .Where(component => Matches(style.Selector, component))
                .Select(component => new ElementId(component.StableId))
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();

            if (kind is "id" or "group" && matched.Length == 0)
                findings.Add(Finding(UnmatchedStyleTarget, style.Span,
                    $"Style selector '{style.Selector}' targets {(kind == "id" ? "ID" : "Group")} '{targetId ?? targetGroup}', which resolves to no element in this document.",
                    "Add the targeted element, or correct the selector."));

            result.Add(new CuiStyleInspection(
                style.Selector,
                kind,
                targetId,
                targetGroup,
                Location(document.SourceName, style.Span, null),
                Array.AsReadOnly(style.Setters.Select(setter => new CuiStyleSetterInspection(
                    CuiThemeResourceInspection.CanonicalName(setter.Property),
                    Render(setter.Value),
                    PrecedenceStyleOverlay,
                    Location(document.SourceName, setter.Span, null))).ToArray()),
                Array.AsReadOnly(matched)));
        }

        return result;
    }

    internal static bool Matches(string rawSelector, CuiComponent component)
    {
        var selector = rawSelector.Trim();

        if (selector.StartsWith("{:::", StringComparison.Ordinal) && selector.EndsWith('}'))
            return component.Groups.Any(group => string.Equals(group, selector[4..^1], StringComparison.Ordinal));

        if (selector.StartsWith("{::", StringComparison.Ordinal) && selector.EndsWith('}'))
            return string.Equals(component.Name, selector[3..^1], StringComparison.Ordinal);

        return string.Equals(component.Type, selector, StringComparison.OrdinalIgnoreCase)
               || string.Equals(component.Name, selector, StringComparison.Ordinal)
               || component.Classes.Any(className => string.Equals(className, selector, StringComparison.OrdinalIgnoreCase));
    }

    private static string ValueKind(CuiValue value) => value switch
    {
        CuiLiteralValue => "literal",
        CuiResourceValue => "resource reference",
        CuiBindingValue => "binding",
        CuiInvalidValue => "invalid",
        _ => value.GetType().Name,
    };

    private static string Render(CuiValue value) => value switch
    {
        CuiLiteralValue literal => literal.Value,
        CuiResourceValue resource => "{Resource " + resource.Key + "}",
        CuiBindingValue binding => "{Binding " + binding.Path + "}",
        CuiInvalidValue invalid => invalid.RawValue,
        _ => string.Empty,
    };

    private static CuiSourceLocation Location(string sourceName, LanguageSpan span, string? authoredId) =>
        CuiSourceLocation.Create(sourceName,
            new CuiSourceSpan(span.Start.Line, span.Start.Column, span.End.Line, span.End.Column), authoredId);

    private static CuiThemeResourceFinding Finding(
        CuiThemeResourceRule rule,
        LanguageSpan span,
        string message,
        string suggestion) =>
        new(rule, new CuiDiagnostic(rule.Code, CuiDiagnosticSeverity.Warning, message, span), suggestion);
}
