using CakeOS.Cui;
using CakeOS.Cui.Language;
using LanguageSpan = CakeOS.Cui.Language.CuiSourceSpan;
using CakeOS.Cui.Runtime;

namespace Haven.CUI.DevTools;

public sealed record CuiLinterRule(
    string Code,
    string Title,
    string RequirementId,
    string SpecLines);

public sealed record CuiLinterFinding(
    CuiLinterRule Rule,
    CuiDiagnostic Diagnostic,
    string? SuggestedCorrection);

public sealed record CuiLinterOptions
{
    public CuiLinterOptions(
        bool reportNestedComments = true,
        bool reportShorthandAmbiguity = true,
        bool reportCaseOnlySymbolCollisions = true,
        bool reportUninstantiatedDefinitions = true,
        bool reportImplicitCallerStateCapture = true,
        bool reportStaticPrefabMutableInput = true,
        bool reportMissingSemanticSelector = true)
    {
        ReportNestedComments = reportNestedComments;
        ReportShorthandAmbiguity = reportShorthandAmbiguity;
        ReportCaseOnlySymbolCollisions = reportCaseOnlySymbolCollisions;
        ReportUninstantiatedDefinitions = reportUninstantiatedDefinitions;
        ReportImplicitCallerStateCapture = reportImplicitCallerStateCapture;
        ReportStaticPrefabMutableInput = reportStaticPrefabMutableInput;
        ReportMissingSemanticSelector = reportMissingSemanticSelector;
    }

    public bool ReportNestedComments { get; }
    public bool ReportShorthandAmbiguity { get; }
    public bool ReportCaseOnlySymbolCollisions { get; }
    public bool ReportUninstantiatedDefinitions { get; }
    public bool ReportImplicitCallerStateCapture { get; }
    public bool ReportStaticPrefabMutableInput { get; }
    public bool ReportMissingSemanticSelector { get; }

    public static CuiLinterOptions Default { get; } = new();
}

/// <summary>
/// Source-level CUI linter. Every rule is derived from an authoritative specification
/// clause and reports file, line and column with a suggested correction where safe.
/// The linter does not replace compiler errors: it reports conditions the compiler cannot
/// decide on its own, such as symbols that are legal but ambiguous by case or underscore.
/// </summary>
public sealed class CuiLinter
{
    private static readonly CuiLinterRule NestedComment = new(
        "CUIL001", "Comments do not nest", "FW-011", "development-specification.txt 952-971");
    private static readonly CuiLinterRule ShorthandAmbiguity = new(
        "CUIL002", "Shorthand must not be ambiguous in the active resolution scope", "FW-011", "development-specification.txt 952-971");
    private static readonly CuiLinterRule CaseOnlySymbolCollision = new(
        "CUIL003", "User symbols are case-sensitive", "FW-011", "development-specification.txt 952-971");
    private static readonly CuiLinterRule UninstantiatedDefinition = new(
        "CUIL004", "A definition is not rendered merely because its declaration is present", "FW-012", "development-specification.txt 973-982");
    private static readonly CuiLinterRule ImplicitCallerStateCapture = new(
        "CUIL005", "A Prefab must not implicitly capture arbitrary caller state", "FW-012", "development-specification.txt 1012-1026");
    private static readonly CuiLinterRule StaticPrefabMutableInput = new(
        "CUIL006", "Static Prefabs reject conflicting per-instance mutable input", "FW-012", "development-specification.txt 1012-1026");
    private static readonly CuiLinterRule MissingSemanticSelector = new(
        "CUIL007", "Semantic targeting requires a stable selector", "FW-020", "development-specification.txt 1290-1304");

    private static readonly CuiControlRegistry Registry = CuiControlRegistry.Default;

    public IReadOnlyList<CuiLinterRule> Rules { get; } =
    [
        NestedComment, ShorthandAmbiguity, CaseOnlySymbolCollision, UninstantiatedDefinition,
        ImplicitCallerStateCapture, StaticPrefabMutableInput, MissingSemanticSelector
    ];

    private sealed record UserSymbol(string Name, string Kind, string Scope, LanguageSpan Span);

