using Avalonia;
using Avalonia.Controls;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsListBehaviorTests
{
    [Theory]
    [InlineData(HavenRichBlockKind.Checklist, false)]
    [InlineData(HavenRichBlockKind.BulletList, false)]
    [InlineData(HavenRichBlockKind.NumberedList, false)]
    [InlineData(HavenRichBlockKind.Checklist, true)]
    [InlineData(HavenRichBlockKind.BulletList, true)]
    [InlineData(HavenRichBlockKind.NumberedList, true)]
    public async Task Empty_Enter_outdents_or_exits_without_losing_surrounding_items(HavenRichBlockKind kind, bool nested)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-list-enter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root); var path = Path.Combine(root, "list.9to1board");
            await Seed(store, path, kind, nested ? 2 : 0, empty: true);
            string focus;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var vm = new BoardsViewModel(adapter); if (!vm.IsEditMode) await vm.DispatchAsync("ToggleEditView", null);
                Assert.True(await vm.SplitTextBlockAtAsync("list:b", 0)); focus = vm.SelectedBlockId;
                var raw = (await adapter.GetDetachedContractDocumentAsync()).RichNotes!;
                var page = raw.Sections.Single().Pages.Single();
                if (nested)
                {
                    Assert.Equal("list:b", focus);
                    Assert.Equal(1, page.Blocks.Single(block => block.Id == "list").Items.Single(item => item.Id == "b").Level);
                }
                else
                {
                    Assert.Equal(HavenRichBlockKind.Paragraph, page.Blocks.Single(block => block.Id == focus).Kind);
                    Assert.Equal(new[] { "a", "c" }, page.Blocks.SelectMany(block => block.Items).Select(item => item.Id));
                    Assert.True(page.Blocks.SelectMany(block => block.Items).Single(item => item.Id == "c").Checked);
                }
                Assert.True(await adapter.UndoAsync());
                Assert.Equal(3, (await adapter.GetDetachedContractDocumentAsync()).RichNotes!.Sections.Single().Pages.Single().Blocks.Single(block => block.Id == "list").Items.Count);
                Assert.True(await adapter.RedoAsync()); await adapter.SaveAsync();
            }
            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.Contains(reopened.Document.Sections.Single().Pages.Single().Blocks, block => block.Id == focus);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Projected_row_grip_moves_duplicates_and_deletes_with_fresh_tickets_and_roundtrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-list-actions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root); var path = Path.Combine(root, "list.9to1board");
            await Seed(store, path, HavenRichBlockKind.Checklist, 1, empty: false);
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var vm = new BoardsViewModel(adapter); if (!vm.IsEditMode) await vm.DispatchAsync("ToggleEditView", null);
                var grip = new BlockGripCui.BlockGripContext(vm, "list:b");
                await grip.DispatchAsync("ToggleActions", null);
                Assert.True(grip.TryGetValue("CanManage", out var available)); Assert.Equal(true, available);
                Assert.True(grip.TryGetValue("DuplicateRequest", out var duplicate));
                await grip.DispatchAsync("ApplyBlockAction", duplicate);
                var copy = adapter.Document.Sections.Single().Pages.Single().Blocks.Single(block => block.Text == "Middle" && block.Id != "list:b");
                Assert.Equal(1, copy.Level); Assert.True(copy.Bold);
                var remove = new BlockGripCui.BlockGripContext(vm, copy.Id);
                await remove.DispatchAsync("ToggleActions", null); remove.TryGetValue("DeleteRequest", out var delete);
                await remove.DispatchAsync("ApplyBlockAction", delete);
                Assert.Equal(3, adapter.Document.Sections.Single().Pages.Single().Blocks.Count);

                var stale = new BlockGripCui.BlockGripContext(vm, "list:b");
                await stale.DispatchAsync("ToggleActions", null); stale.TryGetValue("DeleteRequest", out var staleDelete);
                vm.EditText("chk_list_b", "Changed while menu open");
                await stale.DispatchAsync("ApplyBlockAction", staleDelete);
                Assert.Contains(adapter.Document.Sections.Single().Pages.Single().Blocks, block => block.Id == "list:b");

                var target = adapter.CaptureBlockAction(vm.SelectedPageId, "list:a"); Assert.NotNull(target);
                var destination = TestUiThread.Run(() =>
                {
                    var host = new StackPanel { Width = 600 };
                    BlockRenderer.Rebuild(host, vm, []);
                    var window = new Window { Content = host, Width = 620, Height = 700 };
                    window.Show(); window.Measure(new Size(620, 700)); window.Arrange(new Rect(0, 0, 620, 700));
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    var result = BlockReorderPointer.DestinationForPointer(host, "list:a", 0, host.Bounds.Height + 10);
                    window.Close(); return result;
                });
                Assert.Equal(2, destination);
                var move = await adapter.ApplyBlockMoveAsync(target!, destination!.Value, () => true);
                // The refusal code is reported so a failure identifies the owning gate rather
                // than only showing a bare false.
                Assert.True(move.Applied, "List-row move refused with code " + move.Code + " for " + target!.BlockId);
                await adapter.SaveAsync();
            }
            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.Equal(new[] { "list:b", "list:c", "list:a" }, reopened.Document.Sections.Single().Pages.Single().Blocks.Select(block => block.Id));
            Assert.Equal("Changed while menu open", reopened.Document.Sections.Single().Pages.Single().Blocks[0].Text);
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>
    /// Owner bug: moving/duplicating a list row was refused. A projected row ticket used to be
    /// fenced on the whole page view, so any edit anywhere - and the deferred merge that MarkDirty
    /// starts without awaiting for an ordinary typing edit, which refreshes the view a moment
    /// later - invalidated a freshly captured ticket and the answer was StaleOrReadOnly.
    ///
    /// The rule being pinned here: the ticket must survive edits it has nothing to do with, and must
    /// still be refused when the row itself or the shape of its list changed. Asserting the refusal
    /// code as well as the boolean, so a regression identifies the owning gate.
    /// </summary>
    [Fact]
    public async Task Row_tickets_survive_unrelated_edits_and_still_refuse_row_or_list_changes()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-list-tickets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root); var path = Path.Combine(root, "list.9to1board");
            await Seed(store, path, HavenRichBlockKind.Checklist, 1, empty: false);
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var vm = new BoardsViewModel(adapter); if (!vm.IsEditMode) await vm.DispatchAsync("ToggleEditView", null);

                // 1. Typing in another row of the same list, and the deferred merge that such an
                //    edit starts on its own, must not veto a row action asked for afterwards.
                var moveUp = adapter.CaptureBlockAction(vm.SelectedPageId, "list:c"); Assert.NotNull(moveUp);
                vm.EditText("chk_list_a", "Edited while another row is moved");
                await Task.Delay(120);
                var afterUnrelated = await adapter.ApplyBlockActionAsync(moveUp!, BoardBlockAction.MoveUp, () => true);
                Assert.True(afterUnrelated.Applied, "Unrelated row edit vetoed a fresh row move: " + afterUnrelated.Code);
                Assert.Equal("list:c", afterUnrelated.SelectedBlockId);

                // 2. The same edit pattern must still refuse a ticket for the row that was edited.
                var staleDelete = adapter.CaptureBlockAction(vm.SelectedPageId, "list:b"); Assert.NotNull(staleDelete);
                vm.EditText("chk_list_b", "Edited into the ticket's own row");
                var refused = await adapter.ApplyBlockActionAsync(staleDelete!, BoardBlockAction.Delete, () => true);
                Assert.False(refused.Applied);
                Assert.Equal("StaleOrReadOnly", refused.Code);

                // 3. A change to the list's shape still invalidates an older row ticket, even when
                //    that row's own text is untouched: duplicating another row renumbers siblings.
                var fresh = adapter.CaptureBlockAction(vm.SelectedPageId, "list:c"); Assert.NotNull(fresh);
                var older = adapter.CaptureBlockAction(vm.SelectedPageId, "list:b"); Assert.NotNull(older);
                var duplicated = await adapter.ApplyBlockActionAsync(fresh!, BoardBlockAction.Duplicate, () => true);
                Assert.True(duplicated.Applied, "Duplicate refused: " + duplicated.Code);
                var shapeStale = await adapter.ApplyBlockActionAsync(older!, BoardBlockAction.Delete, () => true);
                Assert.False(shapeStale.Applied);
                Assert.Equal("StaleOrReadOnly", shapeStale.Code);

                // 4. What was allowed has to be real and what was refused must not have happened:
                //    row c moved up and was duplicated, row b is still there with its own edit,
                //    and the unrelated edit to row a survived all of it.
                await adapter.SaveAsync();
                var items = (await adapter.GetDetachedContractDocumentAsync()).RichNotes!
                    .Sections.Single().Pages.Single().Blocks.Single(block => block.Id == "list").Items;
                Assert.Equal(4, items.Count);
                Assert.Equal("a", items[0].Id);
                Assert.Equal("b", items[^1].Id);
                Assert.Equal(2, items.Count(row => row.Text == "Last")); // the row plus its duplicate
                Assert.Equal("Edited while another row is moved", items[0].Text);
                Assert.Equal("Edited into the ticket's own row", items[^1].Text);
            }
            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            // The reopened view projects each list row as its own block (the same projection the
            // surface renders), so the saved work is read back row by row.
            var saved = reopened.Document.Sections.Single().Pages.Single().Blocks;
            Assert.Equal(4, saved.Count);
            Assert.Contains(saved, block => block.Text == "Edited into the ticket's own row");
            Assert.Contains(saved, block => block.Text == "Edited while another row is moved");
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task Seed(JsonFileHavenBoardStore store, string path, HavenRichBlockKind kind, int level, bool empty)
    {
        await using var session = await RichBoardSession.CreateNewAsync(store, "Lists");
        await session.MutateAsync(rich =>
        {
            var page = rich.Sections.Single().Pages.Single(); page.Blocks.Clear();
            page.Blocks.Add(new HavenRichBlock { Id = "list", Kind = kind, Items =
            [new() { Id = "a", Text = "First" }, new() { Id = "b", Text = empty ? "" : "Middle", Level = level, Bold = true },
             new() { Id = "c", Text = "Last", Checked = true }] });
        });
        await session.SaveAsAsync(path);
    }
}
