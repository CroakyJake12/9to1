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
