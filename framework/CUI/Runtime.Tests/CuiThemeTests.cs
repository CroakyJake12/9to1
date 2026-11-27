// CUI Theme Regression Tests — ported from PersonalisationTests.cs.
// Original source: 9to1 Workspace/shared/tests/Haven.Desktop.Tests/PersonalisationTests.cs
//
// Proves: all nine themes resolve across all four appearances, accent override
// precedence, safe fallbacks, Glow baseline parity, nested DefaultTheme scoping,
// and that theme changes don't alter the control tree structure.

using Avalonia.Media;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Covers the CUI personalisation pipeline: nine themes across all four
/// appearances, accent override precedence, safe fallbacks, Glow baseline parity,
/// nested DefaultTheme scoping, and theme expression scaling.
/// </summary>
public sealed class CuiThemeTests
{
    [Theory]
    [InlineData("Home", "app.home", "#405BD2")]
    [InlineData("Spaces", "app.spaces", "#7650C7")]
    [InlineData("Dev", "app.dev", "#B55224")]
    [InlineData("Build", "app.dev", "#B55224")]
    [InlineData("Browse", "app.browse", "#2566B6")]
    public void Native_glow_scope_uses_the_atlas_app_anchor_and_preserves_professional_defaults(string surface, string appId, string colour)
    {
        Assert.Equal(appId, CuiAppAccentCatalog.IdentityFor(surface));
        Assert.Equal(colour, CuiGlowAppAccentCatalog.Find(appId)?.Colour);
        var glow = CuiSurfacePaletteCatalog.For(surface, CuiAppearance.Dark, CuiTheme.Glow, null, appId);
        // Dark palette assembly keeps the source anchor in AccentSecondary and uses its
        // existing 22% white tint for Accent. The raw source is not the action ramp midpoint.
        Assert.Equal(Color.Parse(colour), glow.AccentSecondary);
        Assert.Equal(Color.Parse(colour), CuiSurfacePaletteCatalog.For(surface, CuiAppearance.Bright, CuiTheme.Glow, null, appId).Accent);
        var explicitGlow = CuiSurfacePaletteCatalog.For(surface, CuiAppearance.Dark, CuiTheme.Glow, "#E78D32", appId);
        Assert.Equal(Color.Parse("#E78D32"), explicitGlow.AccentSecondary);
        var professional = CuiSurfacePaletteCatalog.For(surface, CuiAppearance.Dark, CuiTheme.Professional, null, appId);
        Assert.Equal(Color.Parse("#5585EF"), professional.Accent); // Existing #2563EB dark tint, unchanged.
    }

    // === Original PersonalisationTests ported ===

