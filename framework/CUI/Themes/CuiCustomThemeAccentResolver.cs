using Avalonia.Media;

namespace CakeOS.Cui.Themes;

/// <summary>Recolours an authored primary fill for an explicit app accent without changing saved theme data.</summary>
public static class CuiCustomThemeAccentResolver
{
    public static CuiCustomTheme Resolve(CuiCustomTheme theme, string? explicitAccentColour)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (explicitAccentColour is null) return theme;
        var targetHex = CuiAccentPreference.CanonicalColour(explicitAccentColour);
        var target = Color.Parse(targetHex).ToHsl();
        var fill = CuiCustomThemeContract.ValidateFill(theme.PrimaryAccent);
        var anchorColour = fill.Kind switch
        {
            CuiCustomFillKind.Gradient => fill.Gradient!.Stops[0].Colour,
            CuiCustomFillKind.Pattern => fill.Pattern!.Foreground,
            _ => fill.Colour
        };
        var anchor = Color.Parse(anchorColour.Hex).ToHsl();
        CuiCustomColour Recolour(CuiCustomColour colour)
        {
            if (colour.Hex == anchorColour.Hex) return colour with { Hex = targetHex };
            var original = Color.Parse(colour.Hex).ToHsl();
            // Shift the colour family and retain the authored tonal ordering.
            // The first/foreground colour maps exactly to the chosen accent;
            // other tones interpolate on either side without flattening stops.
            var lightness = original.L <= anchor.L && anchor.L > 0
                ? target.L * original.L / anchor.L
                : anchor.L < 1 ? target.L + (1 - target.L) * (original.L - anchor.L) / (1 - anchor.L) : target.L;
            var saturation = anchor.S > 0 ? target.S * original.S / anchor.S : target.S;
            var hue = (original.H - anchor.H + target.H + 360) % 360;
            var mapped = new HslColor(1, hue, Math.Clamp(saturation, 0, 1), Math.Clamp(lightness, 0, 1)).ToRgb();
            return colour with { Hex = $"#{mapped.R:X2}{mapped.G:X2}{mapped.B:X2}" };
        }
        var recoloured = fill with
        {
            Colour = Recolour(fill.Colour),
            Gradient = fill.Gradient is null ? null : fill.Gradient with
            { Stops = Array.AsReadOnly(fill.Gradient.Stops.Select(stop => stop with { Colour = Recolour(stop.Colour) }).ToArray()) },
            Pattern = fill.Pattern is null ? null : fill.Pattern with
            { Foreground = Recolour(fill.Pattern.Foreground), Background = Recolour(fill.Pattern.Background) }
        };
        return theme with { PrimaryAccent = recoloured };
    }
}
