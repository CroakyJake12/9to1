using CakeOS.Cui;
using CakeOS.Cui.Compiler;

namespace Haven.CUI.DevTools;

public sealed record CuiBindingDependency(
    ElementId ElementId,
    string Property,
    string Path,
    CuiBindingMode Mode,
    string? Fallback,
    bool IsDeclaredSymbol,
    string? DeclaredSymbolKind);

public sealed record CuiSymbolDependency(
    string Symbol,
    string Kind,
    IReadOnlyList<ElementId> Dependents);

public sealed class CuiDependencyInspection
{
    public CuiDependencyInspection(
        IEnumerable<CuiBindingDependency> bindings,
        IEnumerable<CuiSymbolDependency> symbols,
        IEnumerable<KeyValuePair<string, IReadOnlyList<CuiCompilationDependency>>> compilerGraph)
    {
        Bindings = DiagnosticsCollections.Copy(bindings);
        Symbols = DiagnosticsCollections.Copy(symbols);
        CompilerGraph = DiagnosticsCollections.Copy<string, IReadOnlyList<CuiCompilationDependency>>(compilerGraph);
    }

    public IReadOnlyList<CuiBindingDependency> Bindings { get; }

    public IReadOnlyList<CuiSymbolDependency> Symbols { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<CuiCompilationDependency>> CompilerGraph { get; }

    public IReadOnlyList<string> UnresolvedPaths =>
        Array.AsReadOnly(Bindings.Where(binding => !binding.IsDeclaredSymbol)
            .Select(binding => binding.Path)
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

        foreach (var component in document.Components.SelectMany(item => item.DescendantsAndSelf()))
        foreach (var (name, value) in component.Properties)
        {
            if (value is not CuiBindingValue binding) continue;
            bindings.Add(new CuiBindingDependency(
                new ElementId(component.StableId), name, binding.Path, binding.Mode, binding.Fallback,
                symbols.TryGetValue(binding.Path, out var kind), kind));
        }

        var dependents = new Dictionary<string, IReadOnlyList<ElementId>>(StringComparer.Ordinal);
        foreach (var group in bindings.GroupBy(binding => binding.Path, StringComparer.Ordinal))
        {
            dependents[group.Key] = Array.AsReadOnly(group.Select(item => item.ElementId)
                .Distinct()
                .OrderBy(item => item.Value, StringComparer.Ordinal)
                .ToArray());
        }

        var symbolRows = symbols
            .Select(pair => new CuiSymbolDependency(pair.Key, pair.Value, dependents.GetValueOrDefault(pair.Key) ?? Array.Empty<ElementId>()))
            .OrderBy(row => row.Symbol, StringComparer.Ordinal)
            .ToArray();

        return new CuiDependencyInspection(bindings, symbolRows, compiled.Dependencies.Entries);
    }

    private static Dictionary<string, string> CollectSymbols(CakeOS.Cui.CuiDocument document)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in document.Templates.Keys) result[key] = "template";
        foreach (var key in document.Resources.Keys) result[key] = "resource";

        foreach (var component in document.Components.SelectMany(item => item.DescendantsAndSelf()))
            if (component.IsDefinition) result[component.Name ?? component.Type] = component.Type;

        return result;
    }
}