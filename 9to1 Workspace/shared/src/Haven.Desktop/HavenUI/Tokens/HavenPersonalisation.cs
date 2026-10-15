using Haven.Core;

namespace Haven.Desktop.HavenUI.Tokens;

/// <summary>
/// Process-wide personalisation state consulted by the palette catalogue so
/// every surface, the tidal background and generated UI resolve through one
/// shared theme/accent pipeline. Owned by <c>UserPreferencesService</c>; other
/// call sites read it but must not write it.
/// </summary>
internal static class HavenPersonalisation
{
    /// <summary>Active canonical theme. Defaults to Glow (pre-theme baseline).</summary>
    internal static HavenUiTheme Theme
    {
        get => field;
        set
        {
            field = Enum.IsDefined(value) ? value : HavenUiTheme.Glow;

            // Merge stage M2: CUI is the theme authority, so the legacy axis writes through to
            // it. Previously this legacy static was the only theme state and the CUI palette
            // used by the Spaces surfaces had no idea what the user had selected - CUI's
            // ActiveTheme was only ever written by Boards. Writing through keeps one authority
            // and keeps this legacy projection consistent with it. Removed at stage M3/M5.
            CakeOS.Cui.Themes.CuiSurfacePaletteCatalog.ActiveTheme = ToCui(field);
        }
    }

    private static CakeOS.Cui.Themes.CuiTheme ToCui(HavenUiTheme theme) => theme switch
    {
        HavenUiTheme.Bubble => CakeOS.Cui.Themes.CuiTheme.Bubble,
        HavenUiTheme.Retro => CakeOS.Cui.Themes.CuiTheme.Retro,
        HavenUiTheme.Playful => CakeOS.Cui.Themes.CuiTheme.Playful,
        HavenUiTheme.Cinematic => CakeOS.Cui.Themes.CuiTheme.Cinematic,
        _ => CakeOS.Cui.Themes.CuiTheme.Glow
    };

    /// <summary>When true, accent anchors come from <see cref="Accent"/> instead of surface hues.</summary>
    internal static bool OverrideAccent { get; set; }

    /// <summary>Semantic accent family used while <see cref="OverrideAccent"/> is set.</summary>
    internal static HavenAccentColour? Accent { get; set; }

    /// <summary>User-selected UI font family name, or null for bundled Montserrat.</summary>
    internal static string? FontFamilyName { get; set; }

    /// <summary>Whether the user profile picture renders on identity surfaces.</summary>
    internal static bool UserAvatarEnabled { get; set; }

    /// <summary>Whether the Haven profile picture renders on identity surfaces.</summary>
    internal static bool HavenAvatarEnabled { get; set; }

    /// <summary>Resets to defaults — used by tests and safe-fallback paths.</summary>
    internal static void Reset()
    {
        Theme = HavenUiTheme.Glow;
        OverrideAccent = false;
        Accent = null;
        FontFamilyName = null;
        UserAvatarEnabled = false;
        HavenAvatarEnabled = false;
    }
}
