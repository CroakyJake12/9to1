// CUI Surface Palette — ported from SurfacePaletteCatalog.cs.
// Original source: 9to1 Workspace/shared/src/Haven.Desktop/Controls/SurfacePaletteCatalog.cs
//
// The single editable colour catalogue for CUI surfaces. Product-specific
// hues live in Hues; the four CUI brightness appearances are
// assembled in For(surface, appearance). Adding or restyling a surface should only
// require changing this file.

using Avalonia.Media;

namespace CakeOS.Cui.Themes;

/// <summary>
/// Maps a named surface to its hue anchors.
/// </summary>
public sealed record CuiSurfaceHue(
    string Tide,
    string Accent,
    string Secondary,
    string Strong,
    string Soft,
    string Ink = "#FFFFFFFF");

/// <summary>
/// A fully resolved semantic colour palette for one surface × appearance × theme combination.
/// </summary>
public sealed record CuiPalette(
    Color TideBase,
    Color TideColour,
    Color Accent,
    Color AccentSecondary,
    Color AccentStrong,
    Color AccentSoft,
    Color AccentInk,
    Color Text,
    Color TextSoft,
    Color Muted,
    Color Muted2,
    Color Panel,
    Color Panel2,
    Color Panel3,
    Color PanelHover,
    Color Line,
    Color LineStrong,
    Color Button,
    Color ButtonHover,
    Color ButtonPressed,
    Color Focus,
    Color AccentBorder,
    Color Attention,
    Color AttentionBorder,
    CuiTheme Theme = CuiTheme.Glow)
{
    /// <summary>The live three-tier gradient palette for the active page.</summary>
    public CuiAccentPalette AccentPalette => CuiAccentPalette.FromAnchors(
        Accent,
        AccentSecondary,
        AccentStrong,
        AccentInk,
        AccentSoft,
        Panel);
}

/// <summary>
/// The single editable colour catalogue for CUI surfaces. Maps surface × appearance × theme
/// to a fully resolved semantic palette consumed by the CUI theme resource applier.
/// </summary>
public static class CuiSurfacePaletteCatalog
{
    // Surface hue definitions ported from original Haven source.
    // Each surface has: Tide, Accent, Secondary, Strong, Soft, Ink.
    private static readonly IReadOnlyDictionary<string, CuiSurfaceHue> Hues =
        new Dictionary<string, CuiSurfaceHue>(StringComparer.OrdinalIgnoreCase)
        {
            ["Home"] = Hue("#FF171D4A", "#FF3527FF", "#FF5868FF", "#FF2115C7", "#FF202750"),
            ["Chat"] = Hue("#FF171D4A", "#FF3527FF", "#FF5868FF", "#FF2115C7", "#FF202750"),
            ["Study"] = Hue("#FF17194A", "#FF3927FF", "#FF695CFF", "#FF2416C7", "#FF22244F"),
            ["Tasks"] = Hue("#FF4A1D0E", "#FFFF5B19", "#FFFF7C43", "#FFC83A00", "#FF482518"),
            ["Studio"] = Hue("#FF102B3A", "#FF19B8FF", "#FF62CEFF", "#FF007CB7", "#FF173342"),
            ["Browse"] = Hue("#FF10273A", "#FF168FEA", "#FF59B5FF", "#FF075C9C", "#FF193246"),
            ["Plan"] = Hue("#FF3C270F", "#FFFFA11A", "#FFFFBE5C", "#FFB96B00", "#FF42331B"),
            ["Automations"] = Hue("#FF3C270F", "#FFFFA11A", "#FFFFBE5C", "#FFB96B00", "#FF42331B"),
            ["Terminal"] = Hue("#FF102B3A", "#FF19B8FF", "#FF62CEFF", "#FF007CB7", "#FF173342"),
            ["Training"] = Hue("#FFDCCBFA", "#FF8254CB", "#FFA27BDD", "#FF56308F", "#FFEDE4FC"),
            ["Imagine"] = Hue("#FFE6C9F8", "#FFA34EC4", "#FFC37BDD", "#FF702B8C", "#FFF2E1FA"),
            ["Present"] = Hue("#FFFFCAB7", "#FFE65F42", "#FFF08D74", "#FF9E3824", "#FFFFE5DC"),
            ["Data"] = Hue("#FFC7E2DD", "#FF268B7B", "#FF62B4A6", "#FF155F53", "#FFDCF0EC"),
            ["Vision"] = Hue("#FFD2CDF0", "#FF6554B3", "#FF8E80CC", "#FF423383", "#FFE7E4F7"),
            ["Play"] = Hue("#FFCFEACB", "#FF3E9A55", "#FF72BC81", "#FF236A35", "#FFE1F2DE"),
            ["Translate"] = Hue("#FFCDDEF5", "#FF3D70BE", "#FF7198D3", "#FF274F8A", "#FFE2EAF8"),
            ["Launcher"] = Hue("#FFDCCEF0", "#FF8055B4", "#FFA17AC8", "#FF56377F", "#FFEDE5F6"),
            ["Go"] = Hue("#FF171D4A", "#FF3527FF", "#FF4658FF", "#FF2115C7", "#FF202750"),
            ["Spaces"] = Hue("#FF221A4A", "#FF9D5CFF", "#FFB87EFF", "#FF6E2BC7", "#FF29224F"),
            ["Boards"] = Hue("#FF12332A", "#FF1FA37A", "#FF5FC2A0", "#FF0E6E52", "#FF1A3A31"),
            ["Maps"] = Hue("#FF10271E", "#FF2FBF7F", "#FF6BD9A4", "#FF12855A", "#FF17342A"),
            ["Dashboard"] = Hue("#FF171D4A", "#FF3527FF", "#FF5868FF", "#FF2115C7", "#FF202750"),
        };

