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
    private readonly Dictionary<string, CuiCustomTheme> _custom = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ephemeralIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CuiResolvedTheme> _resolvedCustom = new(StringComparer.Ordinal);
    public event EventHandler? Changed;
    public IReadOnlyList<string> Names
    {
        get { lock (_gate) return Array.AsReadOnly(CuiThemeCatalog.All.Select(item => item.DisplayName)
            .Concat(_definitions.Keys).Concat(_custom.Values.Where(item => !_ephemeralIds.Contains(item.Id)).Select(item => item.Name)).ToArray()); }
    }

    /// <summary>Atomically replaces one validated account/draft definition. No account storage is written.</summary>
    public void UpsertCustomTheme(CuiCustomTheme theme, bool ephemeral = false)
    {
        var frozen = theme.Validate();
        lock (_gate)
        {
            if (_definitions.ContainsKey(frozen.Name) || _custom.Values.Any(item => item.Id != frozen.Id &&
                string.Equals(item.Name, frozen.Name, StringComparison.OrdinalIgnoreCase)))
                throw new CuiThemeDefinitionException("CUI_THEME_DUPLICATE", "Another theme already uses this name.");
            if (_custom.ContainsKey(frozen.Id) && _ephemeralIds.Contains(frozen.Id) != ephemeral)
                throw new CuiThemeDefinitionException("CUI_THEME_ID_CUSTODY", "A preview and an account theme cannot share one ID.");
            if (_custom.TryGetValue(frozen.Id, out var previous) && CuiCustomThemeContract.Serialize(previous) == CuiCustomThemeContract.Serialize(frozen)) return;
            _custom[frozen.Id] = frozen;
            if (ephemeral) _ephemeralIds.Add(frozen.Id);
            _resolvedCustom.Remove(frozen.Id);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool RemoveCustomTheme(string id)
    {
        bool removed;
        lock (_gate) { removed = _custom.Remove(id); if (removed) { _resolvedCustom.Remove(id); _ephemeralIds.Remove(id); } }
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    /// <summary>Reconcile only the previous account-owned IDs, retaining independent local draft themes.</summary>
    public void ReplaceAccountCustomThemes(IReadOnlyList<CuiCustomTheme> themes, IReadOnlyCollection<string> previousIds)
    {
        var values = CuiCustomThemeContract.DeserializeLibrary(CuiCustomThemeContract.SerializeLibrary(themes));
        var changed = false;
        lock (_gate)
        {
            var next = new Dictionary<string, CuiCustomTheme>(_custom, StringComparer.Ordinal);
            foreach (var id in previousIds) next.Remove(id);
            foreach (var value in values)
            {
                if (next.ContainsKey(value.Id))
                    throw new CuiThemeDefinitionException("CUI_THEME_ID_CUSTODY", "An account theme cannot replace an independent local definition.");
                if (_definitions.ContainsKey(value.Name) || next.Values.Any(item => item.Id != value.Id &&
                    string.Equals(item.Name, value.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new CuiThemeDefinitionException("CUI_THEME_DUPLICATE", "Another theme already uses this name.");
                next[value.Id] = value;
            }
            changed = _custom.Count != next.Count || next.Any(entry => !_custom.TryGetValue(entry.Key, out var old) ||
                CuiCustomThemeContract.Serialize(old) != CuiCustomThemeContract.Serialize(entry.Value));
            if (changed) { _custom.Clear(); _resolvedCustom.Clear(); foreach (var entry in next) _custom.Add(entry.Key, entry.Value); }
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Add(CuiThemeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        lock (_gate)
        {
            var name = RequireName(definition.Name, nameof(definition.Name));
            var inherits = RequireName(definition.Inherits, nameof(definition.Inherits));
            if (CuiThemeCatalog.All.Any(theme => string.Equals(theme.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
                throw new CuiThemeDefinitionException("CUI_THEME_BUILTIN_SHADOW", $"Custom theme '{name}' cannot shadow a built-in theme.");
            if (_definitions.ContainsKey(name) || _custom.Values.Any(theme => string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase)))
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

        var custom = _custom.Values.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (custom is not null)
        {
            if (_resolvedCustom.TryGetValue(custom.Id, out var cached)) return cached;
            var expression = CuiThemeCatalog.Resolve(custom.BaseTheme) with { DisplayName = custom.Name, Description = "Account custom theme." };
            var json = CuiCustomThemeContract.Serialize(custom);
            var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
            var resolved = new CuiResolvedTheme(custom.Name, custom.BaseTheme, expression, EmptyRules(), custom, fingerprint);
            _resolvedCustom.Add(custom.Id, resolved);
            return resolved;
        }

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
            return new CuiResolvedTheme(definition.Name, parent.BaseTheme, expression, FreezeRules(rules), parent.CustomTheme, parent.CustomFingerprint);
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
    IReadOnlyDictionary<CuiThemeRuleKey, IReadOnlyDictionary<string, object?>> Rules,
    CuiCustomTheme? CustomTheme = null, string? CustomFingerprint = null);