    [Theory]
    [InlineData("#405BD2")]
    [InlineData("#E78D32")]
    [InlineData("#999999")]
    public void Glow_dark_fields_follow_the_scoped_hue_without_becoming_accent_fills(string accent)
    {
        var palette = CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, CuiTheme.Glow, accent);
        Assert.Equal(Color.Parse(accent), palette.AccentSecondary);
        Assert.NotEqual(palette.TideBase, palette.Panel2);
        var source = Color.Parse(accent).ToHsl();
        var field = palette.TideBase.ToHsl();
        if (source.S < .025) Assert.Equal(0, field.S);
        else
        {
            Assert.InRange(Math.Abs(source.H - field.H), 0, 2);
            Assert.InRange(field.S, .20, .44);
        }
        foreach (var surface in new[] { palette.TideBase, palette.TideColour, palette.Panel, palette.Panel2, palette.ButtonHover })
            Assert.True(CuiContrast.Ratio(palette.Muted, surface) >= 4.5);
        var bright = CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Bright, CuiTheme.Glow, accent);
        Assert.Equal(Colors.White, bright.TideBase);
    }

    [Theory]
    [InlineData("#405BD2", "#11131D", "#181C2A", "#1E2234")]
    [InlineData("#E78D32", "#1F170F", "#2C2116", "#37291B")]
    [InlineData("#999999", "#171717", "#212121", "#292929")]
    public void Glow_dark_material_uses_the_v5_reference_samples(string accent, string background, string panel, string raised)
    {
        var palette = CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, CuiTheme.Glow, accent);
        // Samples are from Atlas v5 buildGlowMaterial at brightness/contrast 50.
        // A one-channel rounding tolerance permits the native colour conversion.
        Match(background, palette.TideBase);
        Match(panel, palette.Panel);
        Match(raised, palette.Panel2);
        Assert.Equal(Colors.White, palette.Text);
        Assert.Equal(Color.Parse("#DED5CF"), palette.TextSoft);
        Assert.Equal(Color.Parse("#C7BDB6"), palette.Muted);

        static void Match(string expected, Color actual)
        {
            var value = Color.Parse(expected);
            Assert.InRange(Math.Abs(actual.R - value.R), 0, 1);
            Assert.InRange(Math.Abs(actual.G - value.G), 0, 1);
            Assert.InRange(Math.Abs(actual.B - value.B), 0, 1);
        }
    }

    [Fact]
    public void Glow_workspace_field_keeps_chrome_transparent_and_panels_quiet()
    {
        var palette = CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, CuiTheme.Glow, "#405BD2");
        var resources = new Avalonia.Controls.ResourceDictionary();
        CuiThemeResourceApplier.ApplyToResources(resources, palette);
        var field = Assert.IsType<CuiGlowFieldBrush>(resources["CuiWorkspaceBrush"]);
        Assert.Same(field, resources["CuiShellMaterial"]);
        Assert.Same(field, resources["CuiWorkspaceMaterial"]);
        Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiChromeMaterial"]).Color.A);
        Assert.All(new[] { field.FieldTop, field.FieldBottom, field.Light }, colour => Assert.InRange(colour.ToHsl().S, .20, .44));
        Assert.All(new[] { field.FieldTop, field.FieldBottom, field.Light }, colour => Assert.True(CuiContrast.Ratio(palette.Muted, colour) >= 4.5));
        Assert.Equal(palette.Panel, Assert.IsType<SolidColorBrush>(resources["CuiFloatingSurfaceMaterial"]).Color);
        Assert.Equal(palette.Panel, Assert.IsType<SolidColorBrush>(resources["CuiPopupSurfaceBrush"]).Color);
        Assert.Equal(palette.Panel, Assert.IsType<SolidColorBrush>(resources["CuiPopupOpaqueFallbackBrush"]).Color);
        Assert.Equal(0xE0, Assert.IsType<SolidColorBrush>(resources["CuiSurfaceMaterial"]).Color.A);
        CuiThemeResourceApplier.ApplyToResources(resources, palette, new() { HighContrast = true });
        Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiWorkspaceBrush"]);
        Assert.IsNotType<CuiGlowFieldBrush>(resources["CuiWorkspaceBrush"]);
        Assert.NotEqual(0, Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiChromeMaterial"]).Color.A);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1280, 720)]
    [InlineData(390, 844)]
    public void Glow_field_retains_the_atlas_angle_and_radial_origin_when_resized(double width, double height)
    {
        var field = new CuiGlowFieldBrush(Color.Parse("#11131D"), Color.Parse("#24293D"), Color.Parse("#24293D"));
        var drawing = Assert.IsType<DrawingGroup>(field.CreateBrush(new(width, height)).Drawing);
        Assert.Equal(2, drawing.Children.Count);
        var linear = Assert.IsType<LinearGradientBrush>(Assert.IsType<GeometryDrawing>(drawing.Children[0]).Brush);
        var start = linear.StartPoint.Point;
        var end = linear.EndPoint.Point;
        var angle = Math.Atan2(end.X - start.X, start.Y - end.Y) * 180 / Math.PI;
        Assert.InRange(angle, 159.999, 160.001);
        Assert.Equal(width, start.X + end.X, 8);
        Assert.Equal(height, start.Y + end.Y, 8);
        var haze = Assert.IsType<RadialGradientBrush>(Assert.IsType<GeometryDrawing>(drawing.Children[1]).Brush);
        Assert.Equal(new Avalonia.RelativePoint(.17, .10, Avalonia.RelativeUnit.Relative), haze.Center);
        Assert.Equal(haze.Center, haze.GradientOrigin);
        Assert.Equal(77, haze.GradientStops[0].Color.A);
        Assert.Equal(0, haze.GradientStops[1].Color.A);
        Assert.Equal(.64, haze.GradientStops[1].Offset);
    }

    [Theory]
    [InlineData(CuiAppearance.Bright, 100)]
    [InlineData(CuiAppearance.Dark, 0)]
    public void Display_extremes_keep_professional_layers_distinct(CuiAppearance mode, int brightness)
    {
        var source = CuiSurfacePaletteCatalog.For("Home", mode, CuiTheme.Professional);
        CuiPalette Adjust(int contrast) => CuiAccessibilityPalette.Resolve(source,
            new() { Brightness = brightness, ColourContrast = contrast });
        double Distance(CuiPalette value) => Math.Abs(value.Panel.ToHsl().L - value.Panel2.ToHsl().L);
        Assert.True(Distance(Adjust(100)) > Distance(Adjust(0)));
        Assert.NotEqual(Adjust(100).Panel, Adjust(100).Panel2);
        Assert.Equal(source.Accent, Adjust(100).Accent);
        Assert.Same(source, CuiAccessibilityPalette.Resolve(source, new() { Brightness = 50, ColourContrast = 50 }));
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
        Assert.Equal(Color.Parse("#F8F8FC"), retro.Text);

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
    public void Unknown_theme_names_fall_back_to_professional(string? name)
    {
        Assert.Equal(CuiTheme.Professional, CuiThemeCatalog.Parse(name));
        Assert.Equal(CuiTheme.Professional, CuiThemeCatalog.Resolve((CuiTheme)999).Theme);
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
            Assert.Equal(CuiTheme.Professional, CuiThemeScope.ResolveThemeName("Unknown", CuiTheme.Bubble));
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
                  <Theme Name="Default">
                    <Page id="root">
                      <Theme Name="Retro"><Container Type="Grid" id="retro" /></Theme>
                    </Page>
                  </Theme>
                </Cui>
                """);

            Assert.Empty(diagnostics);
            Assert.NotNull(root);
            Assert.Equal(CuiTheme.Bubble, loader.CurrentTheme);
            Assert.True(root.Resources.TryGetResource("CuiTheme", null, out var rootTheme));
            Assert.Equal(CuiTheme.Bubble, rootTheme);
            var page = Assert.IsAssignableFrom<Avalonia.Controls.Panel>(root);
            var retro = Assert.IsType<CuiContainerControl>(Assert.Single(page.Children));
            Assert.True(retro.Resources.TryGetResource("CuiTheme", null, out var nestedTheme));
            Assert.Equal(CuiTheme.Retro, nestedTheme);
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
            Assert.Equal(CuiTheme.Professional, CuiThemePreferenceReader.Read(directory));

            var first = """{"havenUiThemeName":"Bubble","unrelatedSetting":true}""";
            File.WriteAllText(path, first);
            Assert.Equal(CuiTheme.Bubble, CuiThemePreferenceReader.Read(directory));
            Assert.Equal(first, File.ReadAllText(path));

            File.WriteAllText(path, """{"havenUiThemeName":"Retro"}""");
            Assert.Equal(CuiTheme.Retro, CuiThemePreferenceReader.Read(directory));

            File.WriteAllText(path, """{"havenUiThemeName":"NotATheme"}""");
            Assert.Equal(CuiTheme.Professional, CuiThemePreferenceReader.Read(directory));
            File.WriteAllText(path, "{" );
            Assert.Equal(CuiTheme.Professional, CuiThemePreferenceReader.Read(directory));
            File.WriteAllText(path, """{"havenUiThemeName":"Glow"}""");
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
    public void Invalid_theme_name_falls_back_explicitly_to_Professional()
    {
        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Playful;
        Assert.Equal(CuiTheme.Professional, CuiThemeScope.ResolveThemeName("NeonRetro", CuiTheme.Playful));

        CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
        Assert.Equal(CuiTheme.Professional, CuiThemeScope.ResolveThemeName("Bogus", CuiTheme.Glow));
    }

    [Fact]
    public void Unknown_numeric_theme_names_do_not_escape_the_fallback()
    {
        Assert.Equal(CuiTheme.Professional, CuiThemeCatalog.Parse("999"));
        Assert.Equal(CuiTheme.Professional, CuiThemeCatalog.Parse("-1"));
    }

    [Fact]
    public void Custom_theme_inheritance_merges_element_and_type_defaults_base_first()
    {
        var catalog = new CuiThemeDefinitionCatalog();
        catalog.Add(new CuiThemeDefinition("Rainbow", "Professional",
        [
            new("Button", null, new Dictionary<string, object?>
            {
                ["Padding"] = 8d,
                ["Background"] = "blue"
            }),
            new("Button", "Primary", new Dictionary<string, object?>
            {
                ["Foreground"] = "white"
            }),
            new("button", "primary", new Dictionary<string, object?>
            {
                ["foreground"] = "black"
            })
        ]));
        catalog.Add(new CuiThemeDefinition("Ocean", "Rainbow",
        [
            new("button", null, new Dictionary<string, object?>
            {
                ["Background"] = "navy"
            })
        ]));

        var resolved = catalog.Resolve("Ocean");
        Assert.Equal(CuiTheme.Professional, resolved.BaseTheme);
        Assert.Equal("Ocean", resolved.Expression.DisplayName);
        Assert.Equal(8d, resolved.Rules[new("BUTTON", null)]["padding"]);
        Assert.Equal("navy", resolved.Rules[new("Button", null)]["background"]);
        Assert.Equal("white", resolved.Rules[new("Button", "Primary")]["foreground"]);
        Assert.Equal("black", resolved.Rules[new("BUTTON", "primary")]["Foreground"]);
        Assert.False(resolved.Rules.ContainsKey(new("Button", "PRIMARY")));
        Assert.Equal(3, resolved.Rules.Count);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, object?>)resolved.Rules[new("Button", "Primary")])["Foreground"] = "red");
        Assert.Equal("white", catalog.Resolve("ocean").Rules[new("button", "Primary")]["FOREGROUND"]);
    }

    [Fact]
    public void Parsed_theme_rules_keep_registered_authored_Type_names_case_sensitive()
    {
        var registry = new CuiControlRegistry();
        var upper = new Avalonia.Controls.TextBlock();
        var lower = new Avalonia.Controls.TextBlock();
        registry.RegisterControlType("AuthoredLabel", _ => upper);
        registry.RegisterControlType("authoredlabel", _ => lower);
        Assert.False(registry.IsRegisteredRuntimeType("AUTHOREDLABEL"));
        using var loader = new CuiControlLoader(registry);
        var (root, diagnostics) = loader.LoadMarkup("""
            <Cui>
              <Theme Definition="True" Name="CaseRules" Inherits="Professional">
                <text Definition="True" Type="AuthoredLabel" oPaCiTy="0.25" />
                <Text Definition="True" Type="authoredlabel" Opacity="0.75" />
              </Theme>
              <Theme Name="CaseRules">
                <Page>
                  <Text ID="upper" Type="AuthoredLabel">Upper type</Text>
                  <Text ID="lower" Type="authoredlabel">Lower type</Text>
                </Page>
              </Theme>
            </Cui>
            """);

        Assert.Empty(diagnostics);
        var page = Assert.IsAssignableFrom<Avalonia.Controls.Panel>(root);
        Assert.Same(upper, page.Children[0]);
        Assert.Same(lower, page.Children[1]);
        Assert.Equal(.25d, upper.Opacity);
        Assert.Equal(.75d, lower.Opacity);
    }

    [Fact]
    public void Custom_theme_catalog_rejects_shadowing_unknown_bases_and_inheritance_cycles()
    {
        var catalog = new CuiThemeDefinitionCatalog();
        Assert.Equal("CUI_THEME_BUILTIN_SHADOW", Assert.Throws<CuiThemeDefinitionException>(() =>
            catalog.Add(new CuiThemeDefinition("Professional", "Glow", Array.Empty<CuiThemeElementRule>()))).Code);

        catalog.Add(new CuiThemeDefinition("A", "B", Array.Empty<CuiThemeElementRule>()));
        catalog.Add(new CuiThemeDefinition("B", "A", Array.Empty<CuiThemeElementRule>()));
        Assert.Equal("CUI_THEME_INHERITANCE_CYCLE", Assert.Throws<CuiThemeDefinitionException>(() => catalog.Resolve("A")).Code);
        Assert.False(catalog.TryResolve("Missing", out var missing));
        Assert.Null(missing);

        var unknownBase = new CuiThemeDefinitionCatalog();
        unknownBase.Add(new CuiThemeDefinition("Orphan", "Missing", Array.Empty<CuiThemeElementRule>()));
        Assert.Equal("CUI_THEME_UNKNOWN_BASE", Assert.Throws<CuiThemeDefinitionException>(() => unknownBase.Resolve("Orphan")).Code);

        var emptyRule = new CuiThemeDefinitionCatalog();
        Assert.Equal("CUI_THEME_EMPTY_RULE", Assert.Throws<CuiThemeDefinitionException>(() =>
            emptyRule.Add(new CuiThemeDefinition("Empty", "Glow",
            [new("Button", null, new Dictionary<string, object?>())]))).Code);

        var duplicateProperties = new CuiThemeDefinitionCatalog();
        var caseSensitiveProperties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Color"] = "red",
            ["color"] = "blue"
        };
        Assert.Equal("CUI_THEME_DUPLICATE_PROPERTY", Assert.Throws<CuiThemeDefinitionException>(() =>
            duplicateProperties.Add(new CuiThemeDefinition("Duplicate", "Glow",
            [new("Button", null, caseSensitiveProperties)]))).Code);
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
        //     </Theme>  ← back to Bubble
        //   </Theme>  ← back to Glow
        // </Theme>
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
        Assert.Equal(9, expressions.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, expressions.Select(e => (int)e.Theme).Order().ToArray());

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
        Assert.Equal("Comic", CuiThemeCatalog.Name(CuiTheme.Cinematic));
        Assert.Equal("Professional", CuiThemeCatalog.Name(CuiTheme.Professional));
    }

    [Fact]
    public void Comic_replaces_the_builtin_alias_and_retains_custom_theme_identities()
    {
        Assert.Equal(9, CuiThemeCatalog.All.Count);
        Assert.Single(CuiThemeCatalog.All, theme => theme.DisplayName == "Comic");
        Assert.DoesNotContain(CuiThemeCatalog.All, theme => theme.DisplayName == "Cinematic");
        Assert.Equal(4, (int)CuiThemeCatalog.Parse("comic"));
        Assert.Equal(CuiTheme.Professional, CuiThemeCatalog.Parse("cine matic"));

        var definitions = new CuiThemeDefinitionCatalog();
        var legacy = CuiThemeScope.Resolve("cInEmAtIc", definitions);
        Assert.Equal("Comic", legacy.Theme.Name);
        Assert.False(legacy.UsedFallback);
        Assert.Equal(new CuiThemeIdentityMigration("cInEmAtIc", "comic"), legacy.Migration);

        definitions.Add(new("Document presentation", "Cinematic", []));
        Assert.Equal(CuiTheme.Cinematic, definitions.Resolve("Document presentation").BaseTheme);
        var exact = CuiCustomThemeContract.CreateDefault("Cinematic");
        var named = CuiCustomThemeContract.CreateDefault("Cinematic workshop");
        definitions.UpsertCustomTheme(exact);
        definitions.UpsertCustomTheme(named);
        var custom = CuiThemeScope.Resolve("Cinematic", definitions);
        Assert.Null(custom.Migration);
        Assert.Equal(exact.Id, custom.Theme.CustomTheme!.Id);
        Assert.Equal(named.Id, definitions.Resolve(named.Name).CustomTheme!.Id);

        var sharedCaption = CuiCustomThemeContract.CreateDefault("Comic");
        var occupiedKey = CuiCustomThemeContract.CreateDefault("builtin:comic:0");
        var library = CuiCustomThemeContract.DeserializeLibrary(CuiCustomThemeContract.SerializeLibrary([sharedCaption, occupiedKey]));
        foreach (var theme in library) definitions.UpsertCustomTheme(theme);
        Assert.Equal(sharedCaption.Id, definitions.Resolve("Comic").CustomTheme!.Id);
        var builtInKey = CuiThemeCatalog.ComicSelectionKey(library.Select(item => item.Name));
        var builtIn = definitions.Resolve(builtInKey);
        Assert.Equal("Comic", builtIn.Name);
        Assert.Null(builtIn.CustomTheme);
        Assert.Equal("builtin:comic:1", builtInKey);
    }

    [Theory]
    [InlineData(CuiAppearance.Bright)]
    [InlineData(CuiAppearance.Dark)]
    public void Comic_paints_distinct_readable_controls_at_each_appearance(CuiAppearance appearance)
    {
        var resources = new Avalonia.Controls.ResourceDictionary();
        var palette = CuiSurfacePaletteCatalog.For("Home", appearance, CuiTheme.Cinematic, null);
        CuiThemeResourceApplier.ApplyToResources(resources, palette, new() { ReduceMotion = true });
        Color Colour(string key) => Assert.IsType<SolidColorBrush>(resources[key]).Color;
        Assert.Equal(Color.Parse("#FFF12A"), Colour("CuiAccentPrimaryBrush"));
        Assert.Equal(palette.Button, Colour("CuiAccentSecondaryBrush"));
        Assert.NotEqual(Colour("CuiAccentPrimaryBrush"), Colour("CuiComicDisabledBrush"));
        Assert.True(CuiContrast.Ratio(Colour("CuiComicDisabledBrush"), Colour("CuiComicDisabledForegroundBrush")) >= 4.5);
        Assert.NotEqual(palette.Panel2, palette.Panel3);
        Assert.True(CuiContrast.Ratio(Colour("CuiAccentPrimaryBrush"), Colour("CuiAccentPrimaryForegroundBrush")) >= 4.5);
        var rest = Assert.IsType<BoxShadows>(resources["CuiComicPrimaryShadow"]);
        Assert.NotEqual(rest, Assert.IsType<BoxShadows>(resources["CuiComicPrimaryPressedShadow"]));
        Assert.Equal(TimeSpan.Zero, resources["CuiButtonMotionDuration"]);

        var scoped = CuiSurfacePaletteCatalog.For("Write", appearance, CuiTheme.Cinematic, "#3575C4", "app.write");
        CuiThemeResourceApplier.ApplyToResources(resources, scoped, new());
        Assert.Equal(Color.Parse("#3575C4"), Colour("CuiAccentPrimaryBrush"));
        CuiThemeResourceApplier.ApplyToResources(resources, palette, new() { HighContrast = true });
        Assert.True(CuiContrast.Ratio(Colour("CuiAccentPrimaryBrush"), Colour("CuiAccentPrimaryForegroundBrush")) >= 7);
        Assert.Equal(new BoxShadows(), resources["CuiComicPrimaryShadow"]);
    }

    [Fact]
    public void Accessibility_profile_applies_high_contrast_reduced_motion_and_display_scaling()
    {
        var resources = new Avalonia.Controls.ResourceDictionary();
        var settings = new CuiAccessibilitySettings
        {
            HighContrast = true,
            ReduceMotion = true,
            DisplayScale = 1.5d,
            InterfaceFontFamilyOverride = "Atkinson Hyperlegible",
            CodeFontFamilyOverride = "JetBrains Mono"
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
        Assert.StartsWith("Atkinson Hyperlegible, Montserrat,",
            Assert.IsType<string>(resources["CuiFontFamilyInterface"]));
        Assert.StartsWith("JetBrains Mono, Cascadia Mono,",
            Assert.IsType<string>(resources["CuiFontFamilyCode"]));
    }

    [Fact]
    public void High_contrast_palette_has_distinct_nontransparent_focus_and_readable_text_in_every_appearance()
    {
        foreach (var theme in Enum.GetValues<CuiTheme>())
        foreach (var appearance in Enum.GetValues<CuiAppearance>())
        {
            var source = CuiSurfacePaletteCatalog.For("Home", appearance, theme);
            var palette = CuiAccessibilityPalette.Resolve(source, new CuiAccessibilitySettings { HighContrast = true });

            Assert.True(CuiContrast.Ratio(palette.Text, palette.TideBase) >= 7d, $"Text contrast failed for {theme}/{appearance}.");
            Assert.True(CuiContrast.Ratio(palette.TextSoft, palette.Panel) >= 7d, $"Soft-text contrast failed for {theme}/{appearance}.");
            Assert.True(CuiContrast.Ratio(palette.Muted, palette.Panel2) >= 7d, $"Muted-text contrast failed for {theme}/{appearance}.");
            Assert.True(CuiContrast.Ratio(palette.Focus, palette.TideBase) >= 3d, $"Focus contrast failed for {theme}/{appearance}.");
            Assert.Equal<byte>(0xFF, palette.Focus.A);
        }
    }

    [Fact]
    public void Accessibility_defaults_include_interface_and_code_font_fallbacks()
    {
        Assert.StartsWith("Montserrat,", CuiTypography.InterfaceFontFamily);
        Assert.Contains("Segoe UI", CuiTypography.InterfaceFontFamily);
        Assert.Contains("sans-serif", CuiTypography.InterfaceFontFamily);
        Assert.Contains("Cascadia Mono", CuiTypography.CodeFontFamily);
        Assert.Contains("monospace", CuiTypography.CodeFontFamily);
        Assert.StartsWith("Atkinson Hyperlegible, Montserrat,", CuiTypography.ResolveInterfaceFontFamily("Atkinson Hyperlegible"));
        Assert.StartsWith("JetBrains Mono, Cascadia Mono,", CuiTypography.ResolveCodeFontFamily("JetBrains Mono"));
        Assert.Throws<ArgumentException>(() => CuiTypography.ResolveInterfaceFontFamily("Font A, Font B"));
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
              <Theme Name="Bubble">
                <Page id="root">
                  <Text text="Themed" />
                </Page>
              </Theme>
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
              <Theme Name="Retro">
                <Page id="root">
                  <Text text="Retro" />
                </Page>
              </Theme>
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
              <Theme Name="Cinematic">
                <Page id="root">
                  <Text text="Cinematic themed" />
                </Page>
              </Theme>
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
              <Theme Name="Glow">
                <Page id="root">
                  <Text text="Glow parent" />
                  <Theme Name="Bubble">
                    <Container Type="Grid" id="bubble-zone">
                      <Text text="Bubble child" />
                    </Container>
                  </Theme>
                  <Text text="Glow resume" />
                </Page>
              </Theme>
            </Cui>
            """;

        var result = CuiHeadlessRenderer.Render(cui, "nested-theme.cui");
        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        Assert.NotNull(result.Root);
        // Outer DefaultTheme scope (virtual) → Page(Panel) + TextBlock
        // + Inner DefaultTheme scope (virtual) → Border + TextBlock
        // + TextBlock = 5 visual controls
        Assert.Equal(3, Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(result.Root!).OfType<Avalonia.Controls.TextBlock>().Count());
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
