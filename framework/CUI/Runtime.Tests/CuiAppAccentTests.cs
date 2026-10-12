using Avalonia.Media;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiAppAccentTests
{
    [Fact]
    public void App_defaults_remain_distinct_through_every_theme_and_appearance()
    {
        foreach (var theme in Enum.GetValues<CuiTheme>())
        foreach (var appearance in Enum.GetValues<CuiAppearance>())
        {
            var palettes = CuiAppAccentCatalog.Defaults.Select(app =>
                CuiSurfacePaletteCatalog.For(app.Surface, appearance, theme, null)).ToArray();
            Assert.Equal(7, palettes.Select(palette => palette.Accent).Distinct().Count());
            foreach (var palette in palettes)
            {
                Assert.NotEqual(palette.AccentPalette.Primary.Start, palette.AccentPalette.Primary.End);
                if (theme == CuiTheme.Professional)
                {
                    Assert.True(CuiContrast.Ratio(palette.Focus, palette.Panel) >= 3);
                    Assert.True(CuiContrast.Ratio(palette.AccentInk, palette.Accent) >= 4.5);
                }
            }
        }
    }

    [Fact]
    public void Scoped_colour_does_not_read_or_change_legacy_global_override()
    {
        var previousEnabled = CuiSurfacePaletteCatalog.OverrideAccent;
        var previousColour = CuiSurfacePaletteCatalog.AccentOverride;
        try
        {
            var red = CuiSurfacePaletteCatalog.For("Present", CuiAppearance.Bright, CuiTheme.Glow, null);
            CuiSurfacePaletteCatalog.OverrideAccent = true;
            CuiSurfacePaletteCatalog.AccentOverride = CuiAccentColour.Yellow;
            Assert.Equal(red, CuiSurfacePaletteCatalog.For("Present", CuiAppearance.Bright, CuiTheme.Glow, null));
            var chosen = CuiSurfacePaletteCatalog.For("Present", CuiAppearance.Bright, CuiTheme.Glow, "#1070B0");
            Assert.Equal(Color.Parse("#1070B0"), chosen.Accent);
            Assert.Equal(chosen.AccentPalette, CuiSurfacePaletteCatalog.For("Write", CuiAppearance.Bright, CuiTheme.Glow, "#1070B0").AccentPalette);
            Assert.NotEqual(chosen, CuiSurfacePaletteCatalog.For("Present", CuiAppearance.Bright, CuiTheme.Professional, "#1070B0"));
            Assert.True(CuiSurfacePaletteCatalog.OverrideAccent);
            Assert.Equal(CuiAccentColour.Yellow, CuiSurfacePaletteCatalog.AccentOverride);
        }
        finally
        {
            CuiSurfacePaletteCatalog.OverrideAccent = previousEnabled;
            CuiSurfacePaletteCatalog.AccentOverride = previousColour;
        }
    }

    [Theory]
    [InlineData("Red")]
    [InlineData("#abc")]
    [InlineData("#FF123456")]
    [InlineData("#12ZZ56")]
    public void Scoped_colour_rejects_invalid_or_transparent_preference(string value) =>
        Assert.Throws<ArgumentException>(() => CuiSurfacePaletteCatalog.For("Present", CuiAppearance.Dark, CuiTheme.Glow, value));
}
