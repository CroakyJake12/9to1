using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiBundledTypographyTests
{
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
                    <Cui><StackPanel>
                      <TextBlock text="9to1 chrome" />
                      <Button content="Open" />
                      <TextBlock text="user code" font-family="monospace" />
                    </StackPanel></Cui>
                    """), bindings, bindings, new FixtureReadiness());
            await host.ShowAsync(scene, CancellationToken.None);
            var window = new Window { Content = host, Width = 800, Height = 500 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var panel = Assert.IsType<StackPanel>(host.Content);
                var label = Assert.IsType<TextBlock>(panel.Children[0]);
                var button = Assert.IsType<Button>(panel.Children[1]);
                var code = Assert.IsType<TextBlock>(panel.Children[2]);
                Assert.Equal(new FontFamily(CuiNativeHost.BundledInterfaceFontFamily), label.FontFamily);
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
