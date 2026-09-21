// CUI Accent Palette — ported from HavenAccentPalette.cs.
// Original source: 9to1 Workspace/shared/src/Haven.Desktop/HavenUI/Tokens/HavenAccentPalette.cs
//
// The page-owned three-tier accent contract consumed by every CUI control.
// A tier is always a gradient, never a flat colour.

using Avalonia;
using Avalonia.Media;

namespace CakeOS.Cui.Themes;

/// <summary>One live, directional accent gradient.</summary>
public sealed record CuiAccentGradient(
    Color Start,
    Color Middle,
    Color End,
    RelativePoint StartPoint,
    RelativePoint EndPoint)
{
    public static CuiAccentGradient Horizontal(Color start, Color middle, Color end) =>
        new(
            start,
            middle,
            end,
            new RelativePoint(0, 0.5, RelativeUnit.Relative),
            new RelativePoint(1, 0.5, RelativeUnit.Relative));
}

/// <summary>
/// The page-owned three-tier accent contract consumed by every CUI control.
/// A tier is always a gradient, never a flat colour.
/// </summary>
public sealed record CuiAccentPalette(
    CuiAccentGradient Primary,
    CuiAccentGradient Secondary,
    CuiAccentGradient Tertiary,
    Color Foreground,
    Color SoftSurface)
{
    public static CuiAccentPalette FromAnchors(
        Color primary,
        Color secondary,
        Color tertiary,
        Color foreground,
        Color softSurface,
        Color panel)
    {
        var primaryMiddle = Blend(primary, secondary, 0.28);
        var primaryEnd = Blend(primary, tertiary, 0.58);
        var secondaryMiddle = Blend(secondary, primary, 0.30);
        var secondaryEnd = Blend(secondary, primary, 0.62);
        var tertiaryStart = Blend(panel, tertiary, 0.48);
        var tertiaryMiddle = Blend(panel, tertiary, 0.68);
        var tertiaryEnd = Blend(panel, primary, 0.46);

        EnsureVisibleGradient(primary, ref primaryMiddle, ref primaryEnd);
        EnsureVisibleGradient(secondary, ref secondaryMiddle, ref secondaryEnd);
        EnsureVisibleGradient(tertiaryStart, ref tertiaryMiddle, ref tertiaryEnd);

        return new CuiAccentPalette(
            CuiAccentGradient.Horizontal(primary, primaryMiddle, primaryEnd),
            CuiAccentGradient.Horizontal(secondary, secondaryMiddle, secondaryEnd),
            CuiAccentGradient.Horizontal(tertiaryStart, tertiaryMiddle, tertiaryEnd),
            foreground,
            softSurface);
    }

    private static Color Blend(Color first, Color second, double secondWeight)
    {
        var weight = Math.Clamp(secondWeight, 0d, 1d);
        return Color.FromArgb(
            (byte)Math.Round(first.A + ((second.A - first.A) * weight)),
            (byte)Math.Round(first.R + ((second.R - first.R) * weight)),
            (byte)Math.Round(first.G + ((second.G - first.G) * weight)),
            (byte)Math.Round(first.B + ((second.B - first.B) * weight)));
    }

    private static void EnsureVisibleGradient(Color start, ref Color middle, ref Color end)
    {
        if (start != middle || middle != end)
            return;

        var luminance = ((0.2126 * start.R) + (0.7152 * start.G) + (0.0722 * start.B)) / 255d;
        var contrast = luminance > 0.58 ? Colors.Black : Colors.White;
        middle = Blend(start, contrast, 0.12);
        end = Blend(start, contrast, 0.24);
    }
}
