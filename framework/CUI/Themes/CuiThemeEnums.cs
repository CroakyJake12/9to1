// CUI Theme Enums — ported from Haven.Core (HavenUiTheme, HavenUiAppearance, HavenAccentColour).
// Original source: 9to1 Workspace/shared/src/Haven.Core/Models/HavenPersonalisationModels.cs
//                   9to1 Workspace/shared/src/Haven.Core/Models/GenerativeUiModels.cs

namespace CakeOS.Cui.Themes;

/// <summary>
/// Canonical CUI visual themes. A theme decides how components look and react
/// (colour treatment, geometry personality, interaction feedback and motion);
/// it never changes navigation structure or information architecture.
/// Glow is the default and migration fallback and must remain visually
/// identical to the pre-theme appearance.
/// </summary>
public enum CuiTheme
{
    Glow = 0,
    Bubble = 1,
    Retro = 2,
    Playful = 3,
    Cinematic = 4
}

/// <summary>
/// The four colour-only appearances supported by the canonical CUI design system.
/// Numeric order matches the discrete Settings brightness slider.
/// </summary>
public enum CuiAppearance
{
    SuperBright = 0,
    Bright = 1,
    Dark = 2,
    SuperDark = 3
}

/// <summary>
/// Semantic accent colour families offered by personalisation. Each id expands
/// to a full anchor set (primary/secondary/strong/soft) resolved through the
/// active theme and appearance rather than acting as a single RGB constant.
/// Numeric values are persisted; never renumber.
/// </summary>
public enum CuiAccentColour
{
    Red = 0,
    Orange = 1,
    Yellow = 2,
    Lime = 3,
    Green = 4,
    Teal = 5,
    Cyan = 6,
    Blue = 7,
    Purple = 8,
    Pink = 9,
    Strawberry = 10,
    Brown = 11,
    Monotone = 12
}
