// The app half of the canonical Boards failure contract ("Boards MUST return structured failures for
// BoardNotFound, PageNotFound, BlockNotFound, UnsupportedPageType, UnsupportedBlockType,
// InvalidLayoutTransition, RevisionConflict, PermissionDenied, SourceIngestionFailed,
// ScopedChatExternalAccessDenied, DependencyUnavailable and SharedEngineIncompatible, plus the global error
// contract"). The contract layer produces the identity; these cases prove the *application* boundary keeps
// it: a caller through the adapter gets the code rather than a sentence, and the status line the user reads
// names the code and the retryability instead of a reworded exception message.

using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsFailureSurfacingTests
{
    [Fact]
    public async Task ARefusedLayoutTransitionReachesTheCallerAsAnIdentityWithTheBlockRetained()
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, Path.Combine(root, "fail.9to1board"));
            var pageId = adapter.Document.Sections[0].Pages[0].Id;
            var viewModel = new BoardsViewModel(adapter);
            await viewModel.DispatchAsync("AddBlock", null);
            var blockId = adapter.Document.Sections[0].Pages[0].Blocks[^1].Id;

            // The page is Locked, so absolute placement is the invalid transition.
            var refused = await Assert.ThrowsAsync<BoardsFailureException>(
                () => adapter.PlaceBlockAsync(pageId, blockId, 64, 96, 320, 120, 2).AsTask());
            Assert.Equal(BoardsFailureCode.InvalidLayoutTransition, refused.Code);
            Assert.Equal(blockId, refused.TargetId);
            Assert.True(refused.Retryable, "switching the page to free placement is the documented retry");
            Assert.Contains("Unlocked", refused.Message, StringComparison.Ordinal);
            Assert.Equal(HavenRichBlockLayouts.Flow,
                adapter.Document.Sections[0].Pages[0].Blocks.Single(block => block.Id == blockId).Layout);

            // The same operation after the retry the failure names does land, which is what makes the
            // retryability claim more than decoration.
            await adapter.SetLayoutModeAsync(pageId, HavenRichPageLayoutModes.Unlocked);
            await adapter.PlaceBlockAsync(pageId, blockId, 64, 96, 320, 120, 2);
            var placed = adapter.Document.Sections[0].Pages[0].Blocks.Single(block => block.Id == blockId);
            Assert.Equal(HavenRichBlockLayouts.Absolute, placed.Layout);
            viewModel.Dispose();
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task AMissingBlockReachesTheUserAsBlockNotFoundRatherThanAnAnonymousSentence()
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, Path.Combine(root, "missing.9to1board"));
            var pageId = adapter.Document.Sections[0].Pages[0].Id;
            var before = adapter.Snapshot;
            var viewModel = new BoardsViewModel(adapter);
            try
            {
                await viewModel.MoveBlockToVerticalFlowAsync(pageId, "block-that-was-never-created");
                var status = (string)viewModel.Get("StatusText")!;
                Assert.Contains("BlockNotFound", status, StringComparison.Ordinal);
                Assert.Contains("block", status, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(before, adapter.Snapshot);
            }
            finally { viewModel.Dispose(); }
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task AMissingPageIsNamedAtTheAdapterBoundarySoAutomationCanActOnIt()
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, Path.Combine(root, "page.9to1board"));
            var refused = await Assert.ThrowsAsync<BoardsFailureException>(
                () => adapter.SetSnapAsync("page-does-not-exist", true).AsTask());
            Assert.Equal(BoardsFailureCode.PageNotFound, refused.Code);
            Assert.Equal("page-does-not-exist", refused.TargetId);
            Assert.False(adapter.Document.Sections[0].Pages[0].SnapToGrid);
        }
        finally { DeleteDirectory(root); }
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "boards-failures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (IOException) { }
    }
}
