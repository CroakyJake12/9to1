using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsFreeformInsertionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Positioned_whole_replacement_creates_one_canvas_object_with_exact_history_and_reopen(bool list) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot;
            var canonicalBefore = JsonSerializer.Serialize(await BoardsRelativeInsertionTests.ReadSaved(adapter));
            BoardsTabberInsertionTests.OnUi(() => vm.InsertKindRelativeToAsync("freeform", "anchor", "replace"));
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            var page = saved.Sections[0].Pages[0];
            Assert.Equal(new[] { "first", "last" }, page.Blocks.Select(block => block.Id));
            Assert.Equal(2, page.Canvas.Count);
            var box = Assert.Single(page.Canvas, item => item.Id != "existing-box");
            Assert.Equal("New note", box.Text); Assert.Equal("Text", box.Kind);
            Assert.Equal(45, box.X); Assert.Equal(71, box.Y);
            Assert.Equal(400, box.Width); Assert.Equal(260, box.Height);
            Assert.Equal(3, box.ZOrder);
            Assert.Equal(3, (await vm.GetCanvasObjectsAsync()).Single(item => item.Id == box.Id).ZOrder);
            var after = adapter.Snapshot;
            var canonicalAfter = JsonSerializer.Serialize(saved);
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            Assert.Equal(canonicalBefore, JsonSerializer.Serialize(await BoardsRelativeInsertionTests.ReadSaved(adapter)));
            Assert.True(await adapter.RedoAsync()); Assert.Equal(after, adapter.Snapshot); Assert.False(adapter.CanRedo);
            var reopened = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            Assert.Equal(canonicalAfter, JsonSerializer.Serialize(reopened));
            Assert.Equal(box.Id, reopened.Sections[0].Pages[0].Canvas.Single(item => item.Id != "existing-box").Id);
        }, rich => Seed(rich, list));

    internal static void Seed(HavenRichNotes rich, bool list = false)
    {
        var page = rich.Sections[0].Pages[0]; page.LayoutMode = HavenRichPageLayoutModes.Unlocked;
        var anchor = page.Blocks.Single(block => block.Id == "anchor");
        anchor.Layout = HavenRichBlockLayouts.Absolute;
        anchor.X = 45; anchor.Y = 71; anchor.Width = 400; anchor.Height = 260; anchor.ZOrder = 3;
        if (list)
        {
            anchor.Kind = HavenRichBlockKind.Checklist;
            anchor.Items = [new() { Id = "one", Text = "Keep one", Checked = true }, new() { Id = "two", Text = "Keep two" }];
        }
        var box = HavenRichNotesOps.AddCanvasObject(rich, page.Id, "Text", "Existing box", 600, 450);
        box.Id = "existing-box";
    }

    [Theory]
    [InlineData("flow")]
    [InlineData("locked")]
    [InlineData("negative-x")]
    [InlineData("negative-y")]
    [InlineData("too-narrow")]
    [InlineData("too-tall")]
    [InlineData("above")]
    [InlineData("below")]
    [InlineData("interior-list")]
    public Task Unsupported_frames_refuse_at_menu_and_canonical_boundary_without_history(string reason) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var rowId = reason == "interior-list" ? "anchor:two" : "anchor";
            var position = reason is "above" or "below" ? reason : "replace";
            var placement = Enum.Parse<BlockInsertionPlacement>(position, true);
            var before = JsonSerializer.Serialize(await BoardsRelativeInsertionTests.ReadSaved(adapter));
            var version = adapter.DocumentVersion;
            Assert.NotNull(vm.RelativeInsertionUnavailableReason("freeform", rowId, position));
            BoardsTabberInsertionTests.OnUi(() => vm.InsertKindRelativeToAsync("freeform", rowId, position));
            var ticket = adapter.CaptureInsertionTarget(vm.SelectedPageId, rowId, false)!;
            var result = await adapter.InsertRelativeAsync(ticket, new() { Kind = "freeform" }, placement, () => true);
            Assert.False(result.Applied);
            Assert.Equal(before, JsonSerializer.Serialize(await BoardsRelativeInsertionTests.ReadSaved(adapter)));
            Assert.Equal(version, adapter.DocumentVersion); Assert.False(adapter.CanUndo);
        }, rich =>
        {
            Seed(rich, reason == "interior-list"); var page = rich.Sections[0].Pages[0]; var anchor = page.Blocks[1];
            switch (reason)
            {
                case "flow": anchor.Layout = HavenRichBlockLayouts.Flow; break;
                case "locked": page.IsCanvasLocked = true; break;
                case "negative-x": anchor.X = -1; break;
                case "negative-y": anchor.Y = -1; break;
                case "too-narrow": anchor.Width = 23; break;
                case "too-tall": anchor.Height = 5001; break;
            }
        });

    [Theory]
    [InlineData("cancelled")]
    [InlineData("retired-origin")]
    [InlineData("same-content-after-undo")]
    [InlineData("edited-row")]
    public Task Delayed_command_refuses_stale_or_cancelled_origin_without_creating_objects(string reason) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var ticket = adapter.CaptureInsertionTarget(vm.SelectedPageId, "anchor", false)!;
            if (reason == "same-content-after-undo")
            { await adapter.RenamePageAsync(vm.SelectedPageId, "Temporary"); Assert.True(await adapter.UndoAsync()); }
            if (reason == "edited-row") vm.PageBlocks[1].Text = "Newer user text";
            var before = JsonSerializer.Serialize(await BoardsRelativeInsertionTests.ReadSaved(adapter));
            var undo = adapter.CanUndo; var version = adapter.DocumentVersion;
            using var cancellation = new CancellationTokenSource();
            if (reason == "cancelled") cancellation.Cancel();
            var action = adapter.InsertRelativeAsync(ticket, new() { Kind = "freeform" }, BlockInsertionPlacement.Replace,
                () => reason != "retired-origin", cancellation.Token).AsTask();
            if (reason == "cancelled") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action);
            else Assert.False((await action).Applied);
            Assert.Equal(before, JsonSerializer.Serialize(await BoardsRelativeInsertionTests.ReadSaved(adapter)));
            Assert.Equal(version, adapter.DocumentVersion); Assert.Equal(undo, adapter.CanUndo);
        }, rich => Seed(rich));

    [Fact]
    public Task Replacing_Tabber_removes_owned_pages_atomically_and_retires_child_commands() =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var owner = vm.PageBlocks.Single(block => block.Kind == "tabber");
            var childId = owner.Tabber!.Panes[0].PageId;
            var child = TestUiThread.Run(() => vm.CreatePaneModel(childId));
            try
            {
                var before = JsonSerializer.Serialize(await BoardsRelativeInsertionTests.ReadSaved(adapter));
                BoardsTabberInsertionTests.OnUi(() => vm.InsertKindRelativeToAsync("freeform", owner.Id, "replace"));
                var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
                Assert.DoesNotContain(saved.Sections[0].Pages, page => page.Id == childId);
                Assert.Equal(2, saved.Sections[0].Pages[0].Canvas.Count);
                Assert.False(child.CanEdit);
                BoardsTabberInsertionTests.OnUi(() => child.InsertKindAsync("paragraph"));
                Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(await BoardsRelativeInsertionTests.ReadSaved(adapter)));
                Assert.True(await adapter.UndoAsync()); Assert.False(adapter.CanUndo);
                Assert.Equal(before, JsonSerializer.Serialize(await BoardsRelativeInsertionTests.ReadSaved(adapter)));
                BoardsTabberInsertionTests.OnUi(() => child.InsertKindAsync("freeform"));
                Assert.Equal(before, JsonSerializer.Serialize(await BoardsRelativeInsertionTests.ReadSaved(adapter)));
            }
            finally { TestUiThread.Run(child.Dispose); }
        }, rich =>
        {
            Seed(rich); var page = rich.Sections[0].Pages[0];
            var tabber = HavenTabberOps.Add(rich, page.Id, null);
            tabber.Layout = HavenRichBlockLayouts.Absolute;
            tabber.X = 80; tabber.Y = 140; tabber.Width = 600; tabber.Height = 400; tabber.ZOrder = -2;
        });

    [Fact]
    public void Legacy_canvas_json_without_stack_order_keeps_zero()
    {
        var box = JsonSerializer.Deserialize<HavenRichCanvasObject>("{\"Id\":\"old\",\"Text\":\"Keep\",\"X\":2,\"Y\":3}")!;
        Assert.Equal(0, box.ZOrder); Assert.Equal("Keep", box.Text);
    }
}
