using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;

namespace Haven.CUI.DevTools;

public sealed record CuiBindingDependency(
    ElementId ElementId,
    string Property,
    string Path,
    CuiBindingMode Mode,
    string? Fallback,
    bool IsDeclaredSymbol,
    string? DeclaredSymbolKind)
{
    public string? DeclaredSymbol { get; init; }
}

public sealed record CuiSymbolDependency(
    string Symbol,
    string Kind,
    IReadOnlyList<ElementId> Dependents);

/// <summary>A document variable's one-time candidate-source route, which is a typed binding path
/// with selection semantics rather than an ordinary property binding mode.</summary>
public sealed record CuiVariableSourceDependency(
    ElementId ElementId,
    string Variable,
    string Path,
    string? Selection,
    bool IsDeclaredSymbol,
    string? DeclaredSymbolKind)
{
    public bool RequiresHostBindingContext { get; init; }
}

public sealed class CuiDependencyInspection
{
    public CuiDependencyInspection(
        IEnumerable<CuiBindingDependency> bindings,
        IEnumerable<CuiSymbolDependency> symbols,
        IEnumerable<KeyValuePair<string, IReadOnlyList<CuiCompilationDependency>>> compilerGraph,
        IEnumerable<CuiVariableSourceDependency>? variableSources = null)
    {
        Bindings = DiagnosticsCollections.Copy(bindings);
        Symbols = DiagnosticsCollections.Copy(symbols);
        CompilerGraph = DiagnosticsCollections.Copy<string, IReadOnlyList<CuiCompilationDependency>>(compilerGraph);
        VariableSources = DiagnosticsCollections.Copy(variableSources);
    }

    public IReadOnlyList<CuiBindingDependency> Bindings { get; }

    public IReadOnlyList<CuiSymbolDependency> Symbols { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<CuiCompilationDependency>> CompilerGraph { get; }

    public IReadOnlyList<CuiVariableSourceDependency> VariableSources { get; }

    public IReadOnlyList<string> UnresolvedPaths =>
        Array.AsReadOnly(Bindings.Where(binding => !binding.IsDeclaredSymbol).Select(binding => binding.Path)
            .Concat(VariableSources.Where(source => !source.IsDeclaredSymbol && !source.RequiresHostBindingContext)
                .Select(source => source.Path))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());

    public IReadOnlyList<string> UnusedSymbols =>
        Array.AsReadOnly(Symbols.Where(symbol => symbol.Dependents.Count == 0)
            .Select(symbol => symbol.Symbol)
            .Order(StringComparer.Ordinal)
            .ToArray());

    public IReadOnlyList<CuiBindingDependency> BindingsOn(ElementId elementId) =>
        Array.AsReadOnly(Bindings.Where(binding => binding.ElementId == elementId)
            .OrderBy(binding => binding.Property, StringComparer.Ordinal)
            .ToArray());

    public IReadOnlyList<CuiVariableSourceDependency> VariableSourcesOn(ElementId elementId) =>
        Array.AsReadOnly(VariableSources.Where(source => source.ElementId == elementId)
            .OrderBy(source => source.Variable, StringComparer.Ordinal)
            .ToArray());

    public IReadOnlyList<ElementId> DependentsOf(string symbol) =>
        Symbols.FirstOrDefault(item => string.Equals(item.Symbol, symbol, StringComparison.Ordinal))?.Dependents
        ?? Array.Empty<ElementId>();
}

/// <summary>
/// Binding and variable dependency inspection over a compiled CUI document. It reports the
/// declared symbols, which nodes depend on them, and which binding paths are not declared
/// in the current scope.
/// </summary>
public sealed class CuiBindingDependencyInspector
{
    public CuiDependencyInspection Inspect(CuiCompiledDocument compiled)
    {
        ArgumentNullException.ThrowIfNull(compiled);

        var document = compiled.Semantics;
        var symbols = CollectSymbols(document);
        var bindings = new List<CuiBindingDependency>();
        var variableSources = new List<CuiVariableSourceDependency>();

        void AddValue(ElementId elementId, string property, CuiValue? value,
            IReadOnlyDictionary<string, string>? localSymbols = null)
        {
            if (value is CuiBindingValue binding)
            {
                var declaredSymbol = ResolveDeclaredSymbol(binding.Path, symbols, localSymbols, out var kind);
                bindings.Add(new CuiBindingDependency(
                    elementId, property, binding.Path, binding.Mode, binding.Fallback,
                    declaredSymbol is not null, kind) { DeclaredSymbol = declaredSymbol });
            }
        }

        void AddCondition(ElementId elementId, string property, CuiCondition? condition,
            IReadOnlyDictionary<string, string>? localSymbols = null) =>
            AddValue(elementId, property, condition?.Test, localSymbols);

        void AddComponent(ElementId elementId, CuiComponent component,
            IReadOnlyDictionary<string, string>? localSymbols = null)
        {
            foreach (var (name, value) in component.Properties)
                AddValue(elementId, name, value, localSymbols);
            foreach (var part in component.TextParts)
                if (part is CuiExpressionTextPart expression)
                    AddValue(elementId, "TextParts", expression.Value, localSymbols);
            AddCondition(elementId, "Condition", component.Condition, localSymbols);
            if (component.List is { } list) AddValue(elementId, "Items", list.Items, localSymbols);
            if (component.Repeat is { } repeat)
            {
                AddValue(elementId, "Repeat.Source", repeat.Source, localSymbols);
                var itemSymbols = ExtendSymbols(localSymbols, repeat.ItemName,
                    $"repeat:{elementId.Value}:{repeat.ItemName}", "repeat item", symbols);
                AddValue(elementId, "Repeat.Key", repeat.Key, itemSymbols);
                AddValue(elementId, "Repeat.Template", repeat.Template, itemSymbols);
            }
            if (component.PropertyRegion is { } region)
                AddValue(elementId, $"PropertyRegion.{region.PropertyName}", region.Value, localSymbols);
        }

        void Walk(
            IReadOnlyList<CuiCompiledNode> nodes,
            IReadOnlyList<CuiComponent> components,
            IReadOnlyDictionary<string, string>? localSymbols = null)
        {
            if (nodes.Count != components.Count)
                throw new InvalidOperationException("Compiled CUI nodes do not match their retained semantic components.");

            for (var index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index];
                var component = components[index];
                var elementId = new ElementId(node.StableId);
                var componentSymbols = component.IsDefinition
                    ? DefinitionInputs(elementId.Value, component, localSymbols, symbols)
                    : localSymbols;
                if (!component.IsDefinition && component.Type.Equals("Page", StringComparison.Ordinal))
                    componentSymbols = ScopeVariableDefinitions($"page:{elementId.Value}",
                        component.Children.Concat(component.ElseChildren).ToArray(), componentSymbols,
                        "page variable", symbols);
                AddComponent(elementId, component, componentSymbols);
                var childSymbols = componentSymbols;
                if (!component.IsDefinition && component.Repeat is { } repeat)
                {
                    childSymbols = ExtendSymbols(childSymbols, repeat.ItemName,
                        $"repeat:{elementId.Value}:{repeat.ItemName}", "repeat item", symbols);
                    childSymbols = ScopeVariableDefinitions($"repeat:{elementId.Value}",
                        component.Children.Concat(component.ElseChildren).ToArray(), childSymbols,
                        "repeat variable", symbols);
                }
                Walk(node.Children, component.Children, childSymbols);
                Walk(node.ElseChildren, component.ElseChildren, childSymbols);
            }
        }