    /// <summary>Default accent override state — set by the personalisation system.</summary>
    public static bool OverrideAccent { get; set; }

    /// <summary>Active accent override colour — set by the personalisation system.</summary>
    public static CuiAccentColour? AccentOverride { get; set; }

    /// <summary>Active canonical theme — set by the personalisation system.</summary>
    public static CuiTheme ActiveTheme { get; set; } = CuiTheme.Glow;

    /// <summary>
    /// Resolves a full palette for the given surface and appearance, applying the current
    /// theme expression and accent override logic.
    /// </summary>
    public static CuiPalette For(string surface, CuiAppearance appearance)
    {
        var hue = Hues.TryGetValue(surface, out var configured) ? configured : Hues["Home"];
        var theme = ActiveTheme;
        var accent = Color.Parse(hue.Accent);
        var secondary = Color.Parse(hue.Secondary);
        var strong = Color.Parse(hue.Strong);
        var soft = Color.Parse(hue.Soft);

        // Accent precedence: an explicit personalisation palette replaces the
        // surface hue anchors; the appearance branch and theme interpretation
        // still adapt them, so apps only ever consume semantic accent values.
        if (OverrideAccent && AccentOverride is { } overrideColour)
        {
            var anchors = CuiAccentCatalog.Resolve(overrideColour, appearance);
            accent = anchors.Primary;
            secondary = anchors.Secondary;
            strong = anchors.Strong;
            soft = anchors.Soft;
        }

        var palette = Assemble(hue, appearance, theme, accent, secondary, strong, soft);
        return theme == CuiTheme.Glow ? palette : Express(palette, appearance);
    }

    /// <summary>
    /// Resolves a full palette for the given surface and appearance with an explicit theme override.
    /// Used by DefaultTheme scoping. Thread-safe: does not mutate ActiveTheme.
    /// </summary>
    public static CuiPalette For(string surface, CuiAppearance appearance, CuiTheme themeOverride)
    {
        // Thread-safe: compute directly without mutating global state
        var hue = Hues.TryGetValue(surface, out var configured) ? configured : Hues["Home"];
        var theme = themeOverride;
        var accent = Color.Parse(hue.Accent);
        var secondary = Color.Parse(hue.Secondary);
        var strong = Color.Parse(hue.Strong);
        var soft = Color.Parse(hue.Soft);

        if (OverrideAccent && AccentOverride is { } overrideColour)
        {
            var anchors = CuiAccentCatalog.Resolve(overrideColour, appearance);
            accent = anchors.Primary;
            secondary = anchors.Secondary;
            strong = anchors.Strong;
            soft = anchors.Soft;
        }

        var palette = Assemble(hue, appearance, theme, accent, secondary, strong, soft);
        return theme == CuiTheme.Glow ? palette : Express(palette, appearance);
    }

