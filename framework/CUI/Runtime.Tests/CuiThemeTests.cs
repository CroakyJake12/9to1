// CUI Theme Regression Tests — ported from PersonalisationTests.cs.
// Original source: 9to1 Workspace/shared/tests/Haven.Desktop.Tests/PersonalisationTests.cs
//
// Proves: all six themes resolve across all four appearances, accent override
// precedence, safe fallbacks, Glow baseline parity, nested DefaultTheme scoping,
// and that theme changes don't alter the control tree structure.

using Avalonia.Media;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Covers the CUI personalisation pipeline: six themes across all four
/// appearances, accent override precedence, safe fallbacks, Glow baseline parity,
/// nested DefaultTheme scoping, and theme expression scaling.
/// </summary>
public sealed class CuiThemeTests
{
    // === Original PersonalisationTests ported ===

    [Fact]
    public void Glow_palette_matches_the_pre_theme_baseline()
    {
        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
        CuiSurfacePaletteCatalog.OverrideAccent = false;
        CuiSurfacePaletteCatalog.AccentOverride = null;

        var home = CuiSurfacePaletteCatalog.For("Home", CuiAppearance.SuperDark);
        Assert.Equal(Color.Parse("#FF06090D"), home.TideBase);
        Assert.Equal<byte>(0xF5, home.Panel.A);
        Assert.Equal<byte>(0xF5, home.Panel2.A);
        Assert.Equal(CuiTheme.Glow, home.Theme);

        var bright = CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Bright);
        Assert.Equal(Colors.White, bright.TideBase);
        Assert.Equal(Color.Parse("#FF111111"), bright.Text);

