// Coverage for the canonical Board identity shapes the carried-forward review item names ("the block-id
// shape divergence that remains for conversion-created blocks"). The contract model has always used
// prefixed identities (HavenRichBoardContract.cs: "sec-", "page-", "block-", "item-", "cell-",
// "canvas-", "att-" each + 12 hex digits), but the app-side view entities defaulted to a bare 32-hex
// Guid, so every block the app created without naming an Id explicitly - checklist rows on a fallback
// session, tab panes, freeform boxes, conversion-created blocks and database links - carried a different
// identity shape from inserted paragraphs. Measured on the running app on 1 November 2026, an inserted
// database block appeared as id "8086b2b0cc664806bcdbe1c313bdc15b" while its paragraph neighbours were
// "block-b366537a9347" and "block-5510cc923890" (receipt J06/J07 against the physical .9to1board).

using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsIdentityShapeTests
{
    [Fact]
    public void TheAppViewEntitiesUseTheSameIdentityShapesAsTheContractModel()
    {
        Assert.Matches("^block-[0-9a-f]{12}$", new RichBoardBlock().Id);
        Assert.Matches("^page-[0-9a-f]{12}$", new RichBoardPage().Id);
        Assert.Matches("^sec-[0-9a-f]{12}$", new RichBoardSection().Id);
        Assert.NotEqual(new RichBoardBlock().Id, new RichBoardBlock().Id);
        // The contract's own shapes stay as they are: the two models must agree, not one drift to the other.
        Assert.Matches("^block-[0-9a-f]{12}$", new HavenRichBlock().Id);
        Assert.Matches("^page-[0-9a-f]{12}$", new HavenRichPage().Id);
    }

    [Fact]
    public async Task TheFallbackInsertionPathsStillCreateCanonicalBlockIdentities()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/identity-" + Guid.NewGuid().ToString("N"));
        var blocks = session.Document.Sections[0].Pages[0].Blocks;
        var before = blocks.Select(block => block.Id).ToHashSet(StringComparer.Ordinal);
        var viewModel = new BoardsViewModel(session);
        try
        {
            await viewModel.AddChecklistItemAsync("list-any", "Keep me");
            var created = Assert.Single(blocks, block => !before.Contains(block.Id));
            Assert.Matches("^block-[0-9a-f]{12}$", created.Id);
            Assert.Equal("checklist", created.Kind);
        }
        finally { viewModel.Dispose(); }
    }
}
