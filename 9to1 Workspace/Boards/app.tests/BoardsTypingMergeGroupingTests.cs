// The native signature of typed-text undo grouping, at the adapter boundary where the merges happen.
//
// Measured on the running standalone app on 1 November 2026 (instance run22, fresh sandbox home8/profile8,
// binary a7bc409ba650d0a34b66d303c2509dc4b9568b9cd9502c03a669b570e9530496): typing "GRAN" into a fresh block
// took richNotes.version 1 -> 9, and one Ctrl+Z took the physical file from "GRAN" to "GRA" (version 9 -> 7),
// receipts P02-P03, frames 161-162. The first grouping rule asked whether a merge changed a surface by one
// character; that is wrong, because the baseline a merge is diffed against stays at the last saved draft while
// the user types, so each merge carries the whole run since that baseline ("G", then "GR", then "GRA", then
// "GRAN"). Only the first merge looked like one character, so the run took four undo boundaries anyway.
//
// The rule that matches what the user did is: one merge, one changed text surface, same surface as the open
// group. These cases pin that, and the control run in the ledger shows the one-character rule failing here.

using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsTypingMergeGroupingTests
{
    // Created on the suite UI thread on first use and released when the case finishes.
    private readonly ThreadLocal<BoardsViewModel?> BoardsModel = new();

    [Fact]
    public async Task EachKeystrokeMergeCarriesTheWholeRunAgainstTheUnsavedBaseline()
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, Path.Combine(root, "run.9to1board"));
            adapter.HistoryCoalescingWindow = TimeSpan.FromSeconds(30);
            var pageId = adapter.Document.Sections[0].Pages[0].Id;
            var blockId = adapter.Document.Sections[0].Pages[0].Blocks[0].Id;
            // BoardsViewModel touches Avalonia's dispatcher, so it is created and driven on the suite's UI
            // thread: constructing it on an xUnit worker thread binds the dispatcher to that thread and the
            // mounted-surface classes then fail with "a different thread owns it".
            var edit = (string name, string text) => TestUiThread.Run(() =>
            {
                var model = BoardsModel.Value ??= new BoardsViewModel(adapter);
                model.EditText(name, text);
                return true;
            });

            string Canonical() => adapter.Document.Sections[0].Pages[0]
                .Blocks.Single(block => block.Id == blockId).Text;

            // The app types one control-text submission per keystroke and each one merges immediately
            // (ContractSessionAdapter.MarkDirty), with nothing saving in between.
            foreach (var text in new[] { "G", "GR", "GRA", "GRAN" })
            {
                edit("t_" + blockId, text);
                Assert.Equal(text, Canonical());      // reading Document forces the merge, like the host does
            }

            // One boundary for the whole run: this is what failed before the rule was corrected, because only
            // the first merge changed a surface by a single character.
            Assert.True(await adapter.UndoAsync(), "one undo must take back the typed run, not one letter");
            Assert.Equal(string.Empty, Canonical());
            Assert.True(await adapter.RedoAsync());
            Assert.Equal("GRAN", Canonical());
            ReleaseModel();
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task DifferentTextSurfacesGroupSeparately()
    {
        // A change of surface is a change of action, so it opens its own boundary: typing in a block, then
        // renaming the page, then typing in the block again must undo as three steps in the order the user
        // made them, never as one blurred step and never out of order.
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, Path.Combine(root, "two.9to1board"));
            adapter.HistoryCoalescingWindow = TimeSpan.FromSeconds(30);
            var blockId = adapter.Document.Sections[0].Pages[0].Blocks[0].Id;
            var titleBefore = adapter.Document.Sections[0].Pages[0].Title;
            var edit = (string name, string text) => TestUiThread.Run(() =>
            {
                var model = BoardsModel.Value ??= new BoardsViewModel(adapter);
                model.EditText(name, text);
                return true;
            });

            string Block() => adapter.Document.Sections[0].Pages[0]
                .Blocks.Single(block => block.Id == blockId).Text;
            string Title() => adapter.Document.Sections[0].Pages[0].Title;

            edit("t_" + blockId, "one");
            _ = adapter.Document;
            edit("PageTitleBox", "Renamed page");
            _ = adapter.Document;
            edit("t_" + blockId, "onetwo");
            _ = adapter.Document;

            Assert.Equal("onetwo", Block());
            Assert.Equal("Renamed page", Title());

            Assert.True(await adapter.UndoAsync());
            Assert.Equal("one", Block());                  // the last block run back to the surface switch
            Assert.Equal("Renamed page", Title());         // the rename is a separate action, still applied

            Assert.True(await adapter.UndoAsync());
            Assert.Equal("one", Block());
            Assert.Equal(titleBefore, Title());            // then the rename undoes on its own

            Assert.True(await adapter.UndoAsync());
            Assert.Equal(string.Empty, Block());           // and finally the first typed run
            ReleaseModel();
        }
        finally { DeleteDirectory(root); }
    }

    private void ReleaseModel() => TestUiThread.Run(() =>
    {
        BoardsModel.Value?.Dispose();
        BoardsModel.Value = null;
        return true;
    });

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "boards-merge-grouping-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (IOException) { }
    }
}
