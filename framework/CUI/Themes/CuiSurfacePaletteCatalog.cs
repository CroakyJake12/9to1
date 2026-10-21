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
    /// <summary>Ink for an opaque primitive button, using existing palette roles only.</summary>
    public Color ButtonForeground => ResolveButtonForeground(Button);
    public Color ButtonHoverForeground => ResolveButtonForeground(ButtonHover);
    public Color ButtonPressedForeground => ResolveButtonForeground(ButtonPressed);

    private Color ResolveButtonForeground(Color background)
    {
        // Translucent states require their actual backing surface. Preserve the
        // existing text role rather than treating alpha as an opaque colour.
        if (background.A != 255 || CuiContrast.Ratio(Text, background) >= 4.5d)
            return Text;
        return CuiContrast.Ratio(AccentInk, background) > CuiContrast.Ratio(Text, background)
            ? AccentInk : Text;
    }

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
    public static CuiTheme ActiveTheme { get; set; } = CuiTheme.Professional;

    /// <summary>
    /// Resolves a full palette for the given surface and appearance, applying the current
    /// theme expression and accent override logic.
    /// </summary>
    public static CuiPalette For(string surface, CuiAppearance appearance) =>
        For(surface, appearance, ActiveTheme);

    /// <summary>Compatibility entry point for existing personalisation callers.</summary>
    public static CuiPalette For(string surface, CuiAppearance appearance, CuiTheme themeOverride) =>
        ForCore(surface, appearance, themeOverride,
            OverrideAccent && AccentOverride is { } colour ? CuiAccentCatalog.Resolve(colour, appearance) : null);

    /// <summary>
    /// Resolves a scoped preference without reading or mutating global accent state.
    /// Null preserves the owning app's default. A chosen colour remains an anchor:
    /// the theme and appearance still own gradients, surfaces and interaction states.
    /// </summary>
    public static CuiPalette For(string surface, CuiAppearance appearance, CuiTheme themeOverride, string? accentColour) =>
        ForCore(surface, appearance, themeOverride, accentColour is null ? null : ColourAnchors(accentColour));

    /// <summary>
    /// Resolves an app-owned scope without substituting its surface. App identity
    /// selects the default accent; explicit Home overrides still take precedence.
    /// </summary>
    public static CuiPalette For(string surface, CuiAppearance appearance, CuiTheme themeOverride,
        string? accentColour, string? owningAppId) =>
        ForCore(surface, appearance, themeOverride, accentColour is null ? null : ColourAnchors(accentColour), owningAppId);

    private static CuiPalette ForCore(string surface, CuiAppearance appearance, CuiTheme theme,
        CuiAccentAnchorSet? accentOverride, string? owningAppId = null)
    {
        if (!Enum.IsDefined(appearance)) throw new ArgumentOutOfRangeException(nameof(appearance));
        var hue = Hues.TryGetValue(surface, out var configured) ? configured : Hues["Home"];
        // An explicit owning app identity owns its accent. Only when no owner is supplied
        // do we infer from the surface. Professional's unknown/absent owner falls back to
        // blue below; other themes retain their historical surface inference for compatibility.
        var appColour = owningAppId is null
            ? CuiAppAccentCatalog.Find(surface)?.Colour
            : CuiAppAccentCatalog.Find(owningAppId)?.Colour
                ?? (theme == CuiTheme.Professional ? null : CuiAppAccentCatalog.Find(surface)?.Colour);
        // Professional has no app-specific default accent for unknown/no-app surfaces;
        // use the existing canonical blue anchors instead of the legacy surface hue
        // (e.g. Spaces purple). This is palette resolution only: explicit Home
        // accent overrides and persisted preference modes remain untouched.
        var anchors = accentOverride
            ?? (appColour is null
                ? (theme == CuiTheme.Professional ? ColourAnchors("#2563EB") : null)
                : ColourAnchors(appColour));
        var accent = anchors?.Primary ?? Color.Parse(hue.Accent);
        var secondary = anchors?.Secondary ?? Color.Parse(hue.Secondary);
        var strong = anchors?.Strong ?? Color.Parse(hue.Strong);
        var soft = anchors?.Soft ?? Color.Parse(hue.Soft);
        if (anchors is not null)
            hue = hue with { Tide = Blend(Colors.White, accent, .28).ToString() };
        var palette = Assemble(hue, appearance, theme, accent, secondary, strong, soft);
        return theme == CuiTheme.Glow ? palette : Express(palette, appearance);
    }

    private static CuiAccentAnchorSet ColourAnchors(string value)
    {
        if (value.Length != 7 || value[0] != '#' || !value.AsSpan(1).ToArray().All(Uri.IsHexDigit))
            throw new ArgumentException("An accent colour must use #RRGGBB.", nameof(value));
        var colour = Color.Parse(value);
        return new(colour, Blend(colour, Colors.White, .22), Blend(colour, Colors.Black, .25),
            Blend(colour, Colors.White, .85));
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
            CuiTheme.Professional => Professional(palette, appearance),
            CuiTheme.FrostedGlass => FrostedGlass(palette, appearance),
            CuiTheme.Metallic => Metallic(palette, appearance),
            CuiTheme.Western => Western(palette, appearance),
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

    // CUI Professional owns the hierarchy across platforms: a quiet stage,
    // distinct working panels and lifted floating surfaces. No platform material
    // or native theme is the visual source of these roles.
    private static CuiPalette Professional(CuiPalette palette, CuiAppearance appearance)
    {
        var dark = appearance is CuiAppearance.Dark or CuiAppearance.SuperDark;
        var deep = appearance == CuiAppearance.SuperDark;
        var white = appearance == CuiAppearance.SuperBright;
        var accent = palette.Accent;
        var background = Color.Parse(dark ? deep ? "#FF171819" : "#FF202122" : white ? "#FFFCFCFD" : "#FFF0F1F3");
        // Deliberate charcoal surface depth. The contextual working surface sits one neutral step
        // above the shell/background, and the content/card/floating surface sits one step higher
        // again, so shell/rail, contextual and content/card roles read as distinct layers without
        // breaking Professional's quiet hierarchy or per-app accent neutrality.
        var panel = Color.Parse(dark ? deep ? "#FF1C1C1E" : "#FF262627" : white ? "#FFFFFFFF" : "#FFF5F5F7");
        var panel2 = Color.Parse(dark ? deep ? "#FF232324" : "#FF2D2D30" : white ? "#FFFFFFFF" : "#FFFFFFFF");
        return palette with
        {
            TideBase = background,
            TideColour = Color.Parse(dark ? deep ? "#FF090B0E" : "#FF111419" : white ? "#FFFFFFFF" : "#FFFAFAFC"),
            Accent = accent, AccentSecondary = accent, AccentStrong = accent,
            AccentSoft = Blend(panel, accent, dark ? .16 : .10),
            AccentInk = CuiContrast.Ratio(Colors.Black, accent) >= CuiContrast.Ratio(Colors.White, accent) ? Colors.Black : Colors.White,
            Text = Color.Parse(dark ? "#FFE5E5E7" : "#FF252628"),
            TextSoft = Color.Parse(dark ? "#FFC3C4C7" : "#FF55575D"),
            Muted = Color.Parse(dark ? "#FFA0A1A6" : "#FF696B72"), Muted2 = Color.Parse(dark ? "#FF929399" : "#FF75777E"),
            Panel = panel, Panel2 = panel2,
            Panel3 = Color.Parse(dark ? deep ? "#FF2B2B2E" : "#FF353537" : white ? "#FFF2F2F4" : "#FFEBEBED"),
            PanelHover = Color.Parse(dark ? deep ? "#FF313133" : "#FF3B3B3E" : white ? "#FFEAEAEC" : "#FFE2E2E5"),
            Line = Color.Parse(dark ? "#FF2D2E31" : "#FFE0E1E5"), LineStrong = Color.Parse(dark ? "#FF55565C" : "#FFA6A7AD"),
            Button = Color.Parse(dark ? "#FF292A2D" : "#FFE8E9ED"), ButtonHover = Color.Parse(dark ? "#FF343538" : "#FFDDDEE3"),
            ButtonPressed = Color.Parse(dark ? "#FF242527" : "#FFD1D2D8"),
            Focus = CuiContrast.EnsureForegroundContrast(accent, panel, 3), AccentBorder = accent
        };
    }

    private static CuiPalette FrostedGlass(CuiPalette palette, CuiAppearance appearance)
    {
        var dark = appearance is CuiAppearance.Dark or CuiAppearance.SuperDark;
        var deep = appearance == CuiAppearance.SuperDark;
        var white = appearance == CuiAppearance.SuperBright;
        var shell = Color.Parse(dark ? deep ? "#FF060F1C" : "#FF101D2C" : white ? "#FFEAF3FA" : "#FFDCE8F1");
        var panel = Color.Parse(dark ? deep ? "#E01E344E" : "#E425435B" : white ? "#EAF0F8FC" : "#E8ECF5FB");
        var raised = Color.Parse(dark ? deep ? "#EF2D455E" : "#F035536B" : white ? "#F8FFFFFF" : "#F8F6FBFF");
        return FinishPreset(palette with
        {
            TideBase = shell,
            TideColour = Color.Parse(dark ? deep ? "#FF0B1B2D" : "#FF152A3C" : white ? "#FFFAFDFF" : "#FFEEF5FA"),
            Panel = panel, Panel2 = raised,
            Panel3 = Color.Parse(dark ? "#FF3D5A73" : "#FFD8E7F2"),
            PanelHover = Blend(raised, palette.Accent, dark ? .12 : .06),
            Text = Color.Parse(dark ? "#FFF0F8FF" : "#FF12293D"),
            TextSoft = Color.Parse(dark ? "#FFD8E8F4" : "#FF334F66"),
            Muted = Color.Parse(dark ? "#FFBDCEDD" : "#FF466077"),
            Muted2 = Color.Parse(dark ? "#FFB3C7D9" : "#FF526A7E"),
            Line = Color.Parse(dark ? "#FF466A88" : "#FFB5CDDF"),
            LineStrong = Color.Parse(dark ? "#FF86AAC6" : "#FF64849F"),
            Button = WithAlpha(Blend(raised, palette.Accent, dark ? .15 : .10), 244),
            ButtonHover = Blend(raised, palette.Accent, dark ? .23 : .16),
            ButtonPressed = Blend(raised, palette.Accent, dark ? .30 : .24),
            AccentSoft = Blend(panel, palette.Accent, dark ? .20 : .12)
        });
    }

    private static CuiPalette Metallic(CuiPalette palette, CuiAppearance appearance)
    {
        var dark = appearance is CuiAppearance.Dark or CuiAppearance.SuperDark;
        var deep = appearance == CuiAppearance.SuperDark;
        var white = appearance == CuiAppearance.SuperBright;
        var shell = Color.Parse(dark ? deep ? "#FF080B0F" : "#FF14171B" : white ? "#FFE7EAEE" : "#FFC9CDD2");
        var panel = Color.Parse(dark ? deep ? "#FF1E232A" : "#FF292F36" : white ? "#FFF9FAFC" : "#FFE2E5E9");
        var raised = Color.Parse(dark ? deep ? "#FF2B323B" : "#FF353D45" : white ? "#FFFFFFFF" : "#FFF0F2F4");
        return FinishPreset(palette with
        {
            TideBase = shell,
            TideColour = Color.Parse(dark ? deep ? "#FF030507" : "#FF0B0E12" : white ? "#FFCCD2D9" : "#FFB6BDC5"),
            Panel = panel, Panel2 = raised,
            Panel3 = Color.Parse(dark ? deep ? "#FF37414D" : "#FF424B56" : white ? "#FFD9DEE5" : "#FFB7BEC7"),
            PanelHover = Blend(raised, dark ? Colors.White : Colors.Black, .08),
            Text = Color.Parse(dark ? "#FFF5F7FA" : "#FF171C23"),
            TextSoft = Color.Parse(dark ? "#FFD9E0E8" : "#FF343E4A"),
            Muted = Color.Parse(dark ? "#FFBDC8D5" : "#FF424F5E"),
            Muted2 = Color.Parse(dark ? "#FFAFBDCC" : "#FF4B5969"),
            Line = Color.Parse(dark ? "#FF596775" : "#FF9AA6B4"),
            LineStrong = Color.Parse(dark ? "#FF8E9BAB" : "#FF607184"),
            Button = raised,
            ButtonHover = Blend(raised, dark ? Colors.White : Colors.Black, .10),
            ButtonPressed = Blend(raised, Colors.Black, dark ? .16 : .12),
            AccentSoft = Blend(panel, palette.Accent, dark ? .20 : .12)
        });
    }

    private static CuiPalette Western(CuiPalette palette, CuiAppearance appearance)
    {
        var dark = appearance is CuiAppearance.Dark or CuiAppearance.SuperDark;
        var deep = appearance == CuiAppearance.SuperDark;
        var white = appearance == CuiAppearance.SuperBright;
        var shell = Color.Parse(dark ? deep ? "#FF160F09" : "#FF2B2016" : white ? "#FFF8EFD9" : "#FFE9D6B3");
        var panel = Color.Parse(dark ? deep ? "#FF2E2116" : "#FF453321" : white ? "#FFFFF7E5" : "#FFF4E6CA");
        var raised = Color.Parse(dark ? deep ? "#FF3B2B1C" : "#FF513D28" : white ? "#FFFFFBF0" : "#FFFBF0DA");
        return FinishPreset(palette with
        {
            TideBase = shell,
            TideColour = Color.Parse(dark ? deep ? "#FF0E0905" : "#FF1D140D" : white ? "#FFEEDDAD" : "#FFCFB184"),
            Panel = panel, Panel2 = raised,
            Panel3 = Color.Parse(dark ? "#FF655037" : "#FFE4CCA2"),
            PanelHover = Blend(raised, Color.Parse("#FFC29558"), .15),
            Text = Color.Parse(dark ? "#FFFFF1D7" : "#FF302010"),
            TextSoft = Color.Parse(dark ? "#FFEBD2AA" : "#FF523A20"),
            Muted = Color.Parse(dark ? "#FFE0C39A" : "#FF634727"),
            Muted2 = Color.Parse(dark ? "#FFD5B88E" : "#FF6D502E"),
            Line = Color.Parse(dark ? "#FF8F704A" : "#FFBA9868"),
            LineStrong = Color.Parse(dark ? "#FFD5AA70" : "#FF80613C"),
            Button = Color.Parse(dark ? "#FF62482C" : "#FFE3C492"),
            ButtonHover = Color.Parse(dark ? "#FF775837" : "#FFECD4AC"),
            ButtonPressed = Color.Parse(dark ? "#FF4D371F" : "#FFC9A16A"),
            AccentSoft = Blend(panel, palette.Accent, dark ? .18 : .12)
        });
    }

    private static CuiPalette FinishPreset(CuiPalette palette) => palette with
    {
        // Preserve every app/override accent anchor; make focus and ink readable
        // against the preset's own surfaces rather than changing that identity.
        Focus = CuiContrast.EnsureForegroundContrast(palette.Accent, Blend(palette.TideBase, palette.Panel, palette.Panel.A / 255d), 3),
        AccentInk = CuiContrast.Ratio(Colors.Black, palette.Accent) >= CuiContrast.Ratio(Colors.White, palette.Accent) ? Colors.Black : Colors.White,
        AccentBorder = palette.Accent
    };

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
