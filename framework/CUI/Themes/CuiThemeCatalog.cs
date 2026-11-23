// CUI Theme Catalog — ported from HavenThemeCatalog.cs.
// Original source: 9to1 Workspace/shared/src/Haven.Desktop/HavenUI/Tokens/HavenThemeCatalog.cs
//
// Professional is the default. Explicit Glow remains the legacy identity transform.

namespace CakeOS.Cui.Themes;

/// <summary>
/// Catalogue of the canonical themes. Professional is the default;
/// explicit Glow remains the legacy identity transform.
/// </summary>
public static class CuiThemeCatalog
{
    public const string ComicId = "comic";
    public const string ComicName = "Comic";
    private const string ComicQualifiedPrefix = "builtin:comic:";

    // Older libraries could contain a custom theme named Comic. Keep its name
    // and issue a distinct built-in preference key absent from that library.
    public static string ComicSelectionKey(IEnumerable<string> customNames)
    {
        var occupied = customNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!occupied.Contains(ComicName)) return ComicName;
        for (var index = 0; ; index++)
        {
            var key = ComicQualifiedPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!occupied.Contains(key)) return key;
        }
    }

    public static bool IsQualifiedComicKey(string? name) =>
        name?.StartsWith(ComicQualifiedPrefix, StringComparison.OrdinalIgnoreCase) == true &&
        int.TryParse(name.AsSpan(ComicQualifiedPrefix.Length), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out _);

    public static bool IsLegacyComicName(string? name) =>
        string.Equals(name?.Trim(), "Cinematic", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<CuiThemeExpression> All { get; } =
    [
        new(
            CuiTheme.Glow, "Glow",
            "Tidal gradients and soft glow accents.",
            1.0, 1.0, 1.0, 1.0, 1.0, 1.0),
        new(
            CuiTheme.Bubble, "Bubble",
            "Soft glassy surfaces, atmospheric tint and gentle bloom.",
            1.35, 1.3, 1.25, 1.15, 1.35, 0.8, 1.08, 1.02, 1.08, 1.3),
        new(
            CuiTheme.Retro, "Retro",
            "Engineered technical surfaces with fast edge illumination.",
            0.45, 0.55, 0.6, 0.7, 0.75, 1.25, 0.9, 0.96, 0.92, 0.75),
        new(
            CuiTheme.Playful, "Playful",
            "Tactile tonal shapes with springy, friendly feedback.",
            1.5, 1.35, 1.3, 0.9, 0.9, 1.1, 1.05, 1.04, 1.08, 0.9),
        new(
            CuiTheme.Cinematic, ComicName,
            "Ink panels, cyan and plum highlights, and bold yellow action plates.",
            .55, .68, .58, .65, 1.6, 1.25, 1.0, 1.0, 1.0, 1.4),
        new(
            CuiTheme.Professional, "Professional",
            "Quiet layered surfaces, rounded floating menus and precise editorial controls.",
            1.2, 0.95, 1.2, 0.8, 0.55, 0.65, 1.0, 1.0, 0.9, 0.8),
        new(CuiTheme.FrostedGlass, "Frosted Glass", "Translucent frosted surfaces and soft light.",
            1.2, 1.15, 1.25, 1, .65, .6),
        new(CuiTheme.Metallic, "Metallic", "Brushed tonal surfaces and precise metallic highlights.",
            .6, .75, .8, .75, .75, 1.1),
        new(CuiTheme.Western, "Western", "Warm parchment tones and crafted borders.",
            .45, .65, .6, .9, .65, 1.15)
    ];

    /// <summary>
    /// Resolves a theme expression; unknown values fall back to Professional so a
    /// malformed preference can never prevent the app from launching.
    /// </summary>
    public static CuiThemeExpression Resolve(CuiTheme theme)
    {
        foreach (var candidate in All)
            if (candidate.Theme == theme)
                return candidate;
        return All.Single(expression => expression.Theme == CuiTheme.Professional);
    }

    /// <summary>Resolves a built-in name or canonical ID without falling back.</summary>
    public static CuiThemeExpression? FindBuiltIn(string? value, bool acceptLegacyAlias = true)
    {
        var name = value?.Trim();
        if (IsQualifiedComicKey(name) || acceptLegacyAlias && IsLegacyComicName(name))
            return Resolve(CuiTheme.Cinematic);
        return All.FirstOrDefault(theme => string.Equals(theme.DisplayName, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Parses a persisted theme name safely, falling back to Professional.</summary>
    public static CuiTheme Parse(string? value)
    {
        if (FindBuiltIn(value) is { } builtIn) return builtIn.Theme;
        if (Enum.TryParse<CuiTheme>(value?.Replace(" ", string.Empty), ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed) && (parsed != CuiTheme.Cinematic || value?.Trim() == "4"))
            return parsed;
        return CuiTheme.Professional;
    }

    /// <summary>Returns the canonical persisted name for a theme.</summary>
    public static string Name(CuiTheme theme) =>
        Resolve(theme).DisplayName;
}