    public IReadOnlyList<CuiLinterFinding> Lint(string source, CuiDocument document, CuiLinterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(document);

        var settings = options ?? CuiLinterOptions.Default;
        var findings = new List<CuiLinterFinding>();

        if (settings.ReportNestedComments)
            findings.AddRange(LintNestedComments(source, document.SourceName));

        var symbols = CollectSymbols(document);

        if (settings.ReportShorthandAmbiguity)
            findings.AddRange(LintShorthandAmbiguity(symbols));

        if (settings.ReportCaseOnlySymbolCollisions)
            findings.AddRange(LintCaseOnlyCollisions(symbols));

        if (settings.ReportUninstantiatedDefinitions)
            findings.AddRange(LintUninstantiatedDefinitions(document, symbols));

        if (settings.ReportImplicitCallerStateCapture || settings.ReportStaticPrefabMutableInput)
            findings.AddRange(LintPrefabs(document, settings));

        if (settings.ReportMissingSemanticSelector)
            findings.AddRange(LintSemanticSelectors(document));

        return Array.AsReadOnly(findings
            .OrderBy(finding => finding.Diagnostic.Span.SourceName, StringComparer.Ordinal)
            .ThenBy(finding => finding.Diagnostic.Span.Start.Line)
            .ThenBy(finding => finding.Diagnostic.Span.Start.Column)
            .ToArray());
    }

    private static IEnumerable<CuiLinterFinding> LintNestedComments(string source, string sourceName)
    {
        var index = 0;
        while (index < source.Length)
        {
            if (!source.AsSpan(index).StartsWith("<#", StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            var end = source.IndexOf("#>", index + 2, StringComparison.Ordinal);
            var body = end < 0 ? source[(index + 2)..] : source[(index + 2)..end];
            var nested = body.IndexOf("<#", StringComparison.Ordinal);
            if (nested >= 0)
            {
                var offset = index + 2 + nested;
                yield return Finding(NestedComment, LanguageSpan.At(sourceName, LineOf(source, offset), ColumnOf(source, offset), 2),
                    "Remove the nested comment marker; CUI comments do not nest.");
            }

            if (end < 0) yield break;
            index = end + 2;
        }
    }

    private static List<UserSymbol> CollectSymbols(CuiDocument document)
    {
        var result = new List<UserSymbol>();

        foreach (var template in document.Templates.Values)
            result.Add(new UserSymbol(template.Name, "template", "document", template.Span));

        foreach (var resource in document.Resources.Values)
            result.Add(new UserSymbol(resource.Key, "resource", "document", resource.Span));

        void Walk(CuiComponent component, string scope)
        {
            var declaringScope = component.IsDefinition ? (component.Name ?? component.Type) : scope;

            if (component.IsDefinition)
                result.Add(new UserSymbol(component.Name ?? component.Type, "definition", scope, component.Span));

            foreach (var group in component.Groups)
                result.Add(new UserSymbol(group, "group", declaringScope, component.Span));

            foreach (var child in component.Children)
                Walk(child, declaringScope);
            foreach (var child in component.ElseChildren)
                Walk(child, declaringScope);
        }

        foreach (var component in document.Components)
            Walk(component, "document");

        return result;
    }    private static IEnumerable<CuiLinterFinding> LintShorthandAmbiguity(IEnumerable<UserSymbol> symbols)
    {
        foreach (var group in symbols.GroupBy(symbol => ScopeKey(symbol.Scope, symbol.Name.Replace('_', ' ')), StringComparer.Ordinal))
        {
            var underscored = group.Where(symbol => symbol.Name.Contains('_', StringComparison.Ordinal)).ToArray();
            var spaced = group.Where(symbol => symbol.Name.Contains(' ', StringComparison.Ordinal)).ToArray();
            if (underscored.Length == 0 || spaced.Length == 0) continue;

            foreach (var symbol in underscored)
                yield return Finding(ShorthandAmbiguity, symbol.Span,
                    $"Shorthand value '{symbol.Name}' is ambiguous because '{symbol.Name.Replace('_', ' ')}' is also in scope. Use a quoted value or rename one symbol.");
        }
    }

    private static IEnumerable<CuiLinterFinding> LintCaseOnlyCollisions(IEnumerable<UserSymbol> symbols)
    {
        foreach (var group in symbols.GroupBy(symbol => ScopeKey(symbol.Scope, symbol.Name.ToUpperInvariant()), StringComparer.Ordinal))
        {
            var distinct = group.Select(symbol => symbol.Name).Distinct(StringComparer.Ordinal).ToArray();
            if (distinct.Length < 2) continue;

            foreach (var symbol in group)
                yield return Finding(CaseOnlySymbolCollision, symbol.Span,
                    $"User symbol '{symbol.Name}' differs only by case from {string.Join(", ", distinct.Where(name => name != symbol.Name).Select(name => $"'{name}'"))}. Rename one symbol or make the difference explicit.");
        }
    }

    private static IEnumerable<CuiLinterFinding> LintUninstantiatedDefinitions(CuiDocument document, IEnumerable<UserSymbol> symbols)
    {
        var used = document.Components
            .SelectMany(component => component.DescendantsAndSelf())
            .Where(component => !component.IsDefinition)
            .Select(component => component.Type)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var symbol in symbols.Where(item => item.Kind is "definition" or "template"))
        {
            if (used.Contains(symbol.Name)) continue;
            yield return Finding(UninstantiatedDefinition, symbol.Span,
                $"Definition '{symbol.Name}' is declared but never instantiated. Remove it, or instantiate it where it is intended to render.");
        }
    }

    private static IEnumerable<CuiLinterFinding> LintPrefabs(CuiDocument document, CuiLinterOptions settings)
    {
        var prefabs = document.Components
            .SelectMany(component => component.DescendantsAndSelf())
            .Where(component => component.IsDefinition
                                && string.Equals(component.Type, "Prefab", StringComparison.OrdinalIgnoreCase));

        foreach (var prefab in prefabs)
        {
            prefab.TryGetLiteralAttribute("Type", out var prefabType);
            var isStatic = string.Equals(prefabType, "static", StringComparison.OrdinalIgnoreCase);
            var inputs = prefab.Children
                .SelectMany(component => component.DescendantsAndSelf())
                .Where(component => component.IsDefinition && string.Equals(component.Type, "Variable", StringComparison.OrdinalIgnoreCase))
                .Select(component => component.Name ?? component.Type)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var component in prefab.Children.SelectMany(item => item.DescendantsAndSelf()))
            foreach (var (name, value) in component.Properties)
            {
                if (value is not CuiBindingValue binding) continue;

                if (settings.ReportImplicitCallerStateCapture && !inputs.Contains(binding.Path))
                {
                    yield return Finding(ImplicitCallerStateCapture, binding.Span,
                        $"Prefab '{prefab.Name ?? prefab.Type}' binds '{name}' to '{binding.Path}', which is not a declared input. Declare it as a Variable Definition or pass it explicitly.");
                }

                if (settings.ReportStaticPrefabMutableInput && isStatic && inputs.Contains(binding.Path) && binding.Mode == CuiBindingMode.TwoWay)
                {
                    yield return Finding(StaticPrefabMutableInput, binding.Span,
                        $"Static Prefab '{prefab.Name ?? prefab.Type}' binds declared input '{binding.Path}' two-way, which conflicts with shared state. Use a dynamic Prefab or a one-way input.");
                }
            }
        }
    }

