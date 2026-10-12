using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiBundledTypographyTests
{
    [Fact]
    public async Task Direct_CUI_hosts_use_canonical_medium_chrome_across_themes_without_restyling_document_content()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            foreach (var theme in new[] { "Professional", "Glow", "Playful", "Bubble", "Cinematic" })
            foreach (var appearance in Enum.GetValues<CuiAppearance>())
            {
                var registry = new CuiControlRegistry();
                var document = new TextBlock { Text = "User document", FontWeight = FontWeight.Normal, FontFamily = new FontFamily("serif") };
                registry.RegisterObjectRenderer("Document", _ => document);
                using var loader = new CuiControlLoader(registry);
                loader.SetAppearance(appearance);
                var loaded = loader.LoadMarkup($"<Theme Name='{theme}'><Container Type='Vertical Stack'>" +
                    "<Text id='label' Text='Chrome'/><Button Content='Action'/><Input Type='Text' Value='Editable'/>" +
                    "<Text id='heading' Text='Explicit heading' FontWeight='Bold'/><Object Type='Document'/>" +
                    "</Container></Theme>");
                Assert.Empty(loaded.Diagnostics);
                var root = Assert.IsType<CuiContainerControl>(loaded.Root);
                var window = new Window { Content = root, Width = 400, Height = 300 };
                window.Show();
                try
                {
                    window.UpdateLayout();
                    var label = Assert.IsType<TextBlock>(root.AuthoredChildren[0]);
                    var button = Assert.IsType<Button>(root.AuthoredChildren[1]);
                    var input = Assert.IsType<TextBox>(root.AuthoredChildren[2]);
                    Assert.Equal(FontWeight.Medium, label.FontWeight);
                    Assert.Equal(FontWeight.Medium, button.FontWeight);
                    Assert.Equal(FontWeight.Medium, input.FontWeight);
                    Assert.Equal(CuiPropertyLevel.Theme, CuiPropertyPresentation.For(button).Resolve("FontWeight").Level);
                    Assert.Equal(FontWeight.Bold, Assert.IsType<TextBlock>(root.AuthoredChildren[3]).FontWeight);
                    Assert.Equal(FontWeight.Normal, document.FontWeight);
                    Assert.Equal("serif", document.FontFamily.Name);
                    using var competingBackendValue = button.SetValue(TemplatedControl.FontWeightProperty, FontWeight.Light, BindingPriority.Animation);
                    Assert.Equal(FontWeight.Medium, button.FontWeight);
                }
                finally { window.Close(); }
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Native_chrome_loads_the_bundled_Montserrat_glyphs_and_notice()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var family = new FontFamily(CuiNativeHost.BundledInterfaceFontFamily);
            foreach (var weight in new[] { FontWeight.Medium, FontWeight.SemiBold, FontWeight.Bold })
            {
                Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(family, FontStyle.Normal, weight), out var glyphs));
                Assert.Contains("Montserrat", glyphs!.FamilyName, StringComparison.Ordinal);
                Assert.NotEqual((ushort)0, glyphs.CharacterToGlyphMap['A']);
                Assert.NotEqual((ushort)0, glyphs.CharacterToGlyphMap['9']);
            }
            using var notice = AssetLoader.Open(new Uri("avares://CakeOS.Cui.Runtime/Assets/Fonts/OFL-Montserrat.txt"));
            using var reader = new StreamReader(notice);
            Assert.Contains("SIL OPEN FONT LICENSE", reader.ReadToEnd(), StringComparison.Ordinal);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Embedded_scene_chrome_inherits_bundled_medium_font_without_overriding_content_fonts()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        var completed = await session.Dispatch<bool>(async () =>
        {
            var bindings = new CuiViewModel();
            using var host = new CuiSceneHost();
            var scene = new CuiNativeScene("font-fixture", "Font fixture", "Home",
                new CuiRichParser().Parse("""
                    <Cui><Container Type="Vertical Stack">
                      <Text text="9to1 chrome" />
                      <Button content="Open" />
                      <Text text="user code" font-family="monospace" />
                    </Container></Cui>
                    """), bindings, bindings, new FixtureReadiness());
            await host.ShowAsync(scene, CancellationToken.None);
            var window = new Window { Content = host, Width = 800, Height = 500 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var panel = Assert.IsType<CuiContainerControl>(host.Content);
                var label = Assert.IsType<TextBlock>(panel.AuthoredChildren[0]);
                var button = Assert.IsType<Button>(panel.AuthoredChildren[1]);
                var code = Assert.IsType<TextBlock>(panel.AuthoredChildren[2]);
                Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(label.FontFamily, label.FontStyle, label.FontWeight), out var actualGlyphs));
                Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(CuiNativeHost.BundledInterfaceFontFamily), label.FontStyle, label.FontWeight), out var bundledGlyphs));
                Assert.Same(bundledGlyphs, actualGlyphs);
                Assert.Equal(label.FontFamily, button.FontFamily);
                Assert.Equal(FontWeight.Medium, label.FontWeight);
                Assert.Equal(FontWeight.Medium, button.FontWeight);
                Assert.Equal("monospace", code.FontFamily.Name);
                Assert.True(label.Bounds.Width > 0 && label.Bounds.Height > 0);
                Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    private sealed class FixtureReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "font-fixture", "Test-only scene"));
    }
}

public sealed class CuiBundledFontTestApplication : Application
{
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this);

    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<CuiBundledFontTestApplication>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }));
}
