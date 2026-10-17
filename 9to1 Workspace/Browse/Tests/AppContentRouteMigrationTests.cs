using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed class AppContentRouteMigrationTests
{
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
