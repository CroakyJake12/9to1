// CUI Accent Colour Catalog — ported from AccentColourCatalog.cs.
// Original source: 9to1 Workspace/shared/src/Haven.Desktop/HavenUI/Tokens/AccentColourCatalog.cs
//
// The thirteen semantic accent palettes offered by personalisation. Values are
// hue anchors: the active appearance branch blends them into surfaces and the
// active theme decides how they behave (gradients, glows, illumination), so a
// palette is a colour family rather than a fixed RGB constant.

using Avalonia.Media;

namespace CakeOS.Cui.Themes;

/// <summary>Accent anchors for one semantic colour family in one appearance family.</summary>
public sealed record CuiAccentAnchorSet(Color Primary, Color Secondary, Color Strong, Color Soft);

/// <summary>
/// The thirteen semantic accent palettes offered by personalisation.
/// </summary>
public static class CuiAccentCatalog
{
    private sealed record PaletteDefinition(
        CuiAccentColour Colour,
        string Name,
        CuiAccentAnchorSet Light,
        CuiAccentAnchorSet Dark);

    // Dark-appearance anchors are lifted toward brighter primaries so accents
    // keep contrast on near-black panels; Yellow/Lime use deliberately deep
    // strong/soft anchors to survive contrast guards.
    private static readonly IReadOnlyList<PaletteDefinition> Palettes =
    [
        new(CuiAccentColour.Red, "Red",
            new(Color.Parse("#FFD13438"), Color.Parse("#FFE0575B"), Color.Parse("#FFA31E22"), Color.Parse("#FFF6DADA")),
            new(Color.Parse("#FFFF5C60"), Color.Parse("#FFFF8487"), Color.Parse("#FFC22F33"), Color.Parse("#FF3A1D20"))),
        new(CuiAccentColour.Orange, "Orange",
            new(Color.Parse("#FFEF7B1A"), Color.Parse("#FFFF9540"), Color.Parse("#FFC25F04"), Color.Parse("#FFFBE8D8")),
            new(Color.Parse("#FFFF9433"), Color.Parse("#FFFFAE5E"), Color.Parse("#FFCC6A10"), Color.Parse("#FF3A2718"))),
        new(CuiAccentColour.Yellow, "Yellow",
            new(Color.Parse("#FFD9A400"), Color.Parse("#FFE9BC2E"), Color.Parse("#FFA87A00"), Color.Parse("#FFFAF0CE")),
            new(Color.Parse("#FFFFC83D"), Color.Parse("#FFFFD75E"), Color.Parse("#FFC79406"), Color.Parse("#FF383115"))),
        new(CuiAccentColour.Lime, "Lime",
            new(Color.Parse("#FF93B500"), Color.Parse("#FFAAC21F"), Color.Parse("#FF6E8800"), Color.Parse("#FFEEF5CF")),
            new(Color.Parse("#FFB4D414"), Color.Parse("#FFC6E23C"), Color.Parse("#FF8AA504"), Color.Parse("#FF2B3413"))),
        new(CuiAccentColour.Green, "Green",
            new(Color.Parse("#FF1E9E58"), Color.Parse("#FF42B877"), Color.Parse("#FF12713E"), Color.Parse("#FFD8F0E2")),
            new(Color.Parse("#FF37C97B"), Color.Parse("#FF5FD998"), Color.Parse("#FF1E8A52"), Color.Parse("#FF16301F"))),
        new(CuiAccentColour.Teal, "Teal",
            new(Color.Parse("#FF0F9494"), Color.Parse("#FF31B0B0"), Color.Parse("#FF086B6B"), Color.Parse("#FFD5EFEE")),
            new(Color.Parse("#FF26B8B8"), Color.Parse("#FF4BD0D0"), Color.Parse("#FF128282"), Color.Parse("#FF12302F"))),
        new(CuiAccentColour.Cyan, "Cyan",
            new(Color.Parse("#FF0FA3D1"), Color.Parse("#FF35BCE6"), Color.Parse("#FF07799C"), Color.Parse("#FFD6EFF8")),
            new(Color.Parse("#FF2FC0EC"), Color.Parse("#FF57D2F5"), Color.Parse("#FF0E93BF"), Color.Parse("#FF10303B"))),
        new(CuiAccentColour.Blue, "Blue",
            new(Color.Parse("#FF2563EB"), Color.Parse("#FF4D82F5"), Color.Parse("#FF1643AF"), Color.Parse("#FFDAE4FB")),
            new(Color.Parse("#FF4E86FF"), Color.Parse("#FF74A1FF"), Color.Parse("#FF2F62CC"), Color.Parse("#FF16233F"))),
        new(CuiAccentColour.Purple, "Purple",
            new(Color.Parse("#FF8B44D8"), Color.Parse("#FFA463E8"), Color.Parse("#FF662BA6"), Color.Parse("#FFEDE0FA")),
            new(Color.Parse("#FFA25FE8"), Color.Parse("#FFB87FF5"), Color.Parse("#FF7C33BD"), Color.Parse("#FF241736"))),
        new(CuiAccentColour.Pink, "Pink",
            new(Color.Parse("#FFE24C8B"), Color.Parse("#FFED6EA3"), Color.Parse("#FFB92E67"), Color.Parse("#FFFBDDE8")),
            new(Color.Parse("#FFF76AA6"), Color.Parse("#FFFA8CBC"), Color.Parse("#FFCC4583"), Color.Parse("#FF391A28"))),
        new(CuiAccentColour.Strawberry, "Strawberry",
            new(Color.Parse("#FFE8554F"), Color.Parse("#FFF2736E"), Color.Parse("#FFB93030"), Color.Parse("#FFFBDAD8")),
            new(Color.Parse("#FFFF7069"), Color.Parse("#FFFF8C86"), Color.Parse("#FFD63E38"), Color.Parse("#FF3A1B19"))),
        new(CuiAccentColour.Brown, "Brown",
            new(Color.Parse("#FF96601F"), Color.Parse("#FFB17A38"), Color.Parse("#FF6E4412"), Color.Parse("#FFF3E6D5")),
            new(Color.Parse("#FFC08A47"), Color.Parse("#FFD4A263"), Color.Parse("#FF96702E"), Color.Parse("#FF31251A"))),
        new(CuiAccentColour.Monotone, "Monotone",
            new(Color.Parse("#FF171717"), Color.Parse("#FF3B3B3B"), Color.Parse("#FF000000"), Color.Parse("#FFE9E9E9")),
            new(Color.Parse("#FFF2F2F2"), Color.Parse("#FFFFFFFF"), Color.Parse("#FFBDBDBD"), Color.Parse("#FF262626")))
    ];

