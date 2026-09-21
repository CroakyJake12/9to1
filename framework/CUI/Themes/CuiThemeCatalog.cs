// CUI Theme Catalog — ported from HavenThemeCatalog.cs.
// Original source: 9to1 Workspace/shared/src/Haven.Desktop/HavenUI/Tokens/HavenThemeCatalog.cs
//
// Glow is deliberately the identity transform: when the user has not personalised,
// every value below reproduces the pre-theme appearance exactly.

namespace CakeOS.Cui.Themes;

/// <summary>
/// Catalogue of the five canonical themes. Glow is deliberately the identity
/// transform: when the user has not personalised, every value below
/// reproduces the pre-theme appearance exactly.
/// </summary>
public static class CuiThemeCatalog
{
    public static IReadOnlyList<CuiThemeExpression> All { get; } =
    [
        new(
            CuiTheme.Glow, "Glow",
            "The default CUI look: tidal gradients, soft glow accents.",
            1.0, 1.0, 1.0, 1.0, 1.0, 1.0),
        new(
            CuiTheme.Bubble, "Bubble",
            "Soft glassy surfaces, atmospheric tint and gentle bloom.",
            1.35, 1.3, 1.25, 1.15, 1.35, 0.8),
        new(
            CuiTheme.Retro, "Retro",
            "Engineered technical surfaces with fast edge illumination.",
            0.45, 0.55, 0.6, 0.7, 0.75, 1.25),
        new(
            CuiTheme.Playful, "Playful",
            "Tactile tonal shapes with springy, friendly feedback.",
            1.5, 1.35, 1.3, 0.9, 0.9, 1.1),
        new(
            CuiTheme.Cinematic, "Cinematic",
            "Immersive layered depth with contextual light and smooth fades.",
            1.0, 1.05, 1.1, 1.25, 1.7, 0.95)
    ];

    /// <summary>
    /// Resolves a theme expression; unknown values fall back to Glow so a
    /// malformed preference can never prevent the app from launching.
    /// </summary>
    public static CuiThemeExpression Resolve(CuiTheme theme)
    {
        foreach (var candidate in All)
            if (candidate.Theme == theme)
                return candidate;
        return All[0];
    }

    /// <summary>Parses a persisted theme name safely, falling back to Glow.</summary>
    public static CuiTheme Parse(string? value) =>
        Enum.TryParse<CuiTheme>(value, ignoreCase: true, out var parsed) ? parsed : CuiTheme.Glow;

    /// <summary>Returns the canonical persisted name for a theme.</summary>
    public static string Name(CuiTheme theme) =>
        All.FirstOrDefault(expression => expression.Theme == theme)?.DisplayName ?? All[0].DisplayName;
}
