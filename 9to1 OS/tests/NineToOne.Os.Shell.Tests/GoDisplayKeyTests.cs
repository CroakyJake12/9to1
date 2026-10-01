using Haven.Application.Go;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

public sealed class GoDisplayKeyTests
{
    [Fact]
    public void SharedIdsAcrossCanonicalOwnersAndProvidersHaveDistinctRows()
    {
        var first = Result("provider", "Files", "file", "same-id", "1");
        var rows = new[] { first, first with { ProviderId = "other-provider" },
            first with { Reference = first.Reference with { Owner = "Projects" } },
            first with { Reference = first.Reference with { Kind = "folder" } } };
        Assert.Equal(rows.Length, rows.Select(ShellGoDisplayKey.Create).Distinct(StringComparer.Ordinal).Count());
    }
    [Fact]
    public void FieldSeparatorsCannotAliasAndRevisionDoesNotReplaceCanonicalRow()
    {
        Assert.NotEqual(ShellGoDisplayKey.Create(Result("a:b", "c", "kind", "id", "1")),
            ShellGoDisplayKey.Create(Result("a", "b:c", "kind", "id", "1")));
        var row = Result("provider", "Files", "file", "id", "1");
        Assert.Equal(ShellGoDisplayKey.Create(row), ShellGoDisplayKey.Create(row with { Reference = row.Reference with { Revision = "2" }, Label = "Renamed" }));
    }
    [Fact]
    public void ExactIdentifierCodeUnitsAreNotNormalizedIntoCollidingRows()
    {
        Assert.NotEqual(ShellGoDisplayKey.Create(Result("provider", "Files", "file", "\uD800", "1")),
            ShellGoDisplayKey.Create(Result("provider", "Files", "file", "\uD801", "1")));
    }
    private static GoResult Result(string provider, string owner, string kind, string id, string revision) =>
        new(provider, new(owner, kind, id, revision), "Label", "Files", [new("Open", "Open")]);
}
