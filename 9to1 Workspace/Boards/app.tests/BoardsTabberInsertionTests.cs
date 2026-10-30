using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsTabberInsertionTests
{
    [Theory]
    [InlineData("retired-origin")]
    [InlineData("cancelled")]
    [InlineData("same-content-after-undo")]
    [InlineData("nested-canonical-admission")]
    public Task Refused_tabber_admission_never_creates_a_partial_owned_page(string outcome) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var page = outcome == "nested-canonical-admission"
                ? adapter.Document.Sections[0].Pages.First(page => page.TabberOwnerBlockId is not null)
                : adapter.Document.Sections[0].Pages[0];
            var ticket = adapter.CaptureInsertionTarget(page.Id, page.Blocks[0].Id, false)!;
            var before = adapter.Snapshot;
            if (outcome == "same-content-after-undo")
            {
                await adapter.RenamePageAsync(page.Id, "Temporary"); Assert.True(await adapter.UndoAsync());
                Assert.Equal(before, adapter.Snapshot);
            }
            using var cancellation = new CancellationTokenSource();
            if (outcome == "cancelled") cancellation.Cancel();
            var action = adapter.InsertRelativeAsync(ticket, new RichBoardBlock { Kind = "tabber" },
                BlockInsertionPlacement.Replace, () => outcome != "retired-origin", cancellation.Token).AsTask();
            if (outcome == "cancelled") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action);
            else Assert.False((await action).Applied);
            await adapter.SaveAsync(); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
        }, SeedTabbers);

    [Theory]
    [InlineData("above")]
    [InlineData("below")]
    [InlineData("replace")]
    public Task Relative_tabber_persists_owned_pane_and_exact_single_undo_redo(string placement) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot;
            OnUi(() => vm.InsertKindRelativeToAsync("tabber", "anchor", placement));
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            var parent = saved.Sections[0].Pages[0];
            var block = Assert.Single(parent.Blocks, block => block.Kind == HavenRichBlockKind.Tabber);
            Assert.Equal(placement == "below" ? 2 : 1, parent.Blocks.IndexOf(block));
            Assert.Equal(placement != "replace", parent.Blocks.Any(block => block.Id == "anchor"));
            var pane = Assert.Single(block.Tabber!.Panes);
            var child = Assert.Single(saved.Sections[0].Pages, page => page.Id == pane.PageId);
            Assert.Equal(parent.Id, child.ParentPageId); Assert.Equal(block.Id, child.TabberOwnerBlockId);
            Assert.Equal(HavenRichPageTypes.Native, child.PageType);
            Assert.Equal(pane.Id, block.Tabber.SelectedPaneId); Assert.Single(child.Blocks);
            var after = adapter.Snapshot;
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            Assert.True(await adapter.RedoAsync()); Assert.Equal(after, adapter.Snapshot);
            var reopened = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            Assert.Equal(child.Id, reopened.Sections[0].Pages.Single(page => page.TabberOwnerBlockId == block.Id).Id);
        });

    [Theory]
    [InlineData("above", false, "one,INSERT,two,three")]
    [InlineData("below", false, "one,two,INSERT,three")]
    [InlineData("replace", false, "one,INSERT,three")]
    [InlineData("above", true, "INSERT,one,two,three")]
    [InlineData("below", true, "one,two,three,INSERT")]
    [InlineData("replace", true, "INSERT")]
    public Task Tabber_list_destination_retains_other_item_identity_and_formatting(string placement, bool whole, string order) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot;
            OnUi(() => vm.InsertKindRelativeToAsync("tabber", whole ? "anchor" : "anchor:two", placement));
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            var blocks = saved.Sections[0].Pages[0].Blocks.Where(block => block.Id is not ("first" or "last")).ToArray();
            var tabber = Assert.Single(blocks, block => block.Kind == HavenRichBlockKind.Tabber);
            Assert.Equal(order.Split(','), blocks.SelectMany(block => block == tabber ? new[] { "INSERT" } : block.Items.Select(item => item.Id)));
            Assert.All(blocks.Where(block => block != tabber), block => Assert.Equal("quote", block.StyleId));
            Assert.All(blocks.SelectMany(block => block.Items), item =>
            { Assert.True(item.Bold); Assert.True(item.Checked); Assert.Equal("Keep " + item.Id, item.Text); });
            Assert.Equal(tabber.Id, saved.Sections[0].Pages.Single(page => page.Id == tabber.Tabber!.Panes[0].PageId).TabberOwnerBlockId);
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
        }, rich =>
        {
            var list = rich.Sections[0].Pages[0].Blocks[1]; list.Kind = HavenRichBlockKind.Checklist; list.StyleId = "quote";
            list.Items = new[] { "one", "two", "three" }.Select(id => new HavenRichListItem
            { Id = id, Text = "Keep " + id, Bold = true, Checked = true }).ToList();
        });

    [Theory]
    [InlineData("above")]
    [InlineData("below")]
    [InlineData("replace")]
    public Task Tabber_positioned_replacement_preserves_geometry_and_refuses_flow_edges(string placement) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot; var revision = adapter.DocumentVersion;
            OnUi(() => vm.InsertKindRelativeToAsync("tabber", "anchor", placement));
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            if (placement != "replace")
            {
                Assert.Equal(before, adapter.Snapshot); Assert.Equal(revision, adapter.DocumentVersion); Assert.False(adapter.CanUndo); return;
            }
            var block = Assert.Single(saved.Sections[0].Pages[0].Blocks, block => block.Kind == HavenRichBlockKind.Tabber);
            Assert.Equal(HavenRichBlockLayouts.Absolute, block.Layout);
            Assert.Equal(45, block.X); Assert.Equal(71, block.Y); Assert.Equal(400, block.Width);
            Assert.Equal(260, block.Height); Assert.Equal(3, block.ZOrder);
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
        }, rich =>
        {
            var page = rich.Sections[0].Pages[0]; page.LayoutMode = HavenRichPageLayoutModes.Unlocked;
            var block = page.Blocks[1]; block.Layout = HavenRichBlockLayouts.Absolute;
            block.X = 45; block.Y = 71; block.Width = 400; block.Height = 260; block.ZOrder = 3;
        });

    [Fact]
    public Task Replacing_tabber_removes_only_its_owned_pages_and_undo_restores_all_nested_content() =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var old = vm.PageBlocks.First(block => block.Kind == "tabber");
            var oldPages = old.Tabber!.Panes.Select(pane => pane.PageId).ToArray();
            var before = adapter.Snapshot;
            OnUi(() => vm.InsertKindRelativeToAsync("tabber", old.Id, "replace"));
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            Assert.DoesNotContain(saved.Sections[0].Pages, page => oldPages.Contains(page.Id));
            Assert.Equal(2, saved.Sections[0].Pages[0].Blocks.Count(block => block.Kind == HavenRichBlockKind.Tabber));
            var after = adapter.Snapshot;
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            Assert.True(await adapter.RedoAsync()); Assert.Equal(after, adapter.Snapshot);
        }, SeedTabbers);

    [Fact]
    public Task Pane_model_cannot_insert_a_nested_tabber_or_redirect_retained_commands_after_remove_and_undo() =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var owner = vm.PageBlocks.First(block => block.Kind == "tabber");
            // Keep nested-insert refusal and the fresh-editor positive path on
            // the visible pane, independently of the hidden-pane toolbar fence.
            var child = TestUiThread.Run(() => vm.CreatePaneModel(owner.Tabber!.Panes.Single(pane => pane.Id == owner.Tabber.SelectedPaneId).PageId));
            try
            {
                var before = adapter.Snapshot;
                OnUi(() => child.InsertKindRelativeToAsync("tabber", child.PageBlocks[0].Id, "replace"));
                Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
                OnUi(() => vm.ReplaceBlockWithKindAsync("paragraph", owner.Id));
                var removed = adapter.Snapshot;
                OnUi(() => child.InsertKindAsync("paragraph", null));
                await adapter.SaveAsync(); Assert.Equal(removed, adapter.Snapshot);
                Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot);
                OnUi(() => child.InsertKindAsync("paragraph", null));
                await adapter.SaveAsync(); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
                var fresh = TestUiThread.Run(() => vm.CreatePaneModel(owner.Tabber!.Panes.Single(pane => pane.Id == owner.Tabber.SelectedPaneId).PageId));
                try
                {
                    OnUi(() => fresh.InsertKindAsync("paragraph", null));
                    await adapter.SaveAsync();
                    Assert.True(adapter.CanUndo);
                }
                finally { TestUiThread.Run(fresh.Dispose); }
            }
            finally { TestUiThread.Run(child.Dispose); }
        }, SeedTabbers);

    internal static void SeedTabbers(HavenRichNotes rich)
    {
        var parent = rich.Sections[0].Pages[0];
        var first = HavenTabberOps.Add(rich, parent.Id, "anchor");
        HavenTabberOps.Change(rich, parent.Id, first.Id, "add", null, null);
        foreach (var pane in first.Tabber!.Panes)
            rich.Sections[0].Pages.Single(page => page.Id == pane.PageId).Blocks = [HavenRichBlock.Paragraph("Keep pane " + pane.Id)];
        HavenTabberOps.Add(rich, parent.Id, null);
    }

    internal static void OnUi(Func<Task> action) => TestUiThread.Run(() => Await(action()));
    internal static void Await(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Assert.True(task.IsCompleted, "Tabber operation did not settle."); task.GetAwaiter().GetResult();
    }
}