        // Glow is the identity transform: hover keeps its original blend.
        var accent = Color.Parse("#FF3527FF");
        var darkSoft = Blend(Color.Parse("#FF121526"), accent, 0.20);
        var expectedHover = Blend(darkSoft, accent, 0.22);
        Assert.Equal(expectedHover, home.ButtonHover);
    }

    [Fact]
    public void Every_theme_resolves_for_every_surface_and_appearance()
    {
        // Defensive cleanup from any prior test that may have left state dirty
        CuiSurfacePaletteCatalog.OverrideAccent = false;
        CuiSurfacePaletteCatalog.AccentOverride = null;
        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
        try
        {
            foreach (var theme in Enum.GetValues<CuiTheme>())
            {
                CuiSurfacePaletteCatalog.ActiveTheme = theme;
                foreach (var appearance in Enum.GetValues<CuiAppearance>())
                foreach (var surface in new[] { "Home", "Chat", "Tasks", "Terminal", "Data" })
                {
                    var palette = CuiSurfacePaletteCatalog.For(surface, appearance);
                    Assert.Equal(theme, palette.Theme);
                    Assert.NotEqual(default, palette.TideBase);
                    Assert.NotEqual(default, palette.Accent);
                    Assert.True(palette.Panel.A > 0);
                }
            }
        }
        finally
        {
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
            CuiSurfacePaletteCatalog.OverrideAccent = false;
            CuiSurfacePaletteCatalog.AccentOverride = null;
        }
    }

    [Fact]
    public void Non_glow_themes_change_interaction_treatment_without_changing_text_colours()
    {
        CuiSurfacePaletteCatalog.OverrideAccent = false;
        CuiSurfacePaletteCatalog.AccentOverride = null;

        var glow = For(CuiTheme.Glow, "Chat", CuiAppearance.Dark);

        var retro = For(CuiTheme.Retro, "Chat", CuiAppearance.Dark);
        Assert.NotEqual(glow.ButtonHover, retro.ButtonHover);
        Assert.NotEqual(glow.Line, retro.Line);
        Assert.Equal(glow.Text, retro.Text);

        var playful = For(CuiTheme.Playful, "Chat", CuiAppearance.Dark);
        Assert.NotEqual(retro.ButtonHover, playful.ButtonHover);
        Assert.True(playful.Panel.A == 0xFF, "Playful panels should be fully opaque.");

        var bubble = For(CuiTheme.Bubble, "Chat", CuiAppearance.Dark);
        Assert.True(bubble.Panel.A < glow.Panel.A, "Bubble panels should be more translucent than Glow.");

        var cinematic = For(CuiTheme.Cinematic, "Chat", CuiAppearance.Dark);
        Assert.NotEqual(glow.Panel, cinematic.Panel);

        var professional = For(CuiTheme.Professional, "Chat", CuiAppearance.Dark);
        Assert.NotEqual(glow.Panel, professional.Panel);
        Assert.Equal<byte>(0xFF, professional.Panel.A);
        Assert.Equal<byte>(0xFF, professional.Focus.A);

        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
    }

    [Fact]
    public void Theme_expressions_scale_geometry_motion_and_shadow_distinctly()
    {
        var glow = CuiThemeCatalog.Resolve(CuiTheme.Glow);
        var bubble = CuiThemeCatalog.Resolve(CuiTheme.Bubble);
        var retro = CuiThemeCatalog.Resolve(CuiTheme.Retro);
        var playful = CuiThemeCatalog.Resolve(CuiTheme.Playful);
        var cinematic = CuiThemeCatalog.Resolve(CuiTheme.Cinematic);

        Assert.All(new[] { bubble.ControlRadiusScale, playful.ControlRadiusScale },
            scale => Assert.True(scale > glow.ControlRadiusScale));
        Assert.True(retro.ControlRadiusScale < glow.ControlRadiusScale);
        Assert.NotEqual(glow.MotionDurationScale, retro.MotionDurationScale);
        Assert.True(cinematic.ShadowOpacityScale > glow.ShadowOpacityScale);
    }

    [Fact]
    public void Accent_override_replaces_surface_hues_while_off_keeps_them()
    {
        CuiSurfacePaletteCatalog.OverrideAccent = false;
        CuiSurfacePaletteCatalog.AccentOverride = null;
        try
        {
            // Dark appearances emphasise the secondary anchor; light ones the primary.
            CuiSurfacePaletteCatalog.OverrideAccent = true;
            CuiSurfacePaletteCatalog.AccentOverride = CuiAccentColour.Cyan;
            var dark = CuiSurfacePaletteCatalog.For("Tasks", CuiAppearance.Dark);
            var darkAnchors = CuiAccentCatalog.Resolve(CuiAccentColour.Cyan, CuiAppearance.Dark);
            Assert.Equal(darkAnchors.Secondary, dark.Accent);
            Assert.Equal(darkAnchors.Primary, dark.AccentSecondary);

            var light = CuiSurfacePaletteCatalog.For("Tasks", CuiAppearance.Bright);
            var lightAnchors = CuiAccentCatalog.Resolve(CuiAccentColour.Cyan, CuiAppearance.Bright);
            Assert.Equal(lightAnchors.Primary, light.Accent);

            CuiSurfacePaletteCatalog.OverrideAccent = false;
            CuiSurfacePaletteCatalog.AccentOverride = null;
            var restored = CuiSurfacePaletteCatalog.For("Tasks", CuiAppearance.Dark);
            Assert.Equal(Color.Parse("#FFFF5B19"), restored.AccentSecondary);
        }
        finally
        {
            CuiSurfacePaletteCatalog.OverrideAccent = false;
            CuiSurfacePaletteCatalog.AccentOverride = null;
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
        }
    }

    [Fact]
    public void All_thirteen_accent_palettes_resolve_for_both_appearance_families()
    {
        Assert.Equal(13, CuiAccentCatalog.Colours.Count);
        foreach (var colour in CuiAccentCatalog.Colours)
        {
            var light = CuiAccentCatalog.Resolve(colour, CuiAppearance.Bright);
            var dark = CuiAccentCatalog.Resolve(colour, CuiAppearance.SuperDark);
            Assert.NotEqual(light.Primary, light.Strong);
            Assert.NotEqual(dark.Primary, dark.Strong);
            Assert.False(string.IsNullOrWhiteSpace(CuiAccentCatalog.Name(colour)));
        }
    }

    [Fact]
    public void Contrast_critical_palettes_keep_safe_relationships()
    {
        // Yellow and Lime need deep strong anchors to survive contrast guards.
        foreach (var colour in new[] { CuiAccentColour.Yellow, CuiAccentColour.Lime })
        {
            var anchors = CuiAccentCatalog.Resolve(colour, CuiAppearance.SuperDark);
            Assert.True(Luminance(anchors.Strong) < Luminance(anchors.Primary),
                $"{colour} strong anchor must stay darker than primary.");
        }

        // Monotone stays grayscale and keeps strong contrast against its surface.
        foreach (var appearance in new[] { CuiAppearance.Bright, CuiAppearance.SuperDark })
        {
            var monotone = CuiAccentCatalog.Resolve(CuiAccentColour.Monotone, appearance);
            Assert.Equal(monotone.Primary.R, monotone.Primary.G);
            Assert.Equal(monotone.Primary.G, monotone.Primary.B);
            var background = appearance == CuiAppearance.Bright ? "#FFFFFFFF" : "#FF06090D";
            Assert.True(Math.Abs(Luminance(monotone.Primary) - Luminance(Color.Parse(background))) > 150,
                $"{appearance} Monotone must contrast its surface.");
        }

        // Strawberry stays visibly distinct from Red and Pink in the same family.
        var strawberry = ExtractRgb(CuiAccentCatalog.Resolve(CuiAccentColour.Strawberry, CuiAppearance.Dark).Primary);
        var red = ExtractRgb(CuiAccentCatalog.Resolve(CuiAccentColour.Red, CuiAppearance.Dark).Primary);
        var pink = ExtractRgb(CuiAccentCatalog.Resolve(CuiAccentColour.Pink, CuiAppearance.Dark).Primary);
        Assert.True(Distance(strawberry, red) > 15 && Distance(strawberry, pink) > 15);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("BogusTheme")]
    public void Unknown_theme_names_fall_back_to_glow(string? name)
    {
        Assert.Equal(CuiTheme.Glow, CuiThemeCatalog.Parse(name));
        Assert.Equal(CuiTheme.Glow, CuiThemeCatalog.Resolve((CuiTheme)999).Theme);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("NotAPalette")]
    public void Unknown_accent_names_do_not_enable_override(string? name)
    {
        Assert.Null(CuiAccentCatalog.Parse(name));
    }

    // === NEW: DefaultTheme scope tests ===

    [Fact]
    public void DefaultTheme_resolves_to_global_default()
    {
        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Bubble;
        var resolved = CuiThemeScope.ResolveThemeName("Default", CuiSurfacePaletteCatalog.ActiveTheme);
        Assert.Equal(CuiTheme.Bubble, resolved); // "Default" uses global setting
        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
    }

    [Fact]
    public void Theme_scope_uses_supplied_default_instead_of_process_global()
    {
        var saved = CuiSurfacePaletteCatalog.ActiveTheme;
        try
        {
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Cinematic;
            Assert.Equal(CuiTheme.Bubble, CuiThemeScope.ResolveThemeName("Default", CuiTheme.Bubble));
            Assert.Equal(CuiTheme.Bubble, CuiThemeScope.ResolveThemeName(null, CuiTheme.Bubble));
            Assert.Equal(CuiTheme.Bubble, CuiThemeScope.ResolveThemeName("Unknown", CuiTheme.Bubble));
        }
        finally
        {
            CuiSurfacePaletteCatalog.ActiveTheme = saved;
        }
    }

    [Fact]
    public void Applying_global_theme_updates_active_theme_for_subsequent_surface_loads()
    {
        var saved = CuiSurfacePaletteCatalog.ActiveTheme;
        try
        {
            CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Retro);
            Assert.Equal(CuiTheme.Retro, CuiSurfacePaletteCatalog.ActiveTheme);
            Assert.Equal(CuiTheme.Retro, CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark).Theme);
            Assert.Equal(CuiTheme.Retro, new Runtime.CuiControlLoader().CurrentTheme);
        }
        finally
        {
            CuiSurfacePaletteCatalog.ActiveTheme = saved;
        }
    }

    [Fact]
    public void Loader_reads_global_theme_at_load_time_and_preserves_explicit_scope()
    {
        var saved = CuiSurfacePaletteCatalog.ActiveTheme;
        try
        {
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
            var loader = new Runtime.CuiControlLoader();
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Bubble;
            var (root, diagnostics) = loader.LoadMarkup("""
                <Cui id="theme.loader" version="1">
                  <DefaultTheme value="Default">
                    <Page id="root">
                      <DefaultTheme value="Retro"><Border id="retro" /></DefaultTheme>
                    </Page>
                  </DefaultTheme>
                </Cui>
                """);

            Assert.Empty(diagnostics);
            Assert.NotNull(root);
            Assert.Equal(CuiTheme.Bubble, loader.CurrentTheme);
            Assert.Equal(CuiTheme.Bubble, Assert.IsType<Avalonia.Controls.ResourceDictionary>(root.Resources.MergedDictionaries.Last())["CuiTheme"]);
            var page = Assert.IsType<Avalonia.Controls.Panel>(root);
            var retro = Assert.IsType<Avalonia.Controls.Border>(Assert.Single(page.Children));
            Assert.Equal(CuiTheme.Retro, Assert.IsType<Avalonia.Controls.ResourceDictionary>(retro.Resources.MergedDictionaries.Last())["CuiTheme"]);
        }
        finally
        {
            CuiSurfacePaletteCatalog.ActiveTheme = saved;
        }
    }

    [Fact]
    public void Native_hosts_read_the_existing_Haven_theme_preference_without_writing_it()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cui-theme-preference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "preferences.json");
        try
        {
            Assert.Equal(CuiTheme.Glow, CuiThemePreferenceReader.Read(directory));

            var first = """{"havenUiThemeName":"Bubble","unrelatedSetting":true}""";
            File.WriteAllText(path, first);
            Assert.Equal(CuiTheme.Bubble, CuiThemePreferenceReader.Read(directory));
            Assert.Equal(first, File.ReadAllText(path));

            File.WriteAllText(path, """{"havenUiThemeName":"Retro"}""");
            Assert.Equal(CuiTheme.Retro, CuiThemePreferenceReader.Read(directory));

            File.WriteAllText(path, """{"havenUiThemeName":"NotATheme"}""");
            Assert.Equal(CuiTheme.Glow, CuiThemePreferenceReader.Read(directory));
            File.WriteAllText(path, "{" );
            Assert.Equal(CuiTheme.Glow, CuiThemePreferenceReader.Read(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DefaultTheme_Glow_resolves_to_Glow_regardless_of_global()
    {
        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Cinematic;
        var resolved = CuiThemeScope.ResolveThemeName("Glow", CuiTheme.Glow);
        Assert.Equal(CuiTheme.Glow, resolved); // Explicit "Glow" pins to Glow
    }

    [Theory]
    [InlineData("Glow", CuiTheme.Glow)]
    [InlineData("Bubble", CuiTheme.Bubble)]
    [InlineData("Retro", CuiTheme.Retro)]
    [InlineData("Playful", CuiTheme.Playful)]
    [InlineData("Cinematic", CuiTheme.Cinematic)]
    [InlineData("professional", CuiTheme.Professional)]
    [InlineData("Professional", CuiTheme.Professional)]
    [InlineData("glow", CuiTheme.Glow)]
    [InlineData("bubble", CuiTheme.Bubble)]
    [InlineData("default", CuiTheme.Glow)] // "default" uses global
    public void All_theme_names_resolve_correctly(string name, CuiTheme expected)
    {
        var globalDefault = CuiTheme.Glow;
        var resolved = CuiThemeScope.ResolveThemeName(name, globalDefault);
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Null_or_empty_theme_name_uses_global_default()
    {
        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Cinematic;
        Assert.Equal(CuiTheme.Cinematic, CuiThemeScope.ResolveThemeName(null, CuiTheme.Cinematic));
        Assert.Equal(CuiTheme.Cinematic, CuiThemeScope.ResolveThemeName("", CuiTheme.Cinematic));

        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Retro;
        Assert.Equal(CuiTheme.Retro, CuiThemeScope.ResolveThemeName("   ", CuiTheme.Retro));

        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
    }

    [Fact]
    public void Invalid_theme_name_falls_back_to_global_default()
    {
        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Playful;
        Assert.Equal(CuiTheme.Playful, CuiThemeScope.ResolveThemeName("NeonRetro", CuiTheme.Playful));

        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
        Assert.Equal(CuiTheme.Glow, CuiThemeScope.ResolveThemeName("Bogus", CuiTheme.Glow));
    }

    [Fact]
    public void ThemeScopeStack_push_pop_restores_parent()
    {
        var stack = new CuiThemeScopeStack(CuiTheme.Glow);
        Assert.Equal(CuiTheme.Glow, stack.Current);

        stack.Push(CuiTheme.Bubble);
        Assert.Equal(CuiTheme.Bubble, stack.Current);

        stack.Push(CuiTheme.Retro);
        Assert.Equal(CuiTheme.Retro, stack.Current);

        stack.Pop();
        Assert.Equal(CuiTheme.Bubble, stack.Current);

        stack.Pop();
        Assert.Equal(CuiTheme.Glow, stack.Current);
    }

    [Fact]
    public void ThemeScopeStack_cannot_pop_below_initial()
    {
        var stack = new CuiThemeScopeStack(CuiTheme.Glow);
        stack.Pop(); // Should not crash
        Assert.Equal(CuiTheme.Glow, stack.Current);
    }

    [Fact]
    public void Nested_theme_scopes_inherit_and_restore()
    {
        // Simulates:
        // <DefaultTheme="Glow">
        //   <DefaultTheme="Bubble">
        //     <DefaultTheme="Retro">
        //       ...
        //     </DefaultTheme>  ← back to Bubble
        //   </DefaultTheme>  ← back to Glow
        // </DefaultTheme>
        var stack = new CuiThemeScopeStack(CuiTheme.Glow);
        Assert.Equal(CuiTheme.Glow, stack.Current);

        stack.Push(CuiTheme.Glow); // Outer scope
        Assert.Equal(CuiTheme.Glow, stack.Current);

        stack.Push(CuiTheme.Bubble); // Nested scope
        Assert.Equal(CuiTheme.Bubble, stack.Current);

        stack.Push(CuiTheme.Retro); // Innermost scope
        Assert.Equal(CuiTheme.Retro, stack.Current);

        stack.Pop();
        Assert.Equal(CuiTheme.Bubble, stack.Current); // Restores Bubble

        stack.Pop();
        Assert.Equal(CuiTheme.Glow, stack.Current); // Restores Glow
    }

    [Fact]
    public void Theme_expression_values_are_distinct_per_theme()
    {
        var expressions = CuiThemeCatalog.All;
        Assert.Equal(6, expressions.Count);

        var radii = expressions.Select(e => e.ControlRadiusScale).Distinct().ToList();
        Assert.True(radii.Count >= 3, "At least 3 distinct control radius scales expected");

        var motions = expressions.Select(e => e.MotionDurationScale).Distinct().ToList();
        Assert.True(motions.Count >= 3, "At least 3 distinct motion duration scales expected");

        var shadows = expressions.Select(e => e.ShadowOpacityScale).Distinct().ToList();
        Assert.True(shadows.Count >= 3, "At least 3 distinct shadow opacity scales expected");
        Assert.All(expressions, expression =>
        {
            Assert.True(double.IsFinite(expression.SpacingScale) && expression.SpacingScale > 0d);
            Assert.True(double.IsFinite(expression.TypographyScale) && expression.TypographyScale > 0d);
            Assert.True(double.IsFinite(expression.ControlHeightScale) && expression.ControlHeightScale > 0d);
            Assert.True(double.IsFinite(expression.ElevationScale) && expression.ElevationScale >= 0d);
        });
    }

    [Fact]
    public void Glow_baseline_radius_values_match_original()
    {
        var glow = CuiThemeCatalog.Resolve(CuiTheme.Glow);
        Assert.Equal(1.0, glow.ControlRadiusScale);
        Assert.Equal(1.0, glow.CardRadiusScale);
        Assert.Equal(1.0, glow.PopupRadiusScale);
        Assert.Equal(1.0, glow.MotionDurationScale);
        Assert.Equal(1.0, glow.ShadowOpacityScale);
        Assert.Equal(1.0, glow.BorderIntensity);
    }

    [Fact]
    public void Bubble_theme_has_translucent_panels()
    {
        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Bubble;
        var dark = CuiSurfacePaletteCatalog.For("Chat", CuiAppearance.Dark);
        var glowDark = For(CuiTheme.Glow, "Chat", CuiAppearance.Dark);
        Assert.True(dark.Panel.A < glowDark.Panel.A, "Bubble panels should be more translucent than Glow.");
        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
    }

    [Fact]
    public void Retro_theme_has_sharper_corners()
    {
        var retro = CuiThemeCatalog.Resolve(CuiTheme.Retro);
        var glow = CuiThemeCatalog.Resolve(CuiTheme.Glow);
        Assert.True(retro.ControlRadiusScale < glow.ControlRadiusScale,
            "Retro should have sharper (smaller) control radii.");
        Assert.True(retro.CardRadiusScale < glow.CardRadiusScale,
            "Retro should have sharper (smaller) card radii.");
    }

    [Fact]
    public void Playful_theme_has_opaque_panels()
    {
        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Playful;
        var dark = CuiSurfacePaletteCatalog.For("Chat", CuiAppearance.Dark);
        Assert.True(dark.Panel.A == 0xFF, "Playful panels should be fully opaque.");
        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
    }

    [Fact]
    public void Cinematic_theme_has_heavier_shadows()
    {
        var cinematic = CuiThemeCatalog.Resolve(CuiTheme.Cinematic);
        var glow = CuiThemeCatalog.Resolve(CuiTheme.Glow);
        Assert.True(cinematic.ShadowOpacityScale > glow.ShadowOpacityScale,
            "Cinematic should have heavier shadows than Glow.");
    }

    [Fact]
    public void All_four_appearances_resolve_for_each_theme()
    {
        CuiSurfacePaletteCatalog.OverrideAccent = false;
        CuiSurfacePaletteCatalog.AccentOverride = null;

        foreach (var theme in Enum.GetValues<CuiTheme>())
        {
            CuiSurfacePaletteCatalog.ActiveTheme = theme;
            foreach (var appearance in Enum.GetValues<CuiAppearance>())
            {
                var palette = CuiSurfacePaletteCatalog.For("Home", appearance);
                Assert.Equal(theme, palette.Theme);
                Assert.True(palette.Text.A > 0, $"{theme}/{appearance}: Text should not be transparent");
                Assert.True(palette.Panel.A > 0, $"{theme}/{appearance}: Panel should not be transparent");
            }
        }

        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
    }

    [Fact]
    public void SurfacePaletteCatalog_For_with_explicit_theme_override_works()
    {
        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
        var overridden = CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, CuiTheme.Cinematic);
        Assert.Equal(CuiTheme.Cinematic, overridden.Theme);
        // Original should be unchanged
        Assert.Equal(CuiTheme.Glow, CuiSurfacePaletteCatalog.ActiveTheme);
    }

    [Fact]
    public void Accent_palette_has_three_tiers()
    {
        var palette = CuiAccentPalette.FromAnchors(
            Color.Parse("#FF2563EB"),
            Color.Parse("#FF4D82F5"),
            Color.Parse("#FF1643AF"),
            Colors.White,
            Color.Parse("#FFDAE4FB"),
            Color.Parse("#FF161A2A"));

        Assert.NotEqual(palette.Primary.Start, palette.Primary.End);
        Assert.NotEqual(palette.Secondary.Start, palette.Secondary.End);
        Assert.NotEqual(palette.Tertiary.Start, palette.Tertiary.End);
    }

    [Fact]
    public void Theme_name_catalog_returns_correct_names()
    {
        Assert.Equal("Glow", CuiThemeCatalog.Name(CuiTheme.Glow));
        Assert.Equal("Bubble", CuiThemeCatalog.Name(CuiTheme.Bubble));
        Assert.Equal("Retro", CuiThemeCatalog.Name(CuiTheme.Retro));
        Assert.Equal("Playful", CuiThemeCatalog.Name(CuiTheme.Playful));
        Assert.Equal("Cinematic", CuiThemeCatalog.Name(CuiTheme.Cinematic));
        Assert.Equal("Professional", CuiThemeCatalog.Name(CuiTheme.Professional));
    }

    [Fact]
    public void Accessibility_profile_applies_high_contrast_reduced_motion_and_display_scaling()
    {
        var resources = new Avalonia.Controls.ResourceDictionary();
        var settings = new CuiAccessibilitySettings
        {
            HighContrast = true,
            ReduceMotion = true,
            DisplayScale = 1.5d
        };
        var source = CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, CuiTheme.Bubble);

        CuiThemeResourceApplier.ApplyToResources(resources, source, settings);

        var text = Assert.IsType<SolidColorBrush>(resources["CuiTextBrush"]).Color;
        var background = Assert.IsType<SolidColorBrush>(resources["CuiBackgroundBrush"]).Color;
        Assert.True(CuiContrast.Ratio(text, background) >= 7d);
        Assert.Equal(0d, Assert.IsType<double>(resources["CuiMotionDurationScale"]));
        Assert.Equal(1.5d, Assert.IsType<double>(resources["CuiDisplayScale"]));
        Assert.Equal(CuiTypography.BodySize * CuiThemeCatalog.Resolve(CuiTheme.Bubble).TypographyScale * 1.5d,
            Assert.IsType<double>(resources["CuiFontSizeBody"]));
        Assert.Equal(3d, Assert.IsType<double>(resources["CuiFocusIndicatorThickness"]));
        Assert.Equal("IconAndLabel", Assert.IsType<string>(resources["CuiStateCommunication"]));
    }

    [Fact]
    public void Accessibility_defaults_include_interface_and_code_font_fallbacks()
    {
        Assert.StartsWith("Montserrat,", CuiTypography.InterfaceFontFamily);
        Assert.Contains("Segoe UI", CuiTypography.InterfaceFontFamily);
        Assert.Contains("sans-serif", CuiTypography.InterfaceFontFamily);
        Assert.Contains("Cascadia Mono", CuiTypography.CodeFontFamily);
        Assert.Contains("monospace", CuiTypography.CodeFontFamily);
    }

    [Fact]
    public void Contrast_correction_meets_requested_ratio_without_changing_background()
    {
        var background = Color.Parse("#FFE3E3E3");
        var corrected = CuiContrast.EnsureForegroundContrast(Color.Parse("#FFBEBEBE"), background);

        Assert.True(CuiContrast.Ratio(corrected, background) >= 7d);
        Assert.Equal(255, corrected.A);
        Assert.Equal(Color.Parse("#FFE3E3E3"), background);
    }

    [Fact]
    public void Localization_context_formats_culture_and_selects_rtl_flow_direction()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("ar-EG");
        var context = new CuiLocalizationContext(culture);
        var resources = new TestStringResources();

        Assert.Equal(Avalonia.Media.FlowDirection.RightToLeft, context.FlowDirection);
        Assert.Equal("مرحبا", context.GetString(resources, "greeting"));
        Assert.Equal("", context.GetString(resources, "empty"));
        Assert.Equal("fallback", context.GetString(resources, "fallback"));
        Assert.Contains(culture.NumberFormat.NumberDecimalSeparator, context.FormatNumber(12.5d));
        var root = new Avalonia.Controls.Grid();
        context.ApplyTo(root);
        Assert.Equal(Avalonia.Media.FlowDirection.RightToLeft, root.FlowDirection);
    }

    [Fact]
    public void Accessibility_semantics_apply_name_description_role_focus_order_and_shortcut()
    {
        var button = new Avalonia.Controls.Button();
        new CuiAccessibilitySemantics(
            AccessibleName: "Save document",
            AccessibleDescription: "Saves the current document",
            Role: AutomationControlType.Button,
            TabIndex: 4,
            Focusable: false,
            Shortcut: "Ctrl+S").ApplyTo(button);

        Assert.Equal("Save document", AutomationProperties.GetName(button));
        Assert.Equal("Saves the current document", AutomationProperties.GetHelpText(button));
        Assert.Equal(AutomationControlType.Button, AutomationProperties.GetControlTypeOverride(button));
        Assert.Equal(4, button.TabIndex);
        Assert.False(button.Focusable);
        Assert.False(button.IsTabStop);
        Assert.Equal("Ctrl+S", CuiAccessibilityProperties.GetShortcut(button));
    }

    [Fact]
    public void Accessibility_settings_reject_invalid_display_scales()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CuiAccessibilitySettings { DisplayScale = 0d }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CuiAccessibilitySettings { DisplayScale = double.NaN }.Validate());
    }

    // === DefaultTheme parsing in CUI markup ===

    [Fact]
    public void DefaultTheme_element_is_parsed_as_theme_scope()
    {
        var cui = """
            <Cui id="test.theme" version="1">
              <DefaultTheme value="Bubble">
                <Page id="root">
                  <TextBlock text="Themed" />
                </Page>
              </DefaultTheme>
            </Cui>
            """;

        var parser = new Language.CuiRichParser();
        var doc = parser.Parse(cui, "theme-test.cui");

        Assert.Empty(parser.Diagnostics.Diagnostics);
        Assert.Single(doc.Components);
        var root = doc.Components[0];
        Assert.True(root.IsThemeScope);
        Assert.Equal("Bubble", root.DefaultTheme);
        Assert.Single(root.Children); // The Page inside
    }

    [Fact]
    public void DefaultTheme_with_attribute_value_is_parsed()
    {
        var cui = """
            <Cui id="test.attr" version="1">
              <DefaultTheme value="Retro">
                <Page id="root">
                  <TextBlock text="Retro" />
                </Page>
              </DefaultTheme>
            </Cui>
            """;

        var parser = new Language.CuiRichParser();
        var doc = parser.Parse(cui, "attr-test.cui");

        Assert.Empty(parser.Diagnostics.Diagnostics);
        var root = doc.Components[0];
        Assert.True(root.IsThemeScope);
        Assert.Equal("Retro", root.DefaultTheme);
    }

    [Fact]
    public void DefaultTheme_element_loads_and_applies_theme_via_control_loader()
    {
        var cui = """
            <Cui id="test.loader.theme" version="1">
              <DefaultTheme value="Cinematic">
                <Page id="root">
                  <TextBlock text="Cinematic themed" />
                </Page>
              </DefaultTheme>
            </Cui>
            """;

        var result = CuiHeadlessRenderer.Render(cui, "loader-theme.cui");
        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        Assert.NotNull(result.Root);
        // DefaultTheme scope (virtual) + Page(Panel) + TextBlock = 2 visual controls
        Assert.Equal(2, result.ControlCount);
    }

    [Fact]
    public void Nested_DefaultTheme_scopes_load_correctly()
    {
        var cui = """
            <Cui id="test.nested.theme" version="1">
              <DefaultTheme value="Glow">
                <Page id="root">
                  <TextBlock text="Glow parent" />
                  <DefaultTheme value="Bubble">
                    <Border id="bubble-zone">
                      <TextBlock text="Bubble child" />
                    </Border>
                  </DefaultTheme>
                  <TextBlock text="Glow resume" />
                </Page>
              </DefaultTheme>
            </Cui>
            """;

        var result = CuiHeadlessRenderer.Render(cui, "nested-theme.cui");
        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        Assert.NotNull(result.Root);
        // Outer DefaultTheme scope (virtual) → Page(Panel) + TextBlock
        // + Inner DefaultTheme scope (virtual) → Border + TextBlock
        // + TextBlock = 5 visual controls
        Assert.Equal(5, result.ControlCount);
    }

    // === Helpers ===

    private static CuiPalette For(CuiTheme theme, string surface, CuiAppearance appearance)
    {
        var saved = CuiSurfacePaletteCatalog.ActiveTheme;
        CuiSurfacePaletteCatalog.ActiveTheme = theme;
        try
        {
            return CuiSurfacePaletteCatalog.For(surface, appearance);
        }
        finally
        {
            CuiSurfacePaletteCatalog.ActiveTheme = saved;
        }
    }

    private static (byte R, byte G, byte B) ExtractRgb(Color color) =>
        (color.R, color.G, color.B);

    private static double Distance((byte R, byte G, byte B) first, (byte R, byte G, byte B) second) =>
        Math.Sqrt(Math.Pow(first.R - second.R, 2) + Math.Pow(first.G - second.G, 2) + Math.Pow(first.B - second.B, 2));

    private static double Luminance(Color color) =>
        (0.2126d * color.R) + (0.7152d * color.G) + (0.0722d * color.B);

    private static double Luminance(string hex) => Luminance(Color.Parse(hex));

    private static Color Blend(Color first, Color second, double secondWeight)
    {
        var weight = Math.Clamp(secondWeight, 0, 1);
        return Color.FromArgb(
            255,
            (byte)Math.Round(first.R + (second.R - first.R) * weight),
            (byte)Math.Round(first.G + (second.G - first.G) * weight),
            (byte)Math.Round(first.B + (second.B - first.B) * weight));
    }

    private sealed class TestStringResources : ICuiStringResources
    {
        public string? GetString(string key, System.Globalization.CultureInfo culture) => key switch
        {
            "greeting" when culture.Name == "ar-EG" => "مرحبا",
            "empty" => string.Empty,
            "fallback" when culture == System.Globalization.CultureInfo.InvariantCulture => "fallback",
            _ => null
        };
    }
}
