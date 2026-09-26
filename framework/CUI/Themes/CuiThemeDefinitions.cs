namespace CakeOS.Cui.Themes;

/// <summary>
/// One custom theme rule. A null TypeName targets the element's ordinary
/// Definition; a non-null TypeName targets that element Type.
/// </summary>
public sealed record CuiThemeElementRule(
    string ElementName,
    string? TypeName,
    IReadOnlyDictionary<string, object?> Properties);

/// <summary>A custom theme parsed from a .cui-theme source file.</summary>
public sealed record CuiThemeDefinition(
    string Name,
    string Inherits,
    IReadOnlyList<CuiThemeElementRule> Rules);

/// <summary>Stable lookup key for inherited element and element-Type defaults.</summary>
public readonly record struct CuiThemeRuleKey(string ElementName, string? TypeName);

/// <summary>Custom-theme definition failure with a stable diagnostic code.</summary>
public sealed class CuiThemeDefinitionException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Per-compilation theme definition catalog. Resolution inherits built-in
/// structural tokens and merges custom element properties from base to leaf.
/// It never changes global CUI theme or palette state.
/// </summary>
public sealed class CuiThemeDefinitionCatalog
{
    private readonly Dictionary<string, CuiThemeDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public void Add(CuiThemeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        lock (_gate)
        {
            var name = RequireName(definition.Name, nameof(definition.Name));
            var inherits = RequireName(definition.Inherits, nameof(definition.Inherits));
            if (CuiThemeCatalog.All.Any(theme => string.Equals(theme.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
                throw new CuiThemeDefinitionException("CUI_THEME_BUILTIN_SHADOW", $"Custom theme '{name}' cannot shadow a built-in theme.");
            if (_definitions.ContainsKey(name))
                throw new CuiThemeDefinitionException("CUI_THEME_DUPLICATE", $"Custom theme '{name}' is already defined.");

            var rules = definition.Rules ?? throw new ArgumentNullException(nameof(definition.Rules));
            foreach (var rule in rules)
                ValidateRule(rule);

            _definitions.Add(name, definition with
            {
                Name = name,
                Inherits = inherits,
                Rules = rules.Select(rule => rule with
                {
                    ElementName = rule.ElementName.Trim(),
                    TypeName = string.IsNullOrWhiteSpace(rule.TypeName) ? null : rule.TypeName.Trim(),
                    Properties = rule.Properties.ToDictionary(
                        property => RequireName(property.Key, "property name"),
                        property => property.Value,
                        StringComparer.OrdinalIgnoreCase)
                }).ToArray()
            });
        }
    }

    public CuiResolvedTheme Resolve(string name)
    {
        var themeName = RequireName(name, nameof(name));
        lock (_gate)
            return ResolveCore(themeName, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    public bool TryResolve(string name, out CuiResolvedTheme? theme)
    {
        try
        {
            theme = Resolve(name);
            return true;
        }
        catch (Exception exception) when (exception is CuiThemeDefinitionException or ArgumentException)
        {
            theme = null;
            return false;
        }
    }

    private CuiResolvedTheme ResolveCore(string name, HashSet<string> path)
    {
        var builtIn = CuiThemeCatalog.All.FirstOrDefault(
            candidate => string.Equals(candidate.DisplayName, name, StringComparison.OrdinalIgnoreCase));
        if (builtIn is not null)
            return new CuiResolvedTheme(builtIn.DisplayName, builtIn.Theme, builtIn, EmptyRules());

        if (!_definitions.TryGetValue(name, out var definition))
            throw new CuiThemeDefinitionException("CUI_THEME_UNKNOWN_BASE", $"Theme '{name}' is not a built-in or declared custom theme.");
        if (!path.Add(definition.Name))
            throw new CuiThemeDefinitionException("CUI_THEME_INHERITANCE_CYCLE", $"Theme inheritance contains a cycle at '{definition.Name}'.");

        try
        {
            var parent = ResolveCore(definition.Inherits, path);
            var rules = CloneRules(parent.Rules);
            foreach (var rule in definition.Rules)
            {
                var key = new CuiThemeRuleKey(rule.ElementName, rule.TypeName);
                if (!rules.TryGetValue(key, out var inherited))
                {
                    inherited = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    rules.Add(key, inherited);
                }
                foreach (var property in rule.Properties)
                    inherited[property.Key] = property.Value;
            }

            var expression = parent.Expression with
            {
                DisplayName = definition.Name,
                Description = $"Custom theme based on {parent.Name}."
            };
            return new CuiResolvedTheme(definition.Name, parent.BaseTheme, expression, FreezeRules(rules));
        }
        finally
        {
            path.Remove(definition.Name);
        }
    }

    private static Dictionary<CuiThemeRuleKey, Dictionary<string, object?>> CloneRules(
        IReadOnlyDictionary<CuiThemeRuleKey, IReadOnlyDictionary<string, object?>> source)
    {
        var result = new Dictionary<CuiThemeRuleKey, Dictionary<string, object?>>(ThemeRuleKeyComparer.Instance);
        foreach (var (key, properties) in source)
            result.Add(key, new Dictionary<string, object?>(properties, StringComparer.OrdinalIgnoreCase));
        return result;
    }

    private static IReadOnlyDictionary<CuiThemeRuleKey, IReadOnlyDictionary<string, object?>> FreezeRules(
        Dictionary<CuiThemeRuleKey, Dictionary<string, object?>> source)
    {
        var result = new Dictionary<CuiThemeRuleKey, IReadOnlyDictionary<string, object?>>(ThemeRuleKeyComparer.Instance);
        foreach (var (key, properties) in source)
            result.Add(key, new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(properties));
        return new System.Collections.ObjectModel.ReadOnlyDictionary<CuiThemeRuleKey, IReadOnlyDictionary<string, object?>>(result);
    }

    private static IReadOnlyDictionary<CuiThemeRuleKey, IReadOnlyDictionary<string, object?>> EmptyRules() =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<CuiThemeRuleKey, IReadOnlyDictionary<string, object?>>(
            new Dictionary<CuiThemeRuleKey, IReadOnlyDictionary<string, object?>>(ThemeRuleKeyComparer.Instance));

    private static void ValidateRule(CuiThemeElementRule? rule)
    {
        if (rule is null)
            throw new CuiThemeDefinitionException("CUI_THEME_INVALID_RULE", "A theme rule cannot be null.");
        _ = RequireName(rule.ElementName, nameof(rule.ElementName));
        if (rule.TypeName is not null)
            _ = RequireName(rule.TypeName, nameof(rule.TypeName));
        ArgumentNullException.ThrowIfNull(rule.Properties);
        if (rule.Properties.Count == 0)
            throw new CuiThemeDefinitionException("CUI_THEME_EMPTY_RULE", $"Theme rule for '{rule.ElementName}' has no properties.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in rule.Properties)
        {
            var name = RequireName(key, "property name");
            if (!names.Add(name))
                throw new CuiThemeDefinitionException("CUI_THEME_DUPLICATE_PROPERTY", $"Theme rule for '{rule.ElementName}' contains duplicate property '{name}'.");
            if (value is null)
                throw new CuiThemeDefinitionException("CUI_THEME_NULL_PROPERTY", $"Theme property '{key}' on '{rule.ElementName}' cannot be null.");
        }
    }

    private static string RequireName(string? value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException("Theme and property names must be non-empty.", parameterName);

    private sealed class ThemeRuleKeyComparer : IEqualityComparer<CuiThemeRuleKey>
    {
        public static ThemeRuleKeyComparer Instance { get; } = new();

        public bool Equals(CuiThemeRuleKey x, CuiThemeRuleKey y) =>
            string.Equals(x.ElementName, y.ElementName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.TypeName, y.TypeName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(CuiThemeRuleKey key) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(key.ElementName),
            key.TypeName is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(key.TypeName));
    }
}

/// <summary>Resolved built-in base plus immutable effective custom rules.</summary>
public sealed record CuiResolvedTheme(
    string Name,
    CuiTheme BaseTheme,
    CuiThemeExpression Expression,
    IReadOnlyDictionary<CuiThemeRuleKey, IReadOnlyDictionary<string, object?>> Rules);
