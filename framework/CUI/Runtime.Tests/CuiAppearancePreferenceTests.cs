using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiAppearancePreferenceTests
{
    [Fact]
    public async Task Retained_inherited_scene_tracks_global_bright_dark_bright_and_keeps_explicit_appearance()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var previousTheme = CuiSurfacePaletteCatalog.ActiveTheme;
            var previousOverride = CuiSurfacePaletteCatalog.OverrideAccent;
            var previousAccent = CuiSurfacePaletteCatalog.AccentOverride;
            using var inherited = new CuiSceneThemeBinding(Scene());
            using var explicitAppearance = new CuiSceneThemeBinding(Scene(CuiAppearance.Dark));
            try
            {
                foreach (var mode in new[] { CuiAppearance.Bright, CuiAppearance.Dark, CuiAppearance.Bright })
                {
                    CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Glow, appearanceOverride: mode);

                    Assert.Equal(mode.ToString(), Resource(inherited.Resources, "CuiAppearance"));
                    Assert.Equal(CuiSurfacePaletteCatalog.For("Home", mode, CuiTheme.Glow).TideBase,
                        Colour(inherited.Resources, "CuiBackgroundBrush"));
                    Assert.Equal(CuiAppearance.Dark.ToString(), Resource(explicitAppearance.Resources, "CuiAppearance"));
                    Assert.Equal(CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, CuiTheme.Glow).TideBase,
                        Colour(explicitAppearance.Resources, "CuiBackgroundBrush"));
                }

                Assert.Equal(CuiTheme.Glow, CuiSurfacePaletteCatalog.ActiveTheme);
            }
            finally
            {
                CuiSurfacePaletteCatalog.OverrideAccent = previousOverride;
                CuiSurfacePaletteCatalog.AccentOverride = previousAccent;
                CuiThemeScopeApplier.ApplyGlobalTheme(previousTheme);
            }
        }, CancellationToken.None);
    }

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

    private static Color Colour(ResourceDictionary resources, string key)
    {
        Assert.True(resources.TryGetResource(key, null, out var value));
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static object? Resource(ResourceDictionary resources, string key)
    {
        Assert.True(resources.TryGetResource(key, null, out var value));
        return value;
    }

    private static CuiNativeScene Scene(CuiAppearance? appearance = null) => new(
        "test.appearance-binding", "Appearance binding", "Home",
        new CuiRichParser().Parse("<Button Content='Theme binding fixture'/>") ,
        new CuiViewModel(), new CuiViewModel(), null!)
    { Appearance = appearance };

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