        Walk(compiled.Components, document.Components);
        foreach (var (name, template) in compiled.Templates)
        {
            if (!document.Templates.TryGetValue(name, out var semanticTemplate))
                throw new InvalidOperationException($"Compiled CUI template '{name}' has no retained semantic definition.");
            var localSymbols = template.Parameters.ToDictionary(parameter => parameter,
                parameter => $"template:{name}.{parameter}", StringComparer.Ordinal);
            foreach (var symbol in localSymbols.Values) symbols[symbol] = "template parameter";
            localSymbols = TemplateDefinitionInputs(name, semanticTemplate.Content, localSymbols, symbols);
            Walk(template.Content, semanticTemplate.Content, localSymbols);
        }

        foreach (var (name, action) in document.Actions)
        {
            var elementId = new ElementId($"action:{name}");
            AddValue(elementId, "Parameter", action.Parameter);
            var sets = action.Sets ?? Array.Empty<CuiSetDirective>();
            for (var index = 0; index < sets.Count; index++)
            {
                AddValue(elementId, $"Set[{index}].Value", sets[index].Value);
                AddCondition(elementId, $"Set[{index}].When", sets[index].When);
            }
        }

        for (var index = 0; index < document.Effects.Count; index++)
            AddCondition(new ElementId($"effect:{document.Effects[index].Name}#{index}"),
                "Condition", document.Effects[index].Condition);

        foreach (var style in document.Styles)
        foreach (var setter in style.Setters)
            AddValue(new ElementId($"style:{style.Selector}"), setter.Property, setter.Value);

        foreach (var (name, value) in document.RootProperties)
            AddValue(new ElementId("document"), name, value);

