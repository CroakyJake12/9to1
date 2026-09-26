using System.Collections.ObjectModel;
using System.Globalization;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace CakeOS.Cui.Compiler;

/// <summary>Compatibility values written into each compiled CUI artifact.</summary>
public sealed record CuiCompilationMetadata
{
    public CuiCompilationMetadata(
        string languageVersion,
        string runtimeAbiVersion,
        string componentRegistryVersion,
        IEnumerable<string>? requiredCapabilities = null)
    {
        LanguageVersion = RequireVersion(languageVersion, nameof(languageVersion));
        RuntimeAbiVersion = RequireVersion(runtimeAbiVersion, nameof(runtimeAbiVersion));
        ComponentRegistryVersion = RequireVersion(componentRegistryVersion, nameof(componentRegistryVersion));
        RequiredCapabilities = Array.AsReadOnly((requiredCapabilities ?? [])
            .Select(value => string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Capability names cannot be empty.", nameof(requiredCapabilities))
                : value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());
    }

    public string LanguageVersion { get; }
    public string RuntimeAbiVersion { get; }
    public string ComponentRegistryVersion { get; }
    public IReadOnlyList<string> RequiredCapabilities { get; }

    private static string RequireVersion(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty compatibility version is required.", parameterName)
            : value.Trim();
}

public sealed record CuiCompilerOptions(CuiCompilationMetadata Metadata);

public enum CuiDependencyKind
{
    Definition,
    Template,
    Resource,
    Variable,
    Action
}

public sealed record CuiCompilationDependency(CuiDependencyKind Kind, string Name);

public sealed class CuiDependencyGraph
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<CuiCompilationDependency>> _dependencies;

    internal CuiDependencyGraph(IEnumerable<KeyValuePair<string, IEnumerable<CuiCompilationDependency>>> dependencies)
    {
        _dependencies = new ReadOnlyDictionary<string, IReadOnlyList<CuiCompilationDependency>>(
            dependencies.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<CuiCompilationDependency>)Array.AsReadOnly(pair.Value
                    .Distinct()
                    .OrderBy(item => item.Kind)
                    .ThenBy(item => item.Name, StringComparer.Ordinal)
                    .ToArray()),
                StringComparer.Ordinal));
    }

    public IReadOnlyDictionary<string, IReadOnlyList<CuiCompilationDependency>> Entries => _dependencies;

    public IReadOnlyList<CuiCompilationDependency> GetDependencies(string stableId) =>
        _dependencies.GetValueOrDefault(stableId) ?? Array.Empty<CuiCompilationDependency>();
}

public sealed record CuiCompiledProperty(string Name, string LanguageType, CuiValue Value);

public sealed record CuiCompiledNode(
    string StableId,
    string Type,
    IReadOnlyDictionary<string, CuiCompiledProperty> Properties,
    IReadOnlyList<CuiCompiledNode> Children,
    IReadOnlyList<CuiCompiledNode> ElseChildren,
    CuiSourceSpan Source,
    bool IsDefinition);

public sealed record CuiCompiledTemplate(
    string Name,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<CuiCompiledNode> Content,
    CuiSourceSpan Source);

public sealed record CuiCompilerSourceMapEntry(string StableId, CuiSourceSpan Source, string? AuthoredId, bool IsUnambiguous);

/// <summary>
/// Direct CUI runtime representation. It retains typed CUI values and never translates markup to AXAML.
/// </summary>
public sealed record CuiCompiledDocument(
    CuiCompilationMetadata Metadata,
    IReadOnlyList<CuiCompiledNode> Components,
    IReadOnlyDictionary<string, CuiCompiledTemplate> Templates,
    IReadOnlyList<CuiCompilerSourceMapEntry> SourceMap,
    CuiDependencyGraph Dependencies,
    CuiDocument Semantics);

