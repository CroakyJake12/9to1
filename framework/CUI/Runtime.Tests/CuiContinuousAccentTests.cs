using Avalonia.Media;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Owner ruling 2026-10-14: the per-app accent selector must accept ANY colour. The discrete
/// <see cref="CuiAccentColour"/> named-palette enum is not the product model; it is one entry
/// point. These lock in the continuous behaviour so a future change cannot quietly narrow the
/// accent selector back to a fixed swatch list.
///
/// CUI-PREF-203 requires Override Accent Colours? modes (None / All Apps / Individual Apps)
/// with a per-app colour selector, and names per-app identities including Write blue, Present
/// red, Data green, Boards ultraviolet, Picture pink-red, Wave cyan and Motion orchid.
/// </summary>
public sealed class CuiContinuousAccentTests
{
    [Theory]
    [InlineData("#2563EB")]  // Write blue
    [InlineData("#DC2626")]  // Present red
    [InlineData("#16A34A")]  // Data green
    [InlineData("#7C3AED")]  // Boards ultraviolet
    [InlineData("#EC4899")]  // Picture pink-red
    [InlineData("#06B6D4")]  // Wave cyan
    [InlineData("#D946EF")]  // Motion orchid
    [InlineData("#8A2BE2")]  // an arbitrary colour that is in no named palette
    public void Any_hex_colour_is_accepted_as_an_accent_override(string accent)
    {
        var palette = CuiSurfacePaletteCatalog.For("Chat", CuiAppearance.SuperDark, CuiTheme.Professional, accent);

        Assert.NotEqual(default, palette.Accent);
        // The chosen colour stays an anchor; the theme still owns surfaces and interaction.
        Assert.NotEqual(Color.Parse(accent), palette.Accent);
    }

    [Fact]
    public void Two_different_arbitrary_colours_produce_two_different_palettes()
    {
        var first = CuiSurfacePaletteCatalog.For("Chat", CuiAppearance.SuperDark, CuiTheme.Professional, "#123456");
        var second = CuiSurfacePaletteCatalog.For("Chat", CuiAppearance.SuperDark, CuiTheme.Professional, "#654321");

        Assert.NotEqual(first.Accent, second.Accent);
        Assert.NotEqual(first.AccentSecondary, second.AccentSecondary);
    }

    [Fact]
    public void Null_override_preserves_the_owning_app_default()
    {
        // CUI-PREF-203: a null choice must preserve the original app accent, not blank it.
        var palette = CuiSurfacePaletteCatalog.For("Chat", CuiAppearance.SuperDark, CuiTheme.Professional, null);

        Assert.NotEqual(Color.Parse("#000000"), palette.Accent);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#FFF")]
    [InlineData("")]
    [InlineData("#1234567")]
    public void Malformed_accent_colours_are_rejected_rather_than_silently_accepted(string value)
    {
        // The #RRGGBB contract is explicit; a malformed value must not quietly become black.
        Assert.Throws<ArgumentException>(() =>
            CuiSurfacePaletteCatalog.For("Chat", CuiAppearance.SuperDark, CuiTheme.Professional, value));
    }

    [Fact]
    public void Every_approvals_named_app_accent_is_expressed_as_a_continuous_hex_colour()
    {
        // The seven identities CUI-PREF-203 requires are colours, not members of a fixed enum.
        foreach (var accent in CuiAppAccentCatalog.Defaults)
        {
            Assert.StartsWith("#", accent.Colour);
            Assert.Equal(7, accent.Colour.Length);
        }
    }
}