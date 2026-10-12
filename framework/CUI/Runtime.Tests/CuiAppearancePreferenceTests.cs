using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiAppearancePreferenceTests
{
    [Fact]
    public async Task Owned_appearance_refreshes_existing_scopes_and_preserves_document_controls_and_exact_global_mode()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            Application.Current!.Resources["CuiAppearance"] = "SuperDark";
            var source = new Source();
            using var context = new CuiThemePreferenceContext(source);
            using var loader = new CuiControlLoader();
            loader.SetThemePreferenceContext(context);
            var loaded = loader.LoadMarkup("""
                <Container Background="{Resource CuiBackgroundBrush}" Type="Vertical Stack">
                  <Input Type="Text" Value="Saved document text"/>
                  <Theme Name="Bubble"><Button Content="Scoped action"/></Theme>
                </Container>
                """);
            Assert.Empty(loaded.Diagnostics);
            var root = Assert.IsType<CuiContainerControl>(loaded.Root);
            var input = Assert.IsType<TextBox>(root.AuthoredChildren[0]);
            var scoped = Assert.IsType<Button>(root.AuthoredChildren[1]);
            Assert.Equal(CuiSurfacePaletteCatalog.For("Home", CuiAppearance.SuperDark, CuiTheme.Professional).TideBase, Colour(root, "CuiBackgroundBrush"));
            using var overrideLoader = new CuiControlLoader();
            overrideLoader.SetAppearance(CuiAppearance.Dark);
            overrideLoader.SetThemePreferenceContext(context);
            var overridden = Assert.IsType<Button>(overrideLoader.LoadMarkup("<Button Content='Explicit host appearance'/>").Root);
            foreach (var mode in Enum.GetValues<CuiAppearance>())
            {
                source.Set(mode);
                Assert.Equal(mode, context.Current.Appearance);
                Assert.Equal(mode, context.ResolveScope("Bubble").Appearance);
                Assert.Equal(CuiSurfacePaletteCatalog.For("Home", mode, CuiTheme.Professional).TideBase, Colour(root, "CuiBackgroundBrush"));
                Assert.Equal(CuiSurfacePaletteCatalog.For("Home", mode, CuiTheme.Bubble).TideBase, Colour(scoped, "CuiBackgroundBrush"));
                Assert.Equal(CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, CuiTheme.Professional).TideBase, Colour(overridden, "CuiBackgroundBrush"));
                Assert.Same(input, root.AuthoredChildren[0]);
                Assert.Equal("Saved document text", input.Text);
                CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Professional, appearanceOverride: mode);
                Assert.Equal(mode, CuiThemeScopeApplier.DetectAppearance());
                Assert.Equal(CuiSurfacePaletteCatalog.For("Home", mode, CuiTheme.Professional).TideBase,
                    ((ISolidColorBrush)Application.Current.Resources["CuiBackgroundBrush"]!).Color);
                // A later theme switch must retain Super Bright/Super Dark,
                // rather than collapsing the mode into its light/dark family.
                CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Glow);
                Assert.Equal(mode, CuiThemeScopeApplier.DetectAppearance());
            }
        }, CancellationToken.None);
    }

    private static Color Colour(Control control, string key)
    {
        Assert.True(control.Resources.TryGetResource(key, null, out var value));
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private sealed class Source : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; private set; } = new("Professional", 0, "appearance-fixture", false, new());
        public event EventHandler? Changed;
        public void Set(CuiAppearance mode)
        {
            Current = Current with { Revision = Current.Revision + 1, Appearance = mode };
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