/// <summary>Compiles the canonical language model against the runtime-owned element/property registry.</summary>
public sealed class CuiCompiler
{
    private static readonly HashSet<string> LanguageConstructs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Theme", "Style", "Property", "Variable", "Prefab", "ActionEvent", "Import", "Keyframe",
        "If", "Else", "Repeat"
    };

    private static readonly HashSet<string> InteractiveElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "Button", "Input", "TextBox", "CheckBox", "RadioButton", "Slider", "ComboBox", "ListBox", "MenuItem"
    };

    private readonly CuiControlRegistry _registry;

    public CuiCompiler(CuiControlRegistry? registry = null) => _registry = registry ?? CuiControlRegistry.Default;

    public CuiCompilation Compile(string source, string sourceName, CuiCompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Metadata);

        var diagnostics = new CuiDiagnosticBag();
        var parser = new CuiRichParser();
        var document = parser.Parse(source, sourceName);
        foreach (var diagnostic in parser.Diagnostics.Diagnostics)
        {
            if (diagnostic.Severity == CuiDiagnosticSeverity.Error)
                diagnostics.Error(diagnostic.Code, diagnostic.Message, diagnostic.Span);
            else if (diagnostic.Severity == CuiDiagnosticSeverity.Warning)
                diagnostics.Warning(diagnostic.Code, diagnostic.Message, diagnostic.Span);
        }

        if (!IsCuiSource(sourceName))
        {
            diagnostics.Error("CUIC001", "CUI compilation accepts only .cui application markup.",
                CuiSourceSpan.At(sourceName, 1, 1));
        }

        if (document.Components.Count == 0)
        {
            diagnostics.Error("CUIC002", "A compiled CUI document must contain a root Page or component.", document.Span);
        }

        var definitions = CollectDefinitions(document);
        ValidateDefinitions(document, definitions, diagnostics);
        ValidateComponents(document.Components, definitions, diagnostics);

        if (diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            return new CuiCompilation(null, Array.AsReadOnly(diagnostics.Diagnostics.ToArray()));

        var sourceMap = BuildSourceMap(document);
        var dependencies = BuildDependencies(document, definitions);
        var compiledComponents = CompileNodes(document.Components, "document");
        var compiledTemplates = new ReadOnlyDictionary<string, CuiCompiledTemplate>(document.Templates.ToDictionary(
            pair => pair.Key,
            pair => new CuiCompiledTemplate(pair.Value.Name, Array.AsReadOnly(pair.Value.Parameters.ToArray()),
                CompileNodes(pair.Value.Content, $"template:{pair.Key}"), pair.Value.Span),
            StringComparer.Ordinal));
        if (diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            return new CuiCompilation(null, Array.AsReadOnly(diagnostics.Diagnostics.ToArray()));

        var output = new CuiCompiledDocument(options.Metadata, compiledComponents, compiledTemplates, sourceMap, dependencies, document);
        return new CuiCompilation(document, Array.AsReadOnly(diagnostics.Diagnostics.ToArray()))
        {
            Output = output
        };
    }

    private static bool IsCuiSource(string sourceName) =>
        string.Equals(Path.GetExtension(sourceName), ".cui", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> CollectDefinitions(CuiDocument document)
    {
        var definitions = new HashSet<string>(document.Templates.Keys, StringComparer.Ordinal);
        foreach (var component in AllComponents(document.Components))
        {
            if (component.IsDefinition)
                definitions.Add(component.Type);
        }
        return definitions;
    }

    private void ValidateDefinitions(CuiDocument document, HashSet<string> definitions, CuiDiagnosticBag diagnostics)
    {
        foreach (var definition in definitions)
        {
            if (_registry.TryResolveElement(definition, out _))
            {
                var span = AllComponents(document.Components)
                    .FirstOrDefault(component => component.IsDefinition && component.Type == definition)?.Span
                    ?? document.Span;
                diagnostics.Error("CUIC010", $"Definition '{definition}' conflicts with a built-in CUI element name.", span);
            }
        }
    }

    private void ValidateComponents(
        IReadOnlyList<CuiComponent> components,
        HashSet<string> definitions,
        CuiDiagnosticBag diagnostics)
    {
        foreach (var component in components)
        {
            var isLanguageConstruct = component.IsThemeScope || LanguageConstructs.Contains(component.Type);
            var isDefinition = component.IsDefinition;
            var isKnownElement = _registry.TryResolveElement(component.Type, out var element);

            if (!isLanguageConstruct && !isDefinition && !isKnownElement && !definitions.Contains(component.Type))
            {
                var suggestion = Suggest(component.Type, _registryElementNames());
                diagnostics.Error("CUIC011", suggestion is null
                    ? $"Unknown CUI element '{component.Type}'."
                    : $"Unknown CUI element '{component.Type}'. Did you mean '{suggestion}'?", component.Span);
            }

            if (!isLanguageConstruct && !isDefinition && (isKnownElement || definitions.Contains(component.Type)))
                ValidateProperties(component, element, diagnostics);

            if (component.IsThemeScope && component.DefaultTheme is { } theme)
                ValidateTheme(theme, component.Span, diagnostics);

            ValidateAccessibility(component, diagnostics);
            ValidateComponents(component.Children, definitions, diagnostics);
            ValidateComponents(component.ElseChildren, definitions, diagnostics);
        }
    }

    private void ValidateProperties(CuiComponent component, CuiRuntimeElementDescriptor element, CuiDiagnosticBag diagnostics)
    {
        foreach (var (name, value) in component.Properties)
        {
            if (!_registry.TryGetProperty(name, out var property))
            {
                var suggestion = Suggest(name, _registryPropertyNames());
                diagnostics.Error("CUIC012", suggestion is null
                    ? $"Unknown CUI property '{name}' on '{component.Type}'."
                    : $"Unknown CUI property '{name}' on '{component.Type}'. Did you mean '{suggestion}'?", value.Span);
                continue;
            }

            if (!element.AllowedProperties.Contains(property.Name)
                && !property.IsAttached)
            {
                diagnostics.Error("CUIC013", $"Property '{property.Name}' is not allowed on '{component.Type}'.", value.Span);
                continue;
            }

            if (!property.IsAttached && !property.SupportedElementTypes.Contains(element.Name))
                diagnostics.Error("CUIC014", $"Property '{property.Name}' is not supported on '{component.Type}'.", value.Span);

            if (value is CuiLiteralValue literal && !IsValidLiteral(property.LanguageType, literal.Value))
                diagnostics.Error("CUIC015", $"Value '{literal.Value}' is not valid for {property.LanguageType} property '{property.Name}'.", value.Span);
            else if (value is CuiLiteralValue symbol && property.AllowedValues.Count > 0
                     && !property.AllowedValues.Contains(symbol.Value))
            {
                var suggestion = Suggest(symbol.Value, property.AllowedValues);
                diagnostics.Error("CUIC017", suggestion is null
                    ? $"Value '{symbol.Value}' is not allowed for property '{property.Name}'."
                    : $"Value '{symbol.Value}' is not allowed for property '{property.Name}'. Did you mean '{suggestion}'?", symbol.Span);
            }
        }
    }

    private static bool IsValidLiteral(string languageType, string value) => languageType switch
    {
        "boolean" => bool.TryParse(value, out _),
        "integer" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        "number" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number),
        "length" => value.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                    || value.Contains('%') || value.Contains("fr", StringComparison.OrdinalIgnoreCase)
                    || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var length) && double.IsFinite(length),
        "color" => value.StartsWith('#') || value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("transparent", StringComparison.OrdinalIgnoreCase)
                    || value.All(char.IsLetter),
        "text" or "content" or "symbol" or "brush" or "thickness" or "grid-definitions"
            or "font-family" or "font-weight" or "font-style" or "text-decoration" => true,
        _ => true
    };

    private static void ValidateAccessibility(CuiComponent component, CuiDiagnosticBag diagnostics)
    {
        var hidden = IsTrue(component, "Hidden");
        if (!hidden) return;

        var focusable = IsTrue(component, "Focusable");
        if (focusable || InteractiveElements.Contains(component.Type))
        {
            diagnostics.Error("CUIA001",
                "An invisible interactive or focusable CUI element requires an explicit justified accessibility contract.",
                component.Span);
        }
    }

    private static bool IsTrue(CuiComponent component, string property) =>
        component.Properties.TryGetValue(property, out var value)
        && value is CuiLiteralValue literal
        && bool.TryParse(literal.Value, out var result)
        && result;

    private static bool IsFalse(CuiComponent component, string property) =>
        component.Properties.TryGetValue(property, out var value)
        && value is CuiLiteralValue literal
        && bool.TryParse(literal.Value, out var result)
        && !result;

    private static void ValidateTheme(string theme, CuiSourceSpan span, CuiDiagnosticBag diagnostics)
    {
        var supported = CakeOS.Cui.Themes.CuiThemeCatalog.All.Select(item => item.DisplayName)
            .Append("Default").ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!supported.Contains(theme))
        {
            var suggestion = Suggest(theme, supported);
            diagnostics.Error("CUIC016", suggestion is null
                ? $"Unknown CUI theme '{theme}'."
                : $"Unknown CUI theme '{theme}'. Did you mean '{suggestion}'?", span);
        }
    }

    private IEnumerable<string> _registryElementNames()
    {
        var names = new HashSet<string>(LanguageConstructs, StringComparer.OrdinalIgnoreCase);
        foreach (var element in _registry.Elements)
            if (IsCompactElement(element.Name)) names.Add(element.Name);
        return names;
    }

    private IEnumerable<string> _registryPropertyNames() => _registry.Properties.Select(property => property.Name);

    private static bool IsCompactElement(string name) =>
        new[] { "Page", "Container", "Text", "Image", "Audio", "Video", "Object", "Button", "Input", "Layer", "Anchor" }
            .Contains(name, StringComparer.OrdinalIgnoreCase);

    private static string? Suggest(string input, IEnumerable<string> candidates)
    {
        var nearest = candidates.Select(candidate => (Name: candidate, Distance: EditDistance(input, candidate)))
            .Where(candidate => candidate.Distance <= Math.Max(1, input.Length / 4))
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        return nearest.Length == 1 || nearest.Length == 2 && nearest[0].Distance < nearest[1].Distance
            ? nearest[0].Name
            : null;
    }

    private static int EditDistance(string first, string second)
    {
        first = first.ToUpperInvariant();
        second = second.ToUpperInvariant();
        var previous = Enumerable.Range(0, second.Length + 1).ToArray();
        var current = new int[second.Length + 1];
        for (var row = 1; row <= first.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= second.Length; column++)
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + (first[row - 1] == second[column - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[second.Length];
    }

    private IReadOnlyList<CuiCompiledNode> CompileNodes(IReadOnlyList<CuiComponent> components, string parentStableId)
    {
        var typeCounts = components.GroupBy(component => component.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var stableCounts = components.GroupBy(component => component.StableId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return Array.AsReadOnly(components.Select(component =>
        {
            var stableId = $"{parentStableId}/{IdentitySegment(component, typeCounts, stableCounts)}";
            var properties = new Dictionary<string, CuiCompiledProperty>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in component.Properties)
            {
                var languageType = _registry.TryGetProperty(name, out var descriptor)
                    ? descriptor.LanguageType
                    : "unresolved";
                properties[name] = new CuiCompiledProperty(name, languageType, value);
            }

            return new CuiCompiledNode(stableId, component.Type,
                new ReadOnlyDictionary<string, CuiCompiledProperty>(properties),
                CompileNodes(component.Children, stableId),
                CompileNodes(component.ElseChildren, stableId + "/else"),
                component.Span, component.IsDefinition);
        }).ToArray());
    }

    private static string IdentitySegment(
        CuiComponent component,
        IReadOnlyDictionary<string, int> typeCounts,
        IReadOnlyDictionary<string, int> stableCounts)
    {
        if (component.AuthoredId is not null)
            return $"id:{Uri.EscapeDataString(component.AuthoredId)}";
        if (typeCounts[component.Type] == 1)
            return $"type:{Uri.EscapeDataString(component.Type)}";
        if (stableCounts[component.StableId] == 1)
            return component.StableId;
        return $"ambiguous:{Uri.EscapeDataString(component.Type)}";
    }

    private static IReadOnlyList<CuiCompilerSourceMapEntry> BuildSourceMap(CuiDocument document)
    {
        var entries = new List<CuiCompilerSourceMapEntry>();
        void Add(IReadOnlyList<CuiComponent> components, string parentStableId)
        {
            var typeCounts = components.GroupBy(component => component.Type, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var stableCounts = components.GroupBy(component => component.StableId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            foreach (var component in components)
            {
                var segment = IdentitySegment(component, typeCounts, stableCounts);
                var stableId = $"{parentStableId}/{segment}";
                var unambiguous = component.AuthoredId is not null
                                  || typeCounts[component.Type] == 1
                                  || stableCounts[component.StableId] == 1;
                entries.Add(new CuiCompilerSourceMapEntry(stableId, component.Span, component.AuthoredId, unambiguous));
                Add(component.Children, stableId);
                Add(component.ElseChildren, stableId + "/else");
            }
        }
        Add(document.Components, "document");
        foreach (var template in document.Templates.Values)
            Add(template.Content, $"template:{template.Name}");
        return Array.AsReadOnly(entries.ToArray());
    }

    private static CuiDependencyGraph BuildDependencies(CuiDocument document, HashSet<string> definitions)
    {
        var entries = new List<KeyValuePair<string, IEnumerable<CuiCompilationDependency>>>();
        void Add(IReadOnlyList<CuiComponent> components, string parentStableId)
        {
            var typeCounts = components.GroupBy(component => component.Type, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var stableCounts = components.GroupBy(component => component.StableId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            foreach (var component in components)
            {
                var stableId = $"{parentStableId}/{IdentitySegment(component, typeCounts, stableCounts)}";
                var dependencies = new List<CuiCompilationDependency>();
                if (document.Templates.ContainsKey(component.Type))
                    dependencies.Add(new CuiCompilationDependency(CuiDependencyKind.Template, component.Type));
                else if (definitions.Contains(component.Type) && !component.IsDefinition)
                    dependencies.Add(new CuiCompilationDependency(CuiDependencyKind.Definition, component.Type));
                foreach (var value in component.Properties.Values)
                {
                    if (value is CuiResourceValue resource)
                        dependencies.Add(new CuiCompilationDependency(CuiDependencyKind.Resource, resource.Key));
                    if (value is CuiBindingValue binding)
                        dependencies.Add(new CuiCompilationDependency(CuiDependencyKind.Variable, binding.Path));
                }
                foreach (var action in component.Actions.Values)
                    dependencies.Add(new CuiCompilationDependency(CuiDependencyKind.Action, action.Name));
                entries.Add(new KeyValuePair<string, IEnumerable<CuiCompilationDependency>>(stableId, dependencies));
                Add(component.Children, stableId);
                Add(component.ElseChildren, stableId + "/else");
            }
        }
        Add(document.Components, "document");
        foreach (var template in document.Templates.Values)
            Add(template.Content, $"template:{template.Name}");
        return new CuiDependencyGraph(entries);
    }

    private static IEnumerable<CuiComponent> AllComponents(IEnumerable<CuiComponent> components)
    {
        foreach (var component in components)
        {
            yield return component;
            foreach (var child in AllComponents(component.Children)) yield return child;
            foreach (var child in AllComponents(component.ElseChildren)) yield return child;
        }
    }
}
