using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiThemeRolePresentationTests
{
    [Theory]
    [InlineData(CuiTheme.Glow)]
    [InlineData(CuiTheme.Professional)]
    public void Boards_uses_canonical_purple_by_default_and_explicit_accent_wins(CuiTheme theme)
    {
        // Bright exposes the chosen anchor directly; darker appearances retain
        // their theme-owned tonal derivation from that same chosen identity.
        var defaultPalette = CuiSurfacePaletteCatalog.For("Boards", CuiAppearance.Bright, theme, accentColour: null);
        var ownedPalette = CuiSurfacePaletteCatalog.For("Boards", CuiAppearance.Bright, theme, null, "app.boards");
        Assert.Equal(Color.Parse("#7026D9"), defaultPalette.Accent);
        Assert.Equal(defaultPalette, ownedPalette);
        var chosenPalette = CuiSurfacePaletteCatalog.For("Boards", CuiAppearance.Bright, theme, "#168B4F", "app.boards");
        Assert.Equal(Color.Parse("#168B4F"), chosenPalette.Accent);
        Assert.NotEqual(defaultPalette.Accent, chosenPalette.Accent);
        Assert.Null(chosenPalette.SurfaceAccent);
        foreach (var appearance in Enum.GetValues<CuiAppearance>())
        {
            var explicitPurple = CuiSurfacePaletteCatalog.For("Boards", appearance, theme, "#7026D9", "app.boards");
            var appDefault = CuiSurfacePaletteCatalog.For("Boards", appearance, theme, null, "app.boards");
            Assert.Equal(explicitPurple.Accent, appDefault.Accent);
            Assert.Equal(explicitPurple.AccentSecondary, appDefault.AccentSecondary);
            Assert.Equal(explicitPurple.AccentStrong, appDefault.AccentStrong);
            Assert.Equal(explicitPurple.AccentSoft, appDefault.AccentSoft);
            Assert.Null(explicitPurple.SurfaceAccent);
            if (theme == CuiTheme.Glow)
            {
                var tint = Assert.IsType<Color>(appDefault.SurfaceAccent);
                Assert.True(tint.R > 112 && tint.B < 217);
            }
            else Assert.Null(appDefault.SurfaceAccent);
        }
    }

    [Theory]
    [InlineData("#D64B25")]
    [InlineData("#2864D7")]
    public void Glow_keeps_ordinary_controls_neutral_and_publishes_explicit_action_roles(string accent)
    {
        var palette = CuiSurfacePaletteCatalog.For("Boards", CuiAppearance.Dark, CuiTheme.Glow, accent);
        var resources = new ResourceDictionary();
        CuiThemeResourceApplier.ApplyToResources(resources, palette);
        Assert.Equal(palette.Panel, Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiButtonBrush"]).Color);
        Assert.Equal(palette.Panel3, Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiInputSurfaceBrush"]).Color);
        Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiControlBorderBrush"]).Color.A);
        Assert.True(Assert.IsType<Thickness>(resources["CuiControlBorderWidth"]).Left > 0); // focus keeps its reserved outline
        Assert.IsAssignableFrom<IGradientBrush>(resources["CuiAccentPrimaryBrush"]);
        Assert.IsAssignableFrom<IGradientBrush>(resources["CuiAccentSecondaryBrush"]);
        Assert.NotEqual(resources["CuiAccentTertiaryForegroundBrush"], resources["CuiAccentForegroundBrush"]);
        foreach (var state in new[] { "", "Hover", "Pressed" })
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(resources[$"CuiAccentText{state}Brush"]).Color.A);
        Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiAccentNegativeForegroundBrush"]).Color);
        Assert.IsType<DropShadowEffect>(resources["CuiAccentPrimaryHoverEffect"]);
    }

    [Theory]
    [InlineData(CuiTheme.Professional, false)]
    [InlineData(CuiTheme.Glow, true)]
    public void Professional_and_reduced_motion_have_no_decorative_glow(CuiTheme theme, bool reduceMotion)
    {
        var resources = new ResourceDictionary();
        CuiThemeResourceApplier.ApplyToResources(resources,
            CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, theme), new() { ReduceMotion = reduceMotion });
        foreach (var role in new[] { "Primary", "Secondary", "Tertiary", "Negative" })
            Assert.Null(resources[$"CuiAccent{role}HoverEffect"]);
        Assert.True(Assert.IsType<double>(resources["CuiFocusIndicatorThickness"]) >= 2);
        if (reduceMotion) Assert.Equal(TimeSpan.Zero, resources["CuiButtonMotionDuration"]);
    }

    [Theory]
    [InlineData(CuiAppearance.Dark)]
    [InlineData(CuiAppearance.SuperDark)]
    public void Glow_action_ink_contrasts_with_every_gradient_stop_in_both_dark_appearances(CuiAppearance appearance)
    {
        foreach (var accent in new[] { "#2864D7", "#FAE342", "#D64B25" })
        {
            var resources = new ResourceDictionary();
            CuiThemeResourceApplier.ApplyToResources(resources,
                CuiSurfacePaletteCatalog.For("Boards", appearance, CuiTheme.Glow, accent));
            var primary = Assert.IsAssignableFrom<IGradientBrush>(resources["CuiAccentPrimaryBrush"]);
            var secondary = Assert.IsAssignableFrom<IGradientBrush>(resources["CuiAccentSecondaryBrush"]);
            Assert.True(secondary.GradientStops.Average(stop => CuiContrast.Ratio(stop.Color, Colors.Black)) >
                primary.GradientStops.Average(stop => CuiContrast.Ratio(stop.Color, Colors.Black)));
            foreach (var role in new[] { "Primary", "Secondary", "Tertiary", "Negative" })
                foreach (var state in new[] { "", "Hover", "Pressed" })
                {
                    var ink = Assert.IsAssignableFrom<ISolidColorBrush>(resources[$"CuiAccent{role}{state}ForegroundBrush"]).Color;
                    if (role != "Tertiary" || state != "") Assert.Equal(Colors.White, ink);
                    var fill = Assert.IsAssignableFrom<IGradientBrush>(resources[$"CuiAccent{role}{state}Brush"]);
                    Assert.All(fill.GradientStops, stop => Assert.True(CuiContrast.Ratio(ink, stop.Color) >= 4.5));
                }
        }
    }

    [Fact]
    public void Glow_surface_gradients_follow_the_chosen_accent_without_replacing_solid_document_tokens()
    {
        var purple = new ResourceDictionary(); var green = new ResourceDictionary();
        CuiThemeResourceApplier.ApplyToResources(purple, CuiSurfacePaletteCatalog.For("Boards", CuiAppearance.Dark, CuiTheme.Glow, "#7026D9"));
        CuiThemeResourceApplier.ApplyToResources(green, CuiSurfacePaletteCatalog.For("Boards", CuiAppearance.Dark, CuiTheme.Glow, "#168B4F"));
        foreach (var key in new[] { "CuiShellMaterial", "CuiWorkspaceMaterial", "CuiSurfaceMaterial", "CuiFloatingSurfaceMaterial" })
        {
            var first = Assert.IsAssignableFrom<ILinearGradientBrush>(purple[key]);
            var second = Assert.IsAssignableFrom<ILinearGradientBrush>(green[key]);
            Assert.NotEqual(first.GradientStops.Select(stop => stop.Color), second.GradientStops.Select(stop => stop.Color));
        }
        Assert.IsAssignableFrom<ISolidColorBrush>(purple["CuiPanelBrush"]);
        Assert.IsAssignableFrom<ISolidColorBrush>(purple["CuiPanel2Brush"]);
        CuiCustomThemeResourceApplier.ApplyToResources(purple, CuiCustomThemeContract.CreateDefault());
        Assert.Same(purple["CuiPanelBrush"], purple["CuiSurfaceMaterial"]);
    }

    [Fact]
    public async Task Boards_loader_uses_its_app_palette_for_surfaces_selection_focus_and_live_overrides()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var source = new ScopedThemeSource();
            using var preference = new CuiThemePreferenceContext(source);
            using var loader = new CuiControlLoader();
            loader.SetSurface("Boards", "app.boards");
            loader.SetThemePreferenceContext(preference);
            var (root, diagnostics) = loader.LoadMarkup("<Page><Container Type='Vertical Stack'>" +
                "<Input Type='Text' ID='field'/>" +
                "<Button ID='row' Content='Selected page'/>" +
                "<Button ID='action' Content='Action' AccentRole='Primary'/>" +
                "</Container></Page>");
            Assert.Empty(diagnostics); Assert.NotNull(root);
            var window = new Window { Content = root, Width = 420, Height = 300 };
            try
            {
                window.Show(); window.UpdateLayout();
                var field = Assert.Single(root!.GetVisualDescendants().OfType<TextBox>());
                var row = root.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "row");
                var action = root.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "action");
                CuiPropertyPresentation.SetSelected(row, true);
                void CheckPalette(string? accent)
                {
                    var expected = CuiSurfacePaletteCatalog.For("Boards", CuiAppearance.Dark, CuiTheme.Glow, accent, "app.boards");
                    foreach (var control in new Control[] { field, row, action })
                        foreach (var (key, colour) in new[]
                        {
                            ("CuiPanel2Brush", expected.Panel2), ("CuiPanel3Brush", expected.Panel3),
                            ("HavenPanel2Brush", expected.Panel2), ("HavenPanel3Brush", expected.Panel3),
                            ("CuiSelectionBrush", expected.AccentSoft), ("CuiFocusBrush", expected.Focus),
                            ("CuiAttentionBrush", expected.Attention), ("HavenAttentionBrush", expected.Attention),
                            ("CuiAttentionBorderBrush", expected.AttentionBorder), ("HavenAttentionBorderBrush", expected.AttentionBorder)
                        })
                        {
                            Assert.True(control.Resources.TryGetResource(key, null, out var actual));
                            Assert.Equal(colour, Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
                        }
                    Assert.Equal(expected.AccentSoft, Assert.IsAssignableFrom<ISolidColorBrush>(row.Background).Color);
                    var expectedResources = new ResourceDictionary();
                    CuiThemeResourceApplier.ApplyToResources(expectedResources, expected);
                    Assert.Equal(Assert.IsAssignableFrom<IGradientBrush>(expectedResources["CuiAccentPrimaryBrush"])
                            .GradientStops.Select(stop => (stop.Offset, stop.Color)),
                        Assert.IsAssignableFrom<IGradientBrush>(action.Background)
                            .GradientStops.Select(stop => (stop.Offset, stop.Color)));
                }
                CheckPalette(null);
                var overrides = CuiAccentPreference.Default.WithMode(CuiAccentOverrideMode.IndividualApps)
                    .WithAppColour("app.boards", "#168B4F").WithAppColour("app.write", "#D64B25");
                source.Publish(source.Current with { Revision = 2, Accent = overrides });
                CheckPalette("#168B4F");
                source.Publish(source.Current with { Revision = 3, Accent = overrides.WithAppColour("app.boards", null) });
                CheckPalette(null);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Authored_roles_react_and_text_actions_keep_focus_without_a_pill()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("<Theme Name='Glow'><Page><Container Type='Vertical Stack'>" +
                "<Button ID='action' Content='Action' AccentRole='Primary'/>" +
                "<Button ID='text' Content='Details' AccentRole='Text'/>" +
                "<Input Type='Switch' Checked='True'/>" +
                "</Container></Page></Theme>");
            Assert.Empty(diagnostics); Assert.NotNull(root);
            var window = new Window { Content = root, Width = 420, Height = 300 };
            try
            {
                window.Show(); window.UpdateLayout();
                var buttons = root!.GetVisualDescendants().OfType<Button>().ToArray();
                var action = buttons.Single(button => button.Name == "action");
                var text = buttons.Single(button => button.Name == "text");
                Assert.IsAssignableFrom<IGradientBrush>(action.Background);
                Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(text.Background).Color.A);
                CuiPropertyPresentation.Set(action, "AccentRole", CuiAccentRole.Negative, CuiPropertyLevel.Instance, "test role change");
                Assert.Same(Resource(action, "CuiAccentNegativeBrush"), action.Background);
                var actionFrame = Assert.Single(action.GetVisualChildren().OfType<Border>());
                var textFrame = Assert.Single(text.GetVisualChildren().OfType<Border>());
                Assert.NotEmpty(actionFrame.Transitions!);
                Assert.Null(textFrame.Transitions);
                action.Resources["CuiReduceMotion"] = true;
                Assert.Null(actionFrame.Transitions);
                Assert.Null(actionFrame.Effect);
                Assert.Null(actionFrame.RenderTransform);
                action.Resources["CuiReduceMotion"] = false;
                Assert.NotEmpty(actionFrame.Transitions!);
                action.Resources["CuiTheme"] = CuiTheme.Professional;
                Assert.Null(actionFrame.Transitions);
                Assert.Null(actionFrame.Effect);
                text.Focus(); window.UpdateLayout();
                Assert.True(text.IsKeyboardFocusWithin);
                Assert.Same(Resource(text, "CuiFocusBrush"), text.BorderBrush);
                Assert.True(root.GetVisualDescendants().OfType<ToggleSwitch>().Single().IsChecked);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class ScopedThemeSource : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; private set; } =
            new("Glow", 1, "test-home-account", true, CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed;
        public void Publish(CuiThemePreferenceSnapshot snapshot)
        { Current = snapshot; Changed?.Invoke(this, EventArgs.Empty); }
    }

    private static object? Resource(Control control, string key) => control.TryFindResource(key, out var value) ? value : null;
}