    private static CuiPalette Assemble(
        CuiSurfaceHue hue,
        CuiAppearance appearance,
        CuiTheme theme,
        Color accent,
        Color secondary,
        Color strong,
        Color soft)
    {
        if (appearance == CuiAppearance.SuperBright)
        {
            var superBrightTide = Blend(Colors.White, Color.Parse(hue.Tide), 0.62);
            var superBrightSoft = Blend(Colors.White, soft, 0.70);
            return new CuiPalette(
                Color.Parse("#FFFCFEFC"), superBrightTide, accent, secondary, strong, superBrightSoft, Color.Parse(hue.Ink),
                Color.Parse("#FF050607"), Color.Parse("#FF353A3E"), Color.Parse("#FF5D6469"), Color.Parse("#FF4B5257"),
                Color.Parse("#FFFFFFFF"), Color.Parse("#FFF8FAF8"), Color.Parse("#FFF1F5F2"), Color.Parse("#FFE9F0EB"),
                Color.Parse("#FFC8D2CA"), Color.Parse("#FFA9B7AC"), superBrightSoft, Blend(superBrightSoft, secondary, 0.28),
                Blend(superBrightSoft, secondary, 0.48), WithAlpha(accent, 0xB8), WithAlpha(accent, 0x94),
                Color.Parse("#FFFFF59B"), Color.Parse("#FFD7C92B"), theme);
        }

        if (appearance == CuiAppearance.Bright)
        {
            return new CuiPalette(
                Colors.White, Color.Parse(hue.Tide), accent, secondary, strong, soft, Color.Parse(hue.Ink),
                Color.Parse("#FF111111"), Color.Parse("#FF4F565A"), Color.Parse("#FF73797D"), Color.Parse("#FF60676B"),
                Color.Parse("#F5FFFFFF"), Color.Parse("#EBFFFFFF"), Color.Parse("#FFF5F7F5"), Color.Parse("#FFF0F4F1"),
                Color.Parse("#FFDDE4DE"), Color.Parse("#FFBFCAC1"), soft, Blend(soft, secondary, 0.24),
                Blend(soft, secondary, 0.42), WithAlpha(accent, 0x99), WithAlpha(accent, 0x80),
                Color.Parse("#FFFFF9A8"), Color.Parse("#FFE4DF52"), theme);
        }

        if (appearance == CuiAppearance.Dark)
        {
            var darkTide = Blend(Color.Parse("#FF0B0E17"), accent, 0.27);
            var darkSoft = Blend(Color.Parse("#FF171A2A"), accent, 0.22);
            var darkPanel = Blend(Color.Parse("#FF161A2A"), accent, 0.09);
            var darkPanel2 = Blend(Color.Parse("#FF1C2238"), accent, 0.14);
            var darkPanel3 = Blend(Color.Parse("#FF232A45"), accent, 0.18);
            var darkHover = Blend(Color.Parse("#FF2A3354"), accent, 0.22);
            return new CuiPalette(
                Color.Parse("#FF0B0E17"), darkTide, secondary, accent, strong, darkSoft, Color.Parse("#FFFFFFFF"),
                Color.Parse("#FFF8F8FC"), Color.Parse("#FFD5D7E4"), Color.Parse("#FFA7ABC0"), Color.Parse("#FF858BA4"),
                WithAlpha(darkPanel, 0xF5), WithAlpha(darkPanel2, 0xF0), darkPanel3, darkHover,
                Color.Parse("#FF323B5E"), Color.Parse("#FF505B82"), darkSoft, Blend(darkSoft, accent, 0.25),
                Blend(darkSoft, accent, 0.45), WithAlpha(secondary, 0xCC), WithAlpha(secondary, 0xA0),
                Color.Parse("#FF45451E"), Color.Parse("#FFB9B54C"), theme);
        }

        // SuperDark
        var superDarkBase = Color.Parse("#FF06090D");
        var superDarkTide = Blend(Color.Parse("#FF0B0E18"), accent, 0.25);
        var superDarkSoft = Blend(Color.Parse("#FF121526"), accent, 0.20);
        var superDarkPanel = Blend(Color.Parse("#FF0C0F1A"), accent, 0.10);
        var superDarkPanel2 = Blend(Color.Parse("#FF121628"), accent, 0.15);
        var superDarkPanel3 = Blend(Color.Parse("#FF191E34"), accent, 0.20);
        var superDarkHover = Blend(Color.Parse("#FF222941"), accent, 0.24);
        return new CuiPalette(
            superDarkBase, superDarkTide, secondary, accent, strong, superDarkSoft, Color.Parse("#FF020705"),
            Color.Parse("#FFF9F9FD"), Color.Parse("#FFD6D8E6"), Color.Parse("#FFA9AEC4"), Color.Parse("#FF8990AA"),
            WithAlpha(superDarkPanel, 0xF5), WithAlpha(superDarkPanel2, 0xF5), superDarkPanel3, superDarkHover,
            Color.Parse("#FF2D3551"), Color.Parse("#FF4A5577"), superDarkSoft, Blend(superDarkSoft, accent, 0.22),
            Blend(superDarkSoft, accent, 0.40), WithAlpha(secondary, 0xD8), WithAlpha(secondary, 0xA8),
            Color.Parse("#FF363611"), Color.Parse("#FFC9C343"), theme);
    }

