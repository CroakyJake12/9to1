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

    private static object? Resource(Control control, string key) => control.TryFindResource(key, out var value) ? value : null;
}
