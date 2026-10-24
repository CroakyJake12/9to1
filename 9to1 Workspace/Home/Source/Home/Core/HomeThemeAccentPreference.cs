using System.Collections.ObjectModel;

namespace HavenOS.Home.Core;

// Persisted numeric values are stable; absence in the v1 envelope means None.
public enum HomeThemeAccentOverrideMode { None = 0, AllApps = 1, IndividualApps = 2 }

/// <summary>Immutable, value-equal accent choices; storage remains owned by Home.</summary>
public sealed class HomeThemeAccentPreference : IEquatable<HomeThemeAccentPreference>
{
    public const int MaximumIndividualAppChoices = 32;
    public const string InitialAllAppsColour = "#6366F1";
    public const string InitialIndividualAppColour = "#2563EB";
    public static HomeThemeAccentPreference Default { get; } = new();
    public HomeThemeAccentOverrideMode Mode { get; }
    public string? AllAppsColour { get; }
    public IReadOnlyDictionary<string, string> IndividualApps { get; }

    public HomeThemeAccentPreference(HomeThemeAccentOverrideMode mode = HomeThemeAccentOverrideMode.None, string? allAppsColour = null,
        IReadOnlyDictionary<string, string>? individualApps = null)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
        AllAppsColour = allAppsColour is null ? null : CanonicalColour(allAppsColour);
        if (individualApps?.Count > MaximumIndividualAppChoices) throw new ArgumentException("Too many app accent choices.", nameof(individualApps));
        var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (appId, colour) in individualApps ?? new Dictionary<string, string>())
        {
            ValidateAppId(appId);
            copy.Add(appId, CanonicalColour(colour));
        }
        IndividualApps = new ReadOnlyDictionary<string, string>(copy);
    }
    public HomeThemeAccentPreference WithMode(HomeThemeAccentOverrideMode mode) => new(mode, AllAppsColour, IndividualApps);
    public HomeThemeAccentPreference WithAllAppsColour(string? colour) => new(Mode, colour, IndividualApps);
    public HomeThemeAccentPreference WithAppColour(string appId, string? colour)
    {
        ValidateAppId(appId);
        var next = IndividualApps.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (colour is null) next.Remove(appId); else next[appId] = CanonicalColour(colour);
        return new(Mode, AllAppsColour, next);
    }
    public string? ResolveColour(string appId) => Mode switch
    {
        HomeThemeAccentOverrideMode.AllApps => AllAppsColour ?? InitialAllAppsColour,
        HomeThemeAccentOverrideMode.IndividualApps => IndividualApps.GetValueOrDefault(appId, InitialIndividualAppColour),
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
    public bool Equals(HomeThemeAccentPreference? other) => other is not null && Mode == other.Mode && AllAppsColour == other.AllAppsColour &&
        IndividualApps.Count == other.IndividualApps.Count && IndividualApps.All(pair => other.IndividualApps.TryGetValue(pair.Key, out var colour) && colour == pair.Value);
    public override bool Equals(object? obj) => obj is HomeThemeAccentPreference other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Mode); hash.Add(AllAppsColour, StringComparer.Ordinal);
        foreach (var pair in IndividualApps) { hash.Add(pair.Key, StringComparer.Ordinal); hash.Add(pair.Value, StringComparer.Ordinal); }
        return hash.ToHashCode();
    }
}
