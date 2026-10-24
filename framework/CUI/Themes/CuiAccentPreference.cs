using System.Collections.ObjectModel;

namespace CakeOS.Cui.Themes;

// Persisted numeric values are stable; absence in the v1 envelope means None.
public enum CuiAccentOverrideMode { None = 0, AllApps = 1, IndividualApps = 2 }

/// <summary>Immutable, value-equal accent choices; storage remains owned by Home.</summary>
public sealed class CuiAccentPreference : IEquatable<CuiAccentPreference>
{
    public const string InitialAllAppsColour = "#6366F1";
    public static CuiAccentPreference Default { get; } = new();
    public CuiAccentOverrideMode Mode { get; }
    public string? AllAppsColour { get; }
    public IReadOnlyDictionary<string, string> IndividualApps { get; }

    public CuiAccentPreference(CuiAccentOverrideMode mode = CuiAccentOverrideMode.None, string? allAppsColour = null,
        IReadOnlyDictionary<string, string>? individualApps = null)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
        AllAppsColour = allAppsColour is null ? null : CanonicalColour(allAppsColour);
        if (individualApps?.Count > 32) throw new ArgumentException("Too many app accent choices.", nameof(individualApps));
        var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (appId, colour) in individualApps ?? new Dictionary<string, string>())
        {
            ValidateAppId(appId);
            copy.Add(appId, CanonicalColour(colour));
        }
        IndividualApps = new ReadOnlyDictionary<string, string>(copy);
    }
    public CuiAccentPreference WithMode(CuiAccentOverrideMode mode) => new(mode, AllAppsColour, IndividualApps);
    public CuiAccentPreference WithAllAppsColour(string? colour) => new(Mode, colour, IndividualApps);
    public CuiAccentPreference WithAppColour(string appId, string? colour)
    {
        ValidateAppId(appId);
        var next = IndividualApps.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (colour is null) next.Remove(appId); else next[appId] = CanonicalColour(colour);
        return new(Mode, AllAppsColour, next);
    }
    /// <summary>Resolve only the stored override without a theme fallback. Palette/rendering callers
    /// must use the theme-aware overload.</summary>
    public string? ResolveColour(string appId) => Mode switch
    {
        CuiAccentOverrideMode.AllApps => AllAppsColour ?? InitialAllAppsColour,
        CuiAccentOverrideMode.IndividualApps => IndividualApps.GetValueOrDefault(appId),
        _ => null
    };

    /// <summary>Resolve an app choice with its effective theme. Individual-app mode treats an
    /// absent Professional override as canonical blue; None continues to use the owning app's
    /// catalog default, while custom themes retain their authored accent when no choice exists.</summary>
    public string? ResolveColour(string appId, CuiTheme theme, bool hasCustomTheme = false) => Mode switch
    {
        CuiAccentOverrideMode.AllApps => AllAppsColour ?? InitialAllAppsColour,
        CuiAccentOverrideMode.IndividualApps => IndividualApps.TryGetValue(appId, out var colour)
            ? colour
            : theme == CuiTheme.Professional && !hasCustomTheme
                ? CuiAppAccentCatalog.ProfessionalUnassignedAppColour
                : null,
        _ => null
    };
    public static string CanonicalColour(string colour)
    {
        if (colour is not { Length: 7 } || colour[0] != '#' || colour.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF"))
            throw new ArgumentException("Choose a colour in #RRGGBB format.", nameof(colour));
        return colour.ToUpperInvariant();
    }
    private static void ValidateAppId(string appId)
    {
        if (string.IsNullOrEmpty(appId) || appId.Length is < 5 or > 64 || !appId.StartsWith("app.", StringComparison.Ordinal) ||
            appId[4] is < 'a' or > 'z' || appId[^1] == '.' || appId.Contains("..", StringComparison.Ordinal) ||
            appId.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '.' and not '-'))
            throw new ArgumentException("An accent choice requires a stable app ID.", nameof(appId));
    }
    public bool Equals(CuiAccentPreference? other) => other is not null && Mode == other.Mode && AllAppsColour == other.AllAppsColour &&
        IndividualApps.Count == other.IndividualApps.Count && IndividualApps.All(pair => other.IndividualApps.TryGetValue(pair.Key, out var colour) && colour == pair.Value);
    public override bool Equals(object? obj) => obj is CuiAccentPreference other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Mode); hash.Add(AllAppsColour, StringComparer.Ordinal);
        foreach (var pair in IndividualApps) { hash.Add(pair.Key, StringComparer.Ordinal); hash.Add(pair.Value, StringComparer.Ordinal); }
        return hash.ToHashCode();
    }
}
