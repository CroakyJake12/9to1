using Avalonia.Media;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Locks the theme-scoped behaviour of the accent Tide blend.
///
/// <c>CuiSurfacePaletteCatalog.ForCore</c> blends the accent into the Tide whenever an accent
/// anchor is active. That blend is intentionally scoped to <see cref="CuiTheme.Glow"/>, which
/// is the documented legacy identity transform. Every other theme - including Professional,
/// the default and the one the approved reference depicts - must present neutral surfaces with
/// the accent confined to controls (CUI-PRO-202: "restrained accents", rejecting controls made to
/// look professional only through application-local colour patches).
///
/// This was misdiagnosed twice during implementation, so it is pinned explicitly.
/// </summary>
public sealed class CuiThemeScopedTideBlendTests
{
    private const string LoudAccent = "#3527FF"; // the Chat indigo that produced the purple shell

    [Theory]
    [InlineData(CuiAppearance.SuperDark)]
    [InlineData(CuiAppearance.Dark)]
    public void Professional_presents_a_neutral_tide_regardless_of_accent(CuiAppearance appearance)
    {
        var withoutAccent = CuiSurfacePaletteCatalog.For("Chat", appearance, CuiTheme.Professional);
        var withAccent = CuiSurfacePaletteCatalog.For("Chat", appearance, CuiTheme.Professional, LoudAccent);

        // The blend must not reach Professional's surfaces.
        Assert.Equal(withoutAccent.TideBase, withAccent.TideBase);
        Assert.Equal(withoutAccent.TideColour, withAccent.TideColour);

        // And the surfaces must actually be neutral, not merely equal to each other.
        var tide = withAccent.TideColour;
        Assert.True(Math.Abs(tide.R - tide.B) <= 12, $"Professional TideColour {tide} is not neutral.");
    }

    [Theory]
    [InlineData(CuiAppearance.SuperDark)]
    [InlineData(CuiAppearance.Dark)]
    public void Professional_tide_matches_the_documented_neutral_charcoal(CuiAppearance appearance)
    {
        var palette = CuiSurfacePaletteCatalog.For("Chat", appearance, CuiTheme.Professional);

        Assert.Equal(
            appearance == CuiAppearance.SuperDark ? Color.Parse("#FF090B0E") : Color.Parse("#FF111419"),
            palette.TideColour);
    }

    [Fact]
    public void Glow_retains_the_legacy_accent_tide_identity_transform()
    {
        // Glow deliberately skips Express, so the Tide follows the accent. This is the
        // documented legacy behaviour and must not be "fixed" as though it were a defect.
        // (Chat already carries a default app accent, so the blend applies with or without an
        // explicit override - the meaningful comparison is between two different accents.)
        var first = CuiSurfacePaletteCatalog.For("Chat", CuiAppearance.SuperDark, CuiTheme.Glow, LoudAccent);
        var second = CuiSurfacePaletteCatalog.For("Chat", CuiAppearance.SuperDark, CuiTheme.Glow, "#654321");

        Assert.NotEqual(first.TideColour, second.TideColour);

        // The same two accents under Professional leave the surface identical.
        var professionalFirst = CuiSurfacePaletteCatalog.For("Chat", CuiAppearance.SuperDark, CuiTheme.Professional, LoudAccent);
        var professionalSecond = CuiSurfacePaletteCatalog.For("Chat", CuiAppearance.SuperDark, CuiTheme.Professional, "#654321");

        Assert.Equal(professionalFirst.TideColour, professionalSecond.TideColour);
    }

    [Fact]
    public void Professional_keeps_the_accent_on_controls()
    {
        // CUI-PREF-203: each app keeps its original accent even when the shell is neutral.
        var palette = CuiSurfacePaletteCatalog.For("Chat", CuiAppearance.SuperDark, CuiTheme.Professional, LoudAccent);

        Assert.NotEqual(palette.TideColour, palette.Accent);
        Assert.NotEqual(palette.Panel, palette.Accent);
    }
}