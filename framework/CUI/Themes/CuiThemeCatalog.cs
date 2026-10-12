// CUI Theme Catalog — ported from HavenThemeCatalog.cs.
// Original source: 9to1 Workspace/shared/src/Haven.Desktop/HavenUI/Tokens/HavenThemeCatalog.cs
//
// Professional is the default. Explicit Glow remains the legacy identity transform.

namespace CakeOS.Cui.Themes;

/// <summary>
/// Catalogue of the six canonical themes. Professional is the default;
/// explicit Glow remains the legacy identity transform.
/// </summary>
public static class CuiThemeCatalog
{
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
            CuiTheme.Cinematic, "Cinematic",
            "Immersive layered depth with contextual light and smooth fades.",
            1.0, 1.05, 1.1, 1.25, 1.7, 0.95, 1.05, 1.02, 1.0, 1.7),
        new(
            CuiTheme.Professional, "Professional",
            "Quiet layered surfaces, rounded floating menus and precise editorial controls.",
            1.0, 0.75, 1.2, 0.8, 0.55, 0.65, 1.0, 1.0, 0.9, 0.8)
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

    /// <summary>Parses a persisted theme name safely, falling back to Professional.</summary>
    public static CuiTheme Parse(string? value) =>
        Enum.TryParse<CuiTheme>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CuiTheme.Professional;

    /// <summary>Returns the canonical persisted name for a theme.</summary>
    public static string Name(CuiTheme theme) =>
        Resolve(theme).DisplayName;
}