        foreach (var (name, variable) in document.Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Source)) continue;
            variableSources.Add(new CuiVariableSourceDependency(
                new ElementId($"variable:{name}"), name, variable.Source, variable.Selection,
                IsDeclaredSymbol: false, DeclaredSymbolKind: "external binding context")
                { RequiresHostBindingContext = true });
        }

        var dependents = new Dictionary<string, List<ElementId>>(StringComparer.Ordinal);
        void AddDependent(string path, ElementId elementId)
        {
            if (!dependents.TryGetValue(path, out var list))
            {
                list = [];
                dependents[path] = list;
            }
            list.Add(elementId);
        }

        foreach (var binding in bindings)
            if (binding.DeclaredSymbol is { } symbol)
                AddDependent(symbol, binding.ElementId);
        var symbolRows = symbols
            .Select(pair => new CuiSymbolDependency(pair.Key, pair.Value,
                Array.AsReadOnly((dependents.GetValueOrDefault(pair.Key) ?? []).Distinct()
                    .OrderBy(item => item.Value, StringComparer.Ordinal).ToArray())))
            .OrderBy(row => row.Symbol, StringComparer.Ordinal)
            .ToArray();

        return new CuiDependencyInspection(bindings, symbolRows, compiled.Dependencies.Entries, variableSources);
    }

    private static IReadOnlyDictionary<string, string> ExtendSymbols(
        IReadOnlyDictionary<string, string>? current,
        string localName,
        string symbol,
        string kind,
        IDictionary<string, string> symbols)
    {
        var result = current is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(current, StringComparer.Ordinal);
        result[localName] = symbol;
        symbols[symbol] = kind;
        return result;
    }

    private static IReadOnlyDictionary<string, string> DefinitionInputs(
        string definitionIdentity,
        CuiComponent definition,
        IReadOnlyDictionary<string, string>? current,
        IDictionary<string, string> symbols) =>
        ScopeVariableDefinitions($"definition:{definitionIdentity}",
            definition.Children.Concat(definition.ElseChildren).ToArray(), current,
            "definition input", symbols);

    private static Dictionary<string, string> TemplateDefinitionInputs(
        string templateName,
        IReadOnlyList<CuiComponent> components,
        IReadOnlyDictionary<string, string> parameters,
        IDictionary<string, string> symbols) =>
        new Dictionary<string, string>(ScopeVariableDefinitions($"template:{templateName}",
            components, parameters, "template variable", symbols), StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> ScopeVariableDefinitions(
        string scopeIdentity,
        IReadOnlyList<CuiComponent> components,
        IReadOnlyDictionary<string, string>? current,
        string kind,
        IDictionary<string, string> symbols)
    {
        var result = current is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(current, StringComparer.Ordinal);

        void Collect(IReadOnlyList<CuiComponent> items)
        {
            foreach (var component in items)
            {
                if (component.IsDefinition)
                {
                    if (component.Type.Equals("Variable", StringComparison.OrdinalIgnoreCase)
                        && component.Name is { } name)
                    {
                        var symbol = $"{scopeIdentity}.variable:{name}";
                        result[name] = symbol;
                        symbols[symbol] = kind;
                    }

                    // Nested reusable declarations own a separate lexical scope.
                    continue;
                }

                if (component.Type is "Page" or "Repeat") continue;
                Collect(component.Children);
                Collect(component.ElseChildren);
            }
        }

        Collect(components);
        return result;
    }

    private static string? ResolveDeclaredSymbol(
        string path,
        IReadOnlyDictionary<string, string> symbols,
        out string? kind) => ResolveDeclaredSymbol(path, symbols, null, out kind);

    private static string? ResolveDeclaredSymbol(
        string path,
        IReadOnlyDictionary<string, string> symbols,
        IReadOnlyDictionary<string, string>? localSymbols,
        out string? kind)
    {
        if (localSymbols is not null)
        {
            if (localSymbols.TryGetValue(path, out var exactLocal))
            {
                symbols.TryGetValue(exactLocal, out kind);
                return exactLocal;
            }

            var localRoot = path.Split('.')[0];
            if (!string.Equals(localRoot, path, StringComparison.Ordinal)
                && localSymbols.TryGetValue(localRoot, out var rootLocal))
            {
                symbols.TryGetValue(rootLocal, out kind);
                return rootLocal;
            }
        }

        if (symbols.TryGetValue(path, out kind)) return path;

        var segments = path.Split('.');
        var root = segments[0];
        if (segments.Length > 1 && segments.All(segment => segment.Length > 0)
            && symbols.TryGetValue(root, out kind)
            && string.Equals(kind, "variable", StringComparison.Ordinal))
            return root;

        kind = null;
        return null;
    }

    private static Dictionary<string, string> CollectSymbols(CakeOS.Cui.CuiDocument document)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in document.Variables.Keys) result[key] = "variable";
        return result;
    }
}
