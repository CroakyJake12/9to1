using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Headless;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiAppAccentTests
{
    [Fact]
    public async Task Live_app_scopes_follow_modes_without_replacing_controls_or_changing_another_app_override()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var source = new Preferences();
            using var context = new CuiThemePreferenceContext(source);
            using var present = new CuiControlLoader(); using var write = new CuiControlLoader();
            present.SetSurface("Present"); write.SetSurface("Write");
            present.SetThemePreferenceContext(context); write.SetThemePreferenceContext(context);
            const string markup = "<Button ID='accent' Content='Document action' Background='{Resource CuiAccentBrush}'/>";
            var p = present.LoadMarkup(markup); var w = write.LoadMarkup(markup);
            Assert.Empty(p.Diagnostics); Assert.Empty(w.Diagnostics);
            var pButton = Assert.IsAssignableFrom<Button>(p.Root); var wButton = Assert.IsAssignableFrom<Button>(w.Root);
            static Color Accent(Button button) => Assert.IsAssignableFrom<IGradientBrush>(button.Background).GradientStops[0].Color;
            var defaultPresent = Accent(pButton); var defaultWrite = Accent(wButton);
            Assert.NotEqual(defaultPresent, defaultWrite);
            source.Set(new(CuiAccentOverrideMode.AllApps, "#1070B0"));
            Assert.Equal(Accent(pButton), Accent(wButton));
            source.Set(new(CuiAccentOverrideMode.IndividualApps, "#1070B0",
                new Dictionary<string, string> { ["app.present"] = "#A04470" }));
            Assert.Equal(Color.Parse("#A04470"), Accent(pButton));
            Assert.Equal(defaultWrite, Accent(wButton));
            source.Set(CuiAccentPreference.Default);
            Assert.Equal(defaultPresent, Accent(pButton)); Assert.Equal(defaultWrite, Accent(wButton));
            Assert.Same(p.Root, pButton); Assert.Equal("Document action", pButton.Content);
        }, CancellationToken.None);
    }

    private sealed class Preferences : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; private set; } = new("Glow", 0, "account:test", false, new(), CuiAppearance.Bright);
        public event EventHandler? Changed;
        public void Set(CuiAccentPreference accent)
        { Current = Current with { Revision = Current.Revision + 1, Accent = accent }; Changed?.Invoke(this, EventArgs.Empty); }
    }

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
