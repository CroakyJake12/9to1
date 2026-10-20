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
        public Preferences(string theme = "Glow") => Current = new(theme, 0, "account:test", false, new(), CuiAppearance.Bright);
        public CuiThemePreferenceSnapshot Current { get; private set; }
        public event EventHandler? Changed;
        public void Set(CuiAccentPreference accent)
        { Current = Current with { Revision = Current.Revision + 1, Accent = accent }; Changed?.Invoke(this, EventArgs.Empty); }
    }

    [Fact]
    public async Task Explicit_default_app_colour_recolours_custom_primary_and_none_restores_authored_fill()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var theme = CuiCustomThemeContract.CreateDefault("Authored theme");
            theme = theme with { PrimaryAccent = new(CuiCustomFillKind.SolidColour, new("#247BAA")) };
            var stored = CuiCustomThemeContract.Serialize(theme);
            var catalog = new CuiThemeDefinitionCatalog(); catalog.UpsertCustomTheme(theme);
            var source = new Preferences(theme.Name);
            using var context = new CuiThemePreferenceContext(source, catalog);
            using var loader = new CuiControlLoader(); loader.SetSurface("Present"); loader.SetThemePreferenceContext(context);
            var loaded = loader.LoadMarkup("<Button Content='Primary' AccentRole='Primary'/>");
            Assert.Empty(loaded.Diagnostics);
            var button = Assert.IsAssignableFrom<Button>(loaded.Root);
            static Color Fill(Button target) => Assert.IsAssignableFrom<ISolidColorBrush>(target.Background).Color;
            Assert.Equal(Color.Parse("#247BAA"), Fill(button));
            // Same base palette as None; the explicit override must still invalidate custom resources.
            source.Set(new(CuiAccentOverrideMode.AllApps, "#DB3545"));
            Assert.Equal(Color.Parse("#DB3545"), Fill(button));
            source.Set(CuiAccentPreference.Default);
            Assert.Equal(Color.Parse("#247BAA"), Fill(button));
            Assert.Equal(stored, CuiCustomThemeContract.Serialize(catalog.Resolve(theme.Name).CustomTheme!));
        }, CancellationToken.None);
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

    [Fact]
    public void Professional_missing_or_unknown_app_default_is_canonical_blue_across_appearances()
    {
        foreach (var appearance in Enum.GetValues<CuiAppearance>())
        {
            foreach (var surface in new[] { "Spaces", "UnknownSurface" })
            {
                var palette = CuiSurfacePaletteCatalog.For(surface, appearance, CuiTheme.Professional, null);
                var blue = CuiSurfacePaletteCatalog.For("Write", appearance, CuiTheme.Professional, null);
                Assert.Equal(blue.Accent, palette.Accent);
                Assert.Equal(blue.AccentSecondary, palette.AccentSecondary);
                Assert.Equal(blue.AccentStrong, palette.AccentStrong);
                Assert.Equal(blue.Panel, palette.Panel);
                Assert.Equal(blue.TideBase, palette.TideBase);
            }

            // Glow keeps the legacy Spaces hue; the Professional fallback must not leak.
            var glow = CuiSurfacePaletteCatalog.For("Spaces", appearance, CuiTheme.Glow, null);
            Assert.Equal(Color.Parse(appearance is CuiAppearance.Dark or CuiAppearance.SuperDark ? "#FFB87EFF" : "#FF9D5CFF"), glow.Accent);
        }
    }

    [Fact]
    public void Professional_known_app_defaults_are_preserved_across_appearances()
    {
        foreach (var appearance in Enum.GetValues<CuiAppearance>())
        {
            var accents = CuiAppAccentCatalog.Defaults.Select(app =>
                CuiSurfacePaletteCatalog.For(app.Surface, appearance, CuiTheme.Professional, null).Accent).ToArray();
            Assert.Equal(7, accents.Distinct().Count());

            foreach (var app in CuiAppAccentCatalog.Defaults)
            {
                var palette = CuiSurfacePaletteCatalog.For(app.Surface, appearance, CuiTheme.Professional, null);
                Assert.Equal(CuiSurfacePaletteCatalog.For("Spaces", appearance, CuiTheme.Professional, app.Colour).Accent, palette.Accent);
            }
        }
    }

    [Fact]
    public void Professional_known_owner_wins_over_different_surface_default()
    {
        foreach (var appearance in Enum.GetValues<CuiAppearance>())
        {
            var dataOnPresent = CuiSurfacePaletteCatalog.For("Present", appearance, CuiTheme.Professional, null, "app.data");
            Assert.Equal(CuiSurfacePaletteCatalog.For("Data", appearance, CuiTheme.Professional, null).Accent, dataOnPresent.Accent);

            var writeOnPresent = CuiSurfacePaletteCatalog.For("Present", appearance, CuiTheme.Professional, null, "app.write");
            Assert.Equal(CuiSurfacePaletteCatalog.For("Write", appearance, CuiTheme.Professional, null).Accent, writeOnPresent.Accent);
        }
    }

    [Fact]
    public void Professional_unknown_owner_on_known_surface_uses_blue_not_surface_default()
    {
        foreach (var appearance in Enum.GetValues<CuiAppearance>())
        {
            var professional = CuiSurfacePaletteCatalog.For("Present", appearance, CuiTheme.Professional, null, "app.unknown-owner");
            Assert.Equal(CuiSurfacePaletteCatalog.For("Write", appearance, CuiTheme.Professional, null).Accent, professional.Accent);

            // Compatibility behaviour for non-Professional themes is unchanged: an unknown
            // owner still borrows the supplied surface's known default.
            var glow = CuiSurfacePaletteCatalog.For("Present", appearance, CuiTheme.Glow, null, "app.unknown-owner");
            Assert.Equal(CuiSurfacePaletteCatalog.For("Present", appearance, CuiTheme.Glow, null).Accent, glow.Accent);
        }
    }

    [Fact]
    public void Professional_explicit_override_wins_over_blue_fallback()
    {
        var previousEnabled = CuiSurfacePaletteCatalog.OverrideAccent;
        var previousColour = CuiSurfacePaletteCatalog.AccentOverride;
        try
        {
            foreach (var appearance in Enum.GetValues<CuiAppearance>())
            {
                CuiSurfacePaletteCatalog.OverrideAccent = false;
                CuiSurfacePaletteCatalog.AccentOverride = null;

                var fallback = CuiSurfacePaletteCatalog.For("Spaces", appearance, CuiTheme.Professional, null);
                Assert.Equal(CuiSurfacePaletteCatalog.For("Write", appearance, CuiTheme.Professional, null).Accent, fallback.Accent);

                var scoped = CuiSurfacePaletteCatalog.For("Spaces", appearance, CuiTheme.Professional, "#A04470");
                Assert.Equal(CuiSurfacePaletteCatalog.For("Write", appearance, CuiTheme.Professional, "#A04470").Accent, scoped.Accent);

                CuiSurfacePaletteCatalog.OverrideAccent = true;
                CuiSurfacePaletteCatalog.AccentOverride = CuiAccentColour.Red;
                var global = CuiSurfacePaletteCatalog.For("Spaces", appearance, CuiTheme.Professional);
                var red = CuiAccentCatalog.Resolve(CuiAccentColour.Red, appearance);
                Assert.Equal(appearance is CuiAppearance.Dark or CuiAppearance.SuperDark ? red.Secondary : red.Primary, global.Accent);
                var ignoredByScoped = CuiSurfacePaletteCatalog.For("Spaces", appearance, CuiTheme.Professional, null);
                Assert.Equal(CuiSurfacePaletteCatalog.For("Write", appearance, CuiTheme.Professional, null).Accent, ignoredByScoped.Accent);
            }
        }
        finally
        {
            CuiSurfacePaletteCatalog.OverrideAccent = previousEnabled;
            CuiSurfacePaletteCatalog.AccentOverride = previousColour;
        }
    }

    [Fact]
    public async Task Professional_blue_fallback_does_not_recolour_scoped_custom_primary_without_override()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var theme = CuiCustomThemeContract.CreateDefault("Authored theme");
            theme = theme with { PrimaryAccent = new(CuiCustomFillKind.SolidColour, new("#247BAA")) };
            var stored = CuiCustomThemeContract.Serialize(theme);
            var catalog = new CuiThemeDefinitionCatalog();
            catalog.UpsertCustomTheme(theme);
            var source = new Preferences(theme.Name);
            using var context = new CuiThemePreferenceContext(source, catalog);
            using var loader = new CuiControlLoader();
            loader.SetSurface("Spaces");
            loader.SetThemePreferenceContext(context);
            var loaded = loader.LoadMarkup("<Button Content='Primary' AccentRole='Primary'/>");
            Assert.Empty(loaded.Diagnostics);
            var button = Assert.IsAssignableFrom<Avalonia.Controls.Button>(loaded.Root);
            static Color Fill(Avalonia.Controls.Button target) =>
                Assert.IsAssignableFrom<ISolidColorBrush>(target.Background).Color;
            Assert.Equal(Color.Parse("#247BAA"), Fill(button));
            Assert.Equal(CuiAccentPreference.Default, source.Current.EffectiveAccent);
            Assert.Equal(stored, CuiCustomThemeContract.Serialize(catalog.Resolve(theme.Name).CustomTheme!));
        }, CancellationToken.None);
    }
}
