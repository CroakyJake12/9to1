// CUI Theme Expression — ported from HavenThemeCatalog.cs.
// Original source: 9to1 Workspace/shared/src/Haven.Desktop/HavenUI/Tokens/HavenThemeCatalog.cs

namespace CakeOS.Cui.Themes;

/// <summary>
/// The visual personality of one canonical CUI theme: how the shared
/// component system expresses radius, borders, shadows, motion and interaction
/// feedback on top of a resolved surface palette. Layout, spacing rhythm,
/// navigation and control identity are intentionally absent — themes never
/// change information architecture.
/// </summary>
public sealed record CuiThemeExpression(
    CuiTheme Theme,
    string DisplayName,
    string Description,
    double ControlRadiusScale,
    double CardRadiusScale,
    double PopupRadiusScale,
    double MotionDurationScale,
    double ShadowOpacityScale,
    double BorderIntensity,
    double SpacingScale = 1d,
    double TypographyScale = 1d,
    double ControlHeightScale = 1d,
    double ElevationScale = 1d)
{
    /// <summary>Baseline radii matching the pre-theme CUI geometry.</summary>
    public const double BaseControlRadius = 10d;
    public const double BaseCardRadius = 16d;
    public const double BasePopupRadius = 20d;
}