    private static IEnumerable<CuiLinterFinding> LintSemanticSelectors(CuiDocument document)
    {
        foreach (var component in document.Components.SelectMany(item => item.DescendantsAndSelf()))
        {
            if (component.Actions.Count == 0 || component.Name is not null) continue;

            var hasRole = HasCanonicalProperty(component, "Role");
            var hasAccessibleName = HasCanonicalProperty(component, "AccessibleName");
            if (hasRole && hasAccessibleName) continue;

            yield return Finding(MissingSemanticSelector, component.Span,
                $"Element '{component.Type}' exposes an action but has no id, role or accessible name, so automated tests fall back to raw coordinates. Add an id, and a role or accessible name where the default role is not sufficient.");
        }
    }

    private static bool HasCanonicalProperty(CuiComponent component, string canonicalName) =>
        component.Properties.Any(pair =>
            Registry.TryGetProperty(pair.Key, out var descriptor)
            && string.Equals(descriptor.Name, canonicalName, StringComparison.Ordinal));

    private static CuiLinterFinding Finding(CuiLinterRule rule, LanguageSpan span, string suggestion) =>
        new(rule, new CuiDiagnostic(rule.Code, CuiDiagnosticSeverity.Warning, suggestion, span), suggestion);

    private static string ScopeKey(string scope, string name) => $"{scope}\u001f{name}";

    private static int LineOf(string source, int offset)
    {
        var line = 1;
        for (var index = 0; index < offset && index < source.Length; index++)
            if (source[index] == '\n') line++;
        return line;
    }

    private static int ColumnOf(string source, int offset)
    {
        var column = 1;
        for (var index = 0; index < offset && index < source.Length; index++)
        {
            if (source[index] == '\n') column = 1;
            else column++;
        }
        return column;
    }
}