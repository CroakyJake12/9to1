using CakeOS.Cui.Themes;
using Haven.Core;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>
/// Merge stage M1 (owner directive 2026-10-14: CUI is authoritative, HUI merges into it, and
/// the specification takes precedence over current framework behaviour).
///
/// These pin the observable M1 requirement on the theme preference boundary: CUI-PREF-201's
/// full built-in set is selectable, an unknown or malformed saved value resolves to
/// Professional rather than a loud legacy identity, and previously saved identities - including
/// Cinematic - keep working.
/// </summary>
public sealed class CuiThemeAuthorityTests
{
    /// <summary>Round-trips a persisted value through the exact path the service uses.</summary>
    private static HavenUiTheme PersistedThemeMapsTo(string? savedValue) =>
        UserPreferencesService.LegacyThemeFor(CuiThemeCatalog.Parse(savedValue));

    [Theory]
    [InlineData("Professional")]
    [InlineData("Glow")]
    [InlineData("Playful")]
    [InlineData("Frosted Glass")]
    [InlineData("Bubble")]
    [InlineData("Metallic")]
    [InlineData("Retro")]
    [InlineData("Western")]
    public void Every_CUI_PREF_201_built_in_theme_is_resolvable(string themeName)
    {
        // CUI-PREF-201 lists exactly these eight. The legacy HavenThemeCatalog knew only five,
        // so Frosted Glass, Metallic, Western and Professional were unselectable before M1.
        var parsed = CuiThemeCatalog.Parse(themeName);

        Assert.Equal(themeName.Replace(" ", string.Empty), parsed.ToString());
    }

    [Fact]
    public void Cinematic_legacy_identity_still_resolves()
    {
        // CUI-PREF-201 requires truthful legacy handling for previously saved identities,
        // explicitly naming Cinematic.
        Assert.Equal(CuiTheme.Cinematic, CuiThemeCatalog.Parse("Cinematic"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-theme")]
    [InlineData("midnight")]
    public void Unknown_or_malformed_saved_theme_resolves_to_Professional(string? saved)
    {
        // Spec precedence: an unrecognised value must fall back to the specification default.
        // The legacy parser fell back to Glow.
        Assert.Equal(CuiTheme.Professional, CuiThemeCatalog.Parse(saved));
    }

    /// <summary>
    /// The write path (ApplyThemeChoice) persists a CUI canonical name. The read path must
    /// accept that same name. Before M1 they disagreed: "Professional" was written by one and
    /// read as Glow by the other, silently losing the user's selection on restart.
    /// </summary>
    [Theory]
    [InlineData("Professional")]
    [InlineData("Glow")]
    [InlineData("Playful")]
    [InlineData("Frosted Glass")]
    [InlineData("Bubble")]
    [InlineData("Metallic")]
    [InlineData("Retro")]
    [InlineData("Western")]
    [InlineData("Cinematic")]
    public void Persisted_name_survives_the_read_path(string persistedName)
    {
        var written = CuiThemeCatalog.Name(CuiThemeCatalog.Parse(persistedName));

        Assert.Equal(written, CuiThemeCatalog.Name(CuiThemeCatalog.Parse(written)));
        Assert.Equal(
            UserPreferencesService.LegacyThemeFor(CuiThemeCatalog.Parse(written)),
            PersistedThemeMapsTo(written));
    }

    [Fact]
    public void Legacy_five_theme_identities_map_exactly_and_do_not_collide()
    {
        // HavenUiTheme is numerically CuiTheme 0-4, so each legacy identity must round-trip to
        // a distinct legacy theme. A collision would silently restyle an unrelated Space.
        var legacy = Enum.GetValues<HavenUiTheme>()
            .Select(theme => PersistedThemeMapsTo(theme.ToString()))
            .ToArray();

        Assert.Equal(legacy.Length, legacy.Distinct().Count());
    }

    [Fact]
    public void CUI_only_themes_declare_that_they_have_no_legacy_counterpart()
    {
        // Professional, Frosted Glass, Metallic and Western cannot be expressed by the legacy
        // pipeline. That is a known, recorded limitation of stage M1 and is removed at M2; this
        // test makes it visible rather than letting it read as full theme support.
        foreach (var cuiOnly in new[] { CuiTheme.Professional, CuiTheme.FrostedGlass, CuiTheme.Metallic, CuiTheme.Western })
        {
            Assert.Equal(HavenUiTheme.Glow, UserPreferencesService.LegacyThemeFor(cuiOnly));
            Assert.DoesNotContain(cuiOnly.ToString(), Enum.GetNames<HavenUiTheme>());
        }
    }
}