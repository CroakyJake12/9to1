using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsTabberTests
{
    [Fact]
    public async Task Named_panes_use_the_same_board_session_and_preserve_content_ids_through_delete_undo_reopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-tabber-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "tabs.9to1board");
            await using (var seed = await RichBoardSession.CreateNewAsync(store, "Tabber")) await seed.SaveAsAsync(path);
            string parentPage, blockID, firstPage, firstBlock;
            await using (var board = await ContractSessionAdapter.OpenAsync(store, path))
            {
                parentPage = board.Document.Sections[0].Pages[0].Id;
                blockID = await board.AddTabberAsync(parentPage, null);
                var tabs = board.Document.Sections[0].Pages[0].Blocks.Single().Tabber!;
                firstPage = tabs.Panes[0].PageId;
                var child = new BoardsViewModel(board);
                Assert.True(child.SelectPageById(firstPage));
                firstBlock = child.PageBlocks.Single().Id;
                child.EditText("t_" + firstBlock, "Ordinary content inside tab one");
                await board.SaveAsync();
                await board.ChangeTabberAsync(parentPage, blockID, "rename", tabs.Panes[0].Id, "Notes");
                await board.ChangeTabberAsync(parentPage, blockID, "add");
                tabs = board.Document.Sections[0].Pages[0].Blocks.Single().Tabber!;
                var second = tabs.Panes[1];
                Assert.Equal("Notes", tabs.Panes[0].Title);
                await board.ChangeTabberAsync(parentPage, blockID, "remove", second.Id);
                Assert.DoesNotContain(board.Document.Sections[0].Pages, page => page.Id == second.PageId);
                Assert.True(await board.UndoAsync());
                Assert.Contains(board.Document.Sections[0].Pages, page => page.Id == second.PageId);
                var parent = new BoardsViewModel(board);
                Assert.True(parent.SelectPageById(parentPage));
                await parent.DeleteBlockAsync(blockID);
                Assert.Single(board.Document.Sections[0].Pages);
                Assert.True(await board.UndoAsync());
                await board.SaveAsync();
                child.Dispose(); parent.Dispose();
            }
            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var panePage = reopened.Document.Sections[0].Pages.Single(page => page.Id == firstPage);
            Assert.Equal(blockID, panePage.TabberOwnerBlockId);
            Assert.Equal(firstBlock, panePage.Blocks.Single().Id);
            Assert.Equal("Ordinary content inside tab one", panePage.Blocks.Single().Text);
            Assert.Equal("Notes", reopened.Document.Sections[0].Pages.Single(page => page.Id == parentPage).Blocks.Single().Tabber!.Panes[0].Title);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Invalid_or_self_referencing_panes_cannot_hide_existing_pages()
    {
        var notes = HavenRichNotes.Create("Invalid");
        var page = notes.Sections[0].Pages[0];
        var block = new HavenRichBlock { Kind = HavenRichBlockKind.Tabber, Tabber = new() };
        block.Tabber.Panes.Add(new("pane", "Self", page.Id)); block.Tabber.SelectedPaneId = "pane";
        page.Blocks.Add(block);
        Assert.Throws<InvalidDataException>(() => HavenRichNotesValidator.Validate(notes));
    }
}
