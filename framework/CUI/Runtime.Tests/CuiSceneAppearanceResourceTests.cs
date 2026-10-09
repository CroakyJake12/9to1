using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiSceneAppearanceResourceTests
{
    [Theory]
    [InlineData(CuiAppearance.SuperBright)]
    [InlineData(CuiAppearance.Bright)]
    [InlineData(CuiAppearance.Dark)]
    [InlineData(CuiAppearance.SuperDark)]
    public async Task Authored_semantic_brushes_and_appearance_binding_use_the_same_selected_scene(CuiAppearance appearance)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(appearance);
            loader.SetBindingContext(new CuiViewModel());
            var (root, diagnostics) = loader.LoadMarkup("""
                <Cui><StackPanel>
                  <Border Background="{Resource CuiPanelBrush}">
                    <TextBlock Foreground="{Resource CuiTextBrush}" Text="{Binding Appearance}" />
                  </Border>
                </StackPanel></Cui>
                """);
            Assert.Empty(diagnostics);
            var panel = Assert.IsType<Border>(Assert.Single(Assert.IsType<StackPanel>(root).Children));
            var label = Assert.IsType<TextBlock>(panel.Child);
            var palette = CuiSurfacePaletteCatalog.For("Imagine", appearance, loader.CurrentTheme);
            Assert.Equal(palette.Panel, Assert.IsType<SolidColorBrush>(panel.Background).Color);
            Assert.Equal(palette.Text, Assert.IsType<SolidColorBrush>(label.Foreground).Color);
            Assert.Equal(appearance.ToString(), label.Text);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(CuiAppearance.SuperBright)]
    [InlineData(CuiAppearance.Bright)]
    [InlineData(CuiAppearance.Dark)]
    [InlineData(CuiAppearance.SuperDark)]
    public void Opaque_button_states_use_readable_existing_ink_and_same_scoped_backend_brushes(CuiAppearance appearance)
    {
        foreach (var surface in new[] { "Home", "Imagine", "Studio" })
        {
            var palette = CuiSurfacePaletteCatalog.For(surface, appearance, CuiTheme.Glow);
            var resources = new ResourceDictionary();
            CuiThemeResourceApplier.ApplyToResources(resources, palette);
            foreach (var (surfaceKey, inkKey, legacyKey) in new[]
            {
                ("CuiButtonBrush", "CuiButtonForegroundBrush", "HavenButtonForegroundBrush"),
                ("CuiButtonHoverBrush", "CuiButtonHoverForegroundBrush", "HavenButtonHoverForegroundBrush"),
                ("CuiButtonPressedBrush", "CuiButtonPressedForegroundBrush", "HavenButtonPressedForegroundBrush")
            })
            {
                var fill = Assert.IsType<SolidColorBrush>(resources[surfaceKey]).Color;
                var ink = Assert.IsType<SolidColorBrush>(resources[inkKey]);
                Assert.True(ink.Color == palette.Text || ink.Color == palette.AccentInk);
                Assert.Same(resources[ink.Color == palette.Text ? "CuiTextBrush" : "CuiAccentInkBrush"], ink);
                Assert.Same(ink, resources[legacyKey]);
                Assert.True(CuiContrast.Ratio(ink.Color, fill) >= 4.5d,
                    $"{surface}/{appearance}/{surfaceKey} must retain readable text on its actual opaque fill.");
            }
            Assert.Equal(palette.Button, Assert.IsType<SolidColorBrush>(resources["CuiButtonBrush"]).Color);
            Assert.Equal(palette.Text, Assert.IsType<SolidColorBrush>(resources["CuiTextBrush"]).Color);
            Assert.Equal(palette.AccentInk, Assert.IsType<SolidColorBrush>(resources["CuiAccentInkBrush"]).Color);
        }
        var scene = CuiSceneVisualResources.Create("Home", appearance);
        Assert.Same(scene["CuiButtonForegroundBrush"], scene["ButtonForeground"]);
        Assert.Same(scene["CuiButtonHoverForegroundBrush"], scene["ButtonForegroundPointerOver"]);
        Assert.Same(scene["CuiButtonPressedForegroundBrush"], scene["ButtonForegroundPressed"]);
    }
}