    /// <summary>Ordered semantic colours for the settings palette picker.</summary>
    public static IReadOnlyList<CuiAccentColour> Colours =>
        Palettes.Select(palette => palette.Colour).ToArray();

    /// <summary>Ordered display names for the settings palette picker.</summary>
    public static IReadOnlyList<string> Names =>
        Palettes.Select(palette => palette.Name).ToArray();

    /// <summary>Parses a persisted palette name safely; unknown values return null (no override).</summary>
    public static CuiAccentColour? Parse(string? value)
    {
        foreach (var palette in Palettes)
            if (palette.Name.Equals(value, StringComparison.OrdinalIgnoreCase))
                return palette.Colour;
        return null;
    }

    /// <summary>Returns the canonical persisted name for a palette.</summary>
    public static string Name(CuiAccentColour colour) =>
        Palettes.FirstOrDefault(palette => palette.Colour == colour)?.Name ?? string.Empty;

    /// <summary>
    /// Resolves accent anchors for a palette under the current appearance
    /// family. Bright appearances use the light set, dark appearances the dark set.
    /// </summary>
    public static CuiAccentAnchorSet Resolve(CuiAccentColour colour, CuiAppearance appearance)
    {
        var palette = Palettes.FirstOrDefault(candidate => candidate.Colour == colour) ?? Palettes[7];
        var dark = appearance is CuiAppearance.Dark or CuiAppearance.SuperDark;
        return dark ? palette.Dark : palette.Light;
    }
}
