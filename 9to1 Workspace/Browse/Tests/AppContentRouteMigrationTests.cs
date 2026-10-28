using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed class AppContentRouteMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Omnibox_library_routes_roundtrip_real_queries_and_reject_noncanonical_or_malformed_data(bool filesOnly)
    {
        foreach (var text in new[] { "quarterly notes / 2026?", "Résumé 日本語", new string('界', 256) })
        {
            var route = BrowseOmniboxSearchRoute.Create(text, filesOnly);
            Assert.True(BrowseOmniboxSearchRoute.TryParse(route, out var query, out var files));
            Assert.Equal(text, query); Assert.Equal(filesOnly, files);
        }
        foreach (var value in new[] { "browse://home/library/search/all/_w", "browse://home/library/search/all/eB",
            "browse://home/library/search/unknown/eA", "browse://newtab/library/search/all/eA" })
            Assert.False(BrowseOmniboxSearchRoute.TryParse(AppContentAddress.Parse(value), out _, out _));
        Assert.Throws<ArgumentException>(() => BrowseOmniboxSearchRoute.Create("line\nbreak", filesOnly));
        Assert.Throws<ArgumentException>(() => BrowseOmniboxSearchRoute.Create(new string('x', 257), filesOnly));
    }

    [Theory]
    [InlineData("9to1://assistants", "9to1://spaces/assistants")]
    [InlineData("9to1://assistants/assistant/a0000000-0000-0000-0000-000000000029", "9to1://spaces/assistants/assistant/a0000000-0000-0000-0000-000000000029")]
    [InlineData("9to1://spaces/assistants", "9to1://spaces/assistants")]
    [InlineData("9to1://files", "9to1://files")]
    [InlineData("browse://assistants", "browse://assistants")]
    [InlineData("cui://assistants", "cui://assistants")]
    [InlineData("https://assistants.example/", "https://assistants.example/")]
    public void OnlyDeprecatedNativeProductAliasesMoveAndCanonicalizationIsIdempotent(string legacy, string canonical)
    {
        var result = AppContentRouteMigration.Canonicalize(AppContentAddress.Parse(legacy));
        Assert.Equal(canonical, result.Value);
        Assert.Same(result, AppContentRouteMigration.Canonicalize(result));
    }
}
