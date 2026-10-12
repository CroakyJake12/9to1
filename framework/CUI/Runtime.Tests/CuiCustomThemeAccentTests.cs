using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiCustomThemeAccentTests
{
    [Fact]
    public void Explicit_primary_override_preserves_saved_theme_and_other_roles()
    {
        var theme = CuiCustomThemeContract.CreateDefault();
        theme = theme with { PrimaryAccent = new(CuiCustomFillKind.SolidColour, new("#A02244", 34, 12)) };
        var saved = CuiCustomThemeContract.Serialize(theme);
        Assert.Same(theme, CuiCustomThemeAccentResolver.Resolve(theme, null));
        var resolved = CuiCustomThemeAccentResolver.Resolve(theme, "#247baa");
        Assert.Equal(new("#247BAA", 34, 12), resolved.PrimaryAccent.Colour);
        Assert.Same(theme.SecondaryAccent, resolved.SecondaryAccent);
        Assert.Same(theme.TertiaryAccent, resolved.TertiaryAccent);
        Assert.Same(theme.Fonts, resolved.Fonts);
        Assert.Equal(saved, CuiCustomThemeContract.Serialize(theme));
        Assert.Throws<ArgumentException>(() => CuiCustomThemeAccentResolver.Resolve(theme, "Blue"));
    }

    [Theory]
    [InlineData(CuiCustomGradientKind.Linear)] [InlineData(CuiCustomGradientKind.Radial)] [InlineData(CuiCustomGradientKind.Conic)]
    public void Recoloured_gradients_keep_geometry_stops_and_each_colour_material(CuiCustomGradientKind kind)
    {
        var gradient = new CuiCustomGradient(kind, [new(0, new("#B02040", 12, 3)),
            new(.3, new("#D84060", 32, 11)), new(1, new("#F8C0D0", 56, 21))], 72, .2, .7, .8);
        var theme = CuiCustomThemeContract.CreateDefault() with
        { PrimaryAccent = new(CuiCustomFillKind.Gradient, new("#B02040"), gradient) };
        var rendered = CuiCustomThemeAccentResolver.Resolve(theme, "#1876AC");
        var actual = rendered.PrimaryAccent.Gradient!;
        Assert.Equal(gradient with { Stops = actual.Stops }, actual);
        Assert.Equal("#1876AC", actual.Stops[0].Colour.Hex);
        Assert.Equal(3, actual.Stops.Select(stop => stop.Colour.Hex).Distinct().Count());
        for (var i = 0; i < gradient.Stops.Count; ++i)
        {
            Assert.Equal(gradient.Stops[i].Offset, actual.Stops[i].Offset);
            Assert.Equal(gradient.Stops[i].Colour.TransparencyPercent, actual.Stops[i].Colour.TransparencyPercent);
            Assert.Equal(gradient.Stops[i].Colour.BackgroundBlur, actual.Stops[i].Colour.BackgroundBlur);
        }
        Assert.IsType<CuiCustomMaterialBrush>(CuiCustomThemeResourceApplier.CreateBackgroundBrush(rendered.PrimaryAccent));
    }

    [Theory]
    [InlineData(CuiCustomPatternKind.Stripes)] [InlineData(CuiCustomPatternKind.Checkerboard)] [InlineData(CuiCustomPatternKind.Dots)]
    public void Recoloured_patterns_preserve_regions_transparency_and_blur(CuiCustomPatternKind kind)
    {
        var pattern = new CuiCustomPattern(kind, new("#AA2040", 25, 7), new("#E8C0D0", 70, 18), 14, 5, 30);
        var theme = CuiCustomThemeContract.CreateDefault() with
        { PrimaryAccent = new(CuiCustomFillKind.Pattern, new("#AA2040"), Pattern: pattern) };
        var result = CuiCustomThemeAccentResolver.Resolve(theme, "#327FB4").PrimaryAccent.Pattern!;
        Assert.Equal("#327FB4", result.Foreground.Hex);
        Assert.NotEqual(result.Foreground.Hex, result.Background.Hex);
        Assert.Equal(pattern with { Foreground = result.Foreground, Background = result.Background }, result);
        Assert.Equal(pattern.Foreground with { Hex = result.Foreground.Hex }, result.Foreground);
        Assert.Equal(pattern.Background with { Hex = result.Background.Hex }, result.Background);
    }
}
