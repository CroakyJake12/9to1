// UX27-BOARD-016/017/018 — the owner's Enter rule on the rich Boards page:
// Enter ends the current BlockID and starts the next one (a new block of the same
// kind, placed immediately after the original); Shift+Enter keeps the text inside
// the same BlockID. These tests drive the owning view-model operations that the
// rendered editors call, then save and reopen the real .9to1board file.
//
// BLOCK MODEL: the contract stores a list as one block holding Items[], while the
// editor projects each item to its own "{list block}:{item id}" view row. Seeding
// therefore uses Items, and a divider needs its Divider payload to project at all.
//
// THREADING: view-model only, no Avalonia controls are constructed, so these run
// outside the headless UI thread helper used by BoardsEditorTests.

using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsEnterBehaviourTests
{
    [Fact]
    public async Task Enter_splits_a_paragraph_at_the_caret_into_the_next_block_and_survives_save_reopen()
    {
        var root = TempRoot("boards-enter-paragraph");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "maths.9to1board");
            using (var store = new JsonFileHavenBoardStore(root))
            {
                await SeedAsync(store, path);
                await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
                {
                    var vm = new BoardsViewModel(adapter);
                    await EnsureEditModeAsync(vm);
                    vm.FocusBlock("p-sum");

                    Assert.True(await vm.SplitTextBlockAtAsync("p-sum", "x^2 + ".Length));
                    var blocks = Blocks(adapter);
                    Assert.Equal(4, blocks.Count);
                    Assert.Equal("x^2 + ", blocks[0].Text);
                    Assert.Equal("y = 3x", blocks[1].Text);
                    Assert.Equal("paragraph", blocks[1].Kind);
                    Assert.Equal("left right", blocks[2].Text);
                    Assert.Equal(vm.SelectedBlockId, blocks[1].Id);

                    await vm.DispatchAsync("SaveBoard", null);
                }
                await using (var fresh = await ContractSessionAdapter.OpenAsync(store, path))
                {
                    var reopened = Blocks(fresh);
                    Assert.Equal(4, reopened.Count);
                    Assert.Equal("x^2 + ", reopened[0].Text);
                    Assert.Equal("y = 3x", reopened[1].Text);
                    Assert.Equal("paragraph", reopened[1].Kind);
                }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Enter_in_a_bulleted_row_splits_it_in_place_and_keeps_the_same_kind()
    {
        var root = TempRoot("boards-enter-bulleted");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "maths.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await SeedAsync(store, path);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var vm = new BoardsViewModel(adapter);
            await EnsureEditModeAsync(vm);

            // The renderer names list-row editors chk_{rowId}; the previous code resolved
            // only "checklist" kinds, so text typed in a bulleted row was dropped and
            // Enter produced a checkbox item.
            Assert.True(await vm.SplitTextBlockAtAsync("list-1:i-1", "left ".Length));
            var blocks = Blocks(adapter);
            Assert.Equal(4, blocks.Count);
            Assert.Equal("left ", blocks[1].Text);
            Assert.Equal("right", blocks[2].Text);
            Assert.Equal("bulleted", blocks[1].Kind);
            Assert.Equal("bulleted", blocks[2].Kind);
            // The new row stays inside the same list block and directly after its source.
            Assert.StartsWith("list-1:", blocks[2].Id, StringComparison.Ordinal);
            Assert.Equal("div-1", blocks[3].Id);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Enter_at_the_end_of_a_list_row_adds_the_next_row_directly_after_it()
    {
        var root = TempRoot("boards-enter-list-end");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "maths.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await SeedAsync(store, path);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var vm = new BoardsViewModel(adapter);
            await EnsureEditModeAsync(vm);

            await vm.ListEnterAsync("list-1:i-1", "left right".Length);
            var blocks = Blocks(adapter);
            Assert.Equal(4, blocks.Count);
            // The new row sits between the original row and the following divider,
            // never appended at the end of the page.
            Assert.Equal("list-1:i-1", blocks[1].Id);
            Assert.Equal("bulleted", blocks[2].Kind);
            Assert.Equal(string.Empty, blocks[2].Text);
            Assert.Equal("div-1", blocks[3].Id);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task A_split_list_row_keeps_its_position_through_save_and_reopen()
    {
        var root = TempRoot("boards-enter-list-order");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "maths.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await SeedAsync(store, path);
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var vm = new BoardsViewModel(adapter);
                await EnsureEditModeAsync(vm);
                Assert.True(await vm.SplitTextBlockAtAsync("list-1:i-1", "left ".Length));
                await vm.DispatchAsync("SaveBoard", null);
            }
            await using (var fresh = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var reopened = Blocks(fresh);
                Assert.Equal(4, reopened.Count);
                Assert.Equal("left ", reopened[1].Text);
                Assert.Equal("right", reopened[2].Text);
                Assert.Equal("bulleted", reopened[1].Kind);
                Assert.Equal("bulleted", reopened[2].Kind);
                Assert.Equal("divider", reopened[3].Kind);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Soft_break_keeps_text_in_one_block_and_a_non_splittable_block_reports_explicitly()
    {
        var root = TempRoot("boards-enter-soft");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "maths.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await SeedAsync(store, path);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var vm = new BoardsViewModel(adapter);
            await EnsureEditModeAsync(vm);

            // Shift+Enter inside a list row: same BlockID, one extra line, no sibling.
            vm.CommitListSoftBreak("list-1:i-1", "left\nright");
            var afterSoft = Blocks(adapter);
            Assert.Equal(3, afterSoft.Count);
            Assert.Equal("left\nright", afterSoft[1].Text);

            // Enter on a divider cannot split; the surface must say so rather than
            // swallow the key press.
            Assert.False(await vm.SplitTextBlockAtAsync("div-1", 0));
            Assert.Equal(3, Blocks(adapter).Count);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string TempRoot(string name) =>
        Path.Combine(Path.GetTempPath(), name + "-" + Guid.NewGuid().ToString("N"));

    private static List<RichBoardBlock> Blocks(ContractSessionAdapter adapter) =>
        adapter.Document.Sections.Single().Pages.Single().Blocks;

    private static async Task EnsureEditModeAsync(BoardsViewModel vm)
    {
        if (!vm.IsEditMode)
            await vm.DispatchAsync("ToggleEditView", null);
        Assert.True(vm.IsEditMode);
    }

    private static async Task SeedAsync(JsonFileHavenBoardStore store, string path)
    {
        await using var owner = await RichBoardSession.CreateNewAsync(store, "Maths revision");
        await owner.MutateAsync(rich =>
        {
            var page = rich.Sections.Single().Pages.Single();
            page.Blocks.Clear();
            page.Blocks.Add(new() { Id = "p-sum", PlainText = "x^2 + y = 3x", Order = 0 });
            page.Blocks.Add(new()
            {
                Id = "list-1",
                Kind = HavenRichBlockKind.BulletList,
                Items = [new HavenRichListItem { Id = "i-1", Text = "left right" }],
                Order = 1,
            });
            page.Blocks.Add(new()
            {
                Id = "div-1",
                Kind = HavenRichBlockKind.Divider,
                Divider = new HavenRichDivider(),
                Order = 2,
            });
        });
        await owner.SaveAsAsync(path);
    }
}
