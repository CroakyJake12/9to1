using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiBundledFallbackFontTests
{
    [Fact]
    public void Native_emoji_fallback_precedes_bundled_font_and_browser_keeps_bundled_order()
    {
        var native = CuiNativeHost.CreateFontFallbacks("Noto Color Emoji", browser: false);
        Assert.Equal("Noto Color Emoji", native[0].FontFamily.Name);
        Assert.Equal(new FontFamily(CuiNativeHost.BundledEmojiFontFamily), native[1].FontFamily);
        Assert.Equal(new FontFamily(CuiNativeHost.BundledSymbolFontFamily), native[2].FontFamily);

        var browser = CuiNativeHost.CreateFontFallbacks("Noto Color Emoji", browser: true);
        Assert.Equal(new FontFamily(CuiNativeHost.BundledEmojiFontFamily), browser[0].FontFamily);
        Assert.Equal(new FontFamily(CuiNativeHost.BundledSymbolFontFamily), browser[1].FontFamily);
    }

    [Fact]
    public async Task Missing_preferred_emoji_font_falls_through_to_bundled_emoji_glyphs()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiMissingPreferredEmojiFontTestApplication));
        await session.Dispatch(() =>
        {
            var authored = new Typeface(new FontFamily(CuiNativeHost.BundledInterfaceFontFamily),
                FontStyle.Normal, FontWeight.Normal);
            Assert.True(FontManager.Current.TryMatchCharacter(0x1f600, FontStyle.Normal, FontWeight.Normal,
                FontStretch.Normal, authored.FontFamily, null, out var fallback));
            Assert.Equal(new FontFamily(CuiNativeHost.BundledEmojiFontFamily), fallback.FontFamily);
            Assert.NotEqual((ushort)0, fallback.GlyphTypeface.CharacterToGlyphMap[0x1f600]);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Missing_chrome_symbols_and_joined_emoji_use_bundled_glyphs_without_changing_the_authored_font()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledOnlyEmojiFontTestApplication));
        await session.Dispatch(() =>
        {
            var authored = new Typeface(new FontFamily(CuiNativeHost.BundledInterfaceFontFamily), FontStyle.Normal, FontWeight.Medium);
            Assert.True(FontManager.Current.TryGetGlyphTypeface(authored, out var primary));
            Assert.Equal((ushort)0, primary!.CharacterToGlyphMap[0x21b6]);
            Assert.Equal((ushort)0, primary.CharacterToGlyphMap[0x1f469]);
            foreach (var (codepoint, family) in new[]
            {
                (0x21b6, CuiNativeHost.BundledSymbolFontFamily),
                (0x21b7, CuiNativeHost.BundledSymbolFontFamily),
                (0x2713, CuiNativeHost.BundledSymbolFontFamily), // Canonical checked choice mark.
                (0x1f469, CuiNativeHost.BundledEmojiFontFamily),
                (0x1f3fd, CuiNativeHost.BundledEmojiFontFamily),
                (0x1f4bb, CuiNativeHost.BundledEmojiFontFamily)
            })
            {
                Assert.True(FontManager.Current.TryMatchCharacter(codepoint, FontStyle.Normal, FontWeight.Medium,
                    FontStretch.Normal, authored.FontFamily, null, out var fallback));
                Assert.Equal(new FontFamily(family), fallback.FontFamily);
                Assert.NotEqual((ushort)0, fallback.GlyphTypeface.CharacterToGlyphMap[codepoint]);
            }
            foreach (var text in new[] { "↶↷", "✓", "👩🏽‍💻" })
            {
                using var layout = new TextLayout(text, authored, 24, Brushes.Black);
                var runs = layout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().ToArray();
                Assert.NotEmpty(runs);
                Assert.Equal(text, string.Concat(runs.Select(run => run.Text.ToString())));
                var glyphs = runs.SelectMany(run => run.GlyphRun.GlyphInfos).ToArray();
                Assert.All(glyphs, glyph => Assert.NotEqual((ushort)0, glyph.GlyphIndex));
                // The joined woman + skin tone + computer sequence must be one
                // shaped emoji, not separate tofu or unjoined component glyphs.
                Assert.Equal(text == "↶↷" ? 2 : 1, glyphs.Length);
                Assert.True(layout.Width > 0 && layout.Height > 0);
            }
            Assert.Equal(new FontFamily(CuiNativeHost.BundledInterfaceFontFamily), authored.FontFamily);
            Assert.Contains("Montserrat", authored.GlyphTypeface.FamilyName, StringComparison.Ordinal);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Fallback_font_redistribution_notices_and_provenance_are_packaged()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            foreach (var (file, required) in new[]
            {
                ("NotoEmoji/OFL.txt", "SIL OPEN FONT LICENSE"),
                ("DejaVuSans/LICENSE.txt", "Arev Fonts Copyright"),
                ("fallback-provenance.json", "sha256")
            })
            {
                using var stream = AssetLoader.Open(new Uri("avares://CakeOS.Cui.Runtime/Assets/Fonts/" + file));
                using var reader = new StreamReader(stream);
                Assert.Contains(required, reader.ReadToEnd(), StringComparison.Ordinal);
            }
        }, CancellationToken.None);
    }
}

public sealed class CuiMissingPreferredEmojiFontTestApplication : Application
{
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<CuiMissingPreferredEmojiFontTestApplication>()
        .With(new FontManagerOptions
        {
            DefaultFamilyName = CuiNativeHost.BundledInterfaceFontFamily,
            FontFallbacks = CuiNativeHost.CreateFontFallbacks("Codex Missing Emoji Font", browser: false)
        })
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class CuiBundledOnlyEmojiFontTestApplication : Application
{
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<CuiBundledOnlyEmojiFontTestApplication>()
        .With(new FontManagerOptions
        {
            DefaultFamilyName = CuiNativeHost.BundledInterfaceFontFamily,
            FontFallbacks = CuiNativeHost.CreateFontFallbacks(null, browser: true)
        })
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