    /// <summary>
    /// Applies one non-Glow theme's interaction language to an assembled
    /// palette: how hover, press and selection react, how glassy surfaces are,
    /// and how strongly borders read. Glow never passes through here so the
    /// default appearance stays byte-identical to its baseline.
    /// </summary>
    private static CuiPalette Express(CuiPalette palette, CuiAppearance appearance)
    {
        var isDark = appearance is CuiAppearance.Dark or CuiAppearance.SuperDark;
        return palette.Theme switch
        {
            CuiTheme.Bubble => palette with
            {
                Panel = WithAlpha(palette.Panel, isDark ? (byte)0xE8 : (byte)0xE4),
                Panel2 = WithAlpha(palette.Panel2, isDark ? (byte)0xDE : (byte)0xDA),
                Line = Blend(palette.Line, palette.Panel2, 0.30),
                ButtonHover = Blend(palette.ButtonHover, palette.AccentSoft, 0.45),
                ButtonPressed = Blend(palette.ButtonPressed, palette.AccentStrong, 0.25),
                Focus = WithAlpha(palette.Focus, 0xD8)
            },
            CuiTheme.Retro => palette with
            {
                Line = Blend(palette.LineStrong, palette.Accent, 0.38),
                LineStrong = Blend(palette.LineStrong, palette.Accent, 0.55),
                Button = Blend(palette.Button, palette.TideBase, isDark ? 0.42 : 0.30),
                ButtonHover = WithAlpha(palette.Accent, isDark ? (byte)0x30 : (byte)0x24),
                ButtonPressed = WithAlpha(palette.AccentStrong, (byte)0x40),
                Focus = WithAlpha(palette.AccentSecondary, 0xEE)
            },
            CuiTheme.Playful => palette with
            {
                Panel = WithAlpha(palette.Panel, 0xFF),
                Panel2 = WithAlpha(palette.Panel2, 0xFF),
                Line = Blend(palette.Line, palette.Panel3, 0.35),
                ButtonHover = WithAlpha(Blend(palette.AccentSoft, palette.Accent, 0.40), 0xFF),
                ButtonPressed = WithAlpha(Blend(palette.AccentSoft, palette.AccentStrong, 0.55), 0xFF),
                Focus = WithAlpha(palette.AccentSecondary, 0xE6)
            },
            CuiTheme.Professional => palette with
            {
                Panel = WithAlpha(palette.Panel, 0xFF),
                Panel2 = WithAlpha(palette.Panel2, 0xFF),
                Line = Blend(palette.LineStrong, palette.Panel3, 0.35),
                ButtonHover = Blend(palette.ButtonHover, palette.AccentSoft, 0.25),
                ButtonPressed = Blend(palette.ButtonPressed, palette.AccentStrong, 0.20),
                Focus = WithAlpha(palette.AccentSecondary, 0xFF)
            },
            CuiTheme.Cinematic => palette with
            {
                Panel = WithAlpha(Blend(palette.Panel, palette.TideColour, isDark ? 0.18 : 0.10), isDark ? (byte)0xEC : (byte)0xF0),
                Panel2 = WithAlpha(Blend(palette.Panel2, palette.TideColour, 0.14), isDark ? (byte)0xE6 : (byte)0xEA),
                Panel3 = Blend(palette.Panel3, palette.TideBase, 0.12),
                Line = Blend(palette.Line, palette.TideColour, 0.22),
                ButtonHover = Blend(palette.ButtonHover, palette.Accent, 0.20),
                ButtonPressed = Blend(palette.ButtonPressed, palette.Panel3, 0.30),
                Focus = WithAlpha(palette.Accent, 0xCC)
            },
            _ => palette
        };
    }

    private static CuiSurfaceHue Hue(string tide, string accent, string secondary, string strong, string soft) =>
        new(tide, accent, secondary, strong, soft);

    private static Color WithAlpha(Color value, byte alpha) =>
        Color.FromArgb(alpha, value.R, value.G, value.B);

    private static Color Blend(Color first, Color second, double secondWeight)
    {
        var weight = Math.Clamp(secondWeight, 0, 1);
        return Color.FromArgb(
            255,
            (byte)Math.Round(first.R + (second.R - first.R) * weight),
            (byte)Math.Round(first.G + (second.G - first.G) * weight),
            (byte)Math.Round(first.B + (second.B - first.B) * weight));
    }
}
