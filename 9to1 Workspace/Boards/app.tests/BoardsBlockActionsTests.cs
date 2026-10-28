using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsBlockActionsTests
{
    [Theory]
    [InlineData("move", false)]
    [InlineData("click", false)]
    [InlineData("escape", false)]
    [InlineData("stale", false)]
    [InlineData("return-to-grip", false)]
    [InlineData("move", true)]
    [InlineData("click", true)]
    [InlineData("escape", true)]
    public async Task Actual_grip_pointer_routes_move_click_cancel_and_revision_refusal(string gesture, bool freePlacement)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-actual-grip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "pointer.9to1board");
            await SeedAsync(store, path, freePlacement);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var vm = new BoardsViewModel(adapter);
            if (!vm.IsEditMode) await vm.DispatchAsync("ToggleEditView", null);
            var original = adapter.Snapshot;
            var fixture = TestUiThread.Run(() =>
            {
                var host = new StackPanel { Width = 600, Margin = new Thickness(76, 0, 0, 0),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
                var absoluteHost = new Canvas();
                var surface = new Grid();
                surface.Children.Add(absoluteHost); surface.Children.Add(host);
                BlockRenderer.Rebuild(host, vm, [], absoluteHost);
                var window = new Window { Width = 760, Height = 600, Content = surface };
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var row = Assert.Single(host.Children.OfType<Border>(), value =>
                    value.Tag is BlockReorderRow { BlockId: "block-a" });
                var grip = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => button.Name == "BlockGripToggle");
                var actions = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => button.Name == "BlockDuplicate");
                var start = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
                var end = host.TranslatePoint(new Point(start.X, host.Bounds.Height + 12), window)!.Value;
                return (Window: window, Host: host, Row: row, Grip: grip, Actions: actions, Start: start, End: end);
            });
            try
            {
                TestUiThread.Run(() =>
                {
                    fixture.Window.MouseMove(fixture.Start);
                    fixture.Window.MouseDown(fixture.Start, MouseButton.Left);
                    Assert.True(fixture.Grip.IsPressed);
                    if (gesture != "click")
                    {
                        fixture.Window.MouseMove(fixture.End);
                        Dispatcher.UIThread.RunJobs();
                        if (freePlacement) Assert.IsType<Avalonia.Media.TranslateTransform>(fixture.Row.RenderTransform);
                        else Assert.Contains(fixture.Host.Children.OfType<Border>(), row => row.BorderThickness.Top == 3);
                        if (gesture == "escape")
                            fixture.Grip.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                        if (gesture == "stale") vm.EditText("t_block-b", "Changed during drag");
                        if (gesture is "return-to-grip" or "escape") fixture.Window.MouseMove(fixture.Start);
                    }
                    fixture.Window.MouseUp(gesture is "click" or "return-to-grip" or "escape" ? fixture.Start : fixture.End, MouseButton.Left);
                    Dispatcher.UIThread.RunJobs();
                });
                if (gesture is "move" or "stale")
                {
                    for (var attempt = 0; attempt < 100; attempt++)
                    {
                        if (TestUiThread.Run(() => gesture == "move" ?
                            (freePlacement ? Blocks(adapter)[0].Layout == HavenRichBlockLayouts.Absolute : Blocks(adapter)[0].Id == "block-b") :
                            vm.TryGetValue("StatusText", out var status) && status?.ToString()?.Contains("Block move refused", StringComparison.Ordinal) == true)) break;
                        await Task.Delay(10);
                    }
                }
                TestUiThread.Run(() =>
                {
                    Assert.False(fixture.Grip.IsPressed);
                    Assert.Equal(gesture == "click", fixture.Actions.IsEffectivelyVisible);
                    Assert.DoesNotContain(fixture.Host.Children.OfType<Border>(), row => row.BorderThickness.Top == 3);
                    Assert.Null(fixture.Row.RenderTransform);
                    Assert.Equal(gesture == "move" && !freePlacement ? new[] { "block-b", "block-c", "block-a" } :
                        new[] { "block-a", "block-b", "block-c" }, Blocks(adapter).Select(block => block.Id));
                    if (freePlacement)
                        Assert.Equal(gesture == "move" ? HavenRichBlockLayouts.Absolute : HavenRichBlockLayouts.Flow,
                            Blocks(adapter)[0].Layout);
                    if (gesture == "stale")
                    {
                        Assert.Equal("Changed during drag", Blocks(adapter)[1].Text);
                        Assert.True(vm.TryGetValue("StatusText", out var status));
                        Assert.Contains("Block move refused", status?.ToString() ?? string.Empty);
                    }
                });
                if (gesture == "move")
                {
                    Assert.True(await adapter.UndoAsync());
                    Assert.Equal(original, adapter.Snapshot);
                    Assert.False(adapter.CanUndo);
                }
                else if (gesture != "stale")
                {
                    Assert.Equal(original, adapter.Snapshot);
                    Assert.False(adapter.CanUndo);
                }
            }
            finally { TestUiThread.Run(() => fixture.Window.Close()); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Grip_refreshes_after_typing_but_an_open_menu_cannot_apply_a_stale_ticket()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-grip-edit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "grip.9to1board");
            await SeedAsync(store, path);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var vm = new BoardsViewModel(adapter);
            var grip = new BlockGripCui.BlockGripContext(vm, "block-b");
            vm.EditText("t_block-b", "Typed before opening");
            await grip.DispatchAsync("ToggleActions", null);
            Assert.True(grip.TryGetValue("DuplicateRequest", out var request));
            await grip.DispatchAsync("ApplyBlockAction", request);
            Assert.Equal(4, Blocks(adapter).Count);
            Assert.Equal(2, Blocks(adapter).Count(block => block.Text == "Typed before opening"));

            var guarded = new BlockGripCui.BlockGripContext(vm, "block-a");
            await guarded.DispatchAsync("ToggleActions", null);
            Assert.True(guarded.TryGetValue("DeleteRequest", out var stale));
            vm.EditText("t_block-a", "Changed with menu open");
            await guarded.DispatchAsync("ApplyBlockAction", stale);
            Assert.Contains(Blocks(adapter), block => block.Id == "block-a");
            Assert.Equal(4, Blocks(adapter).Count);
            await guarded.DispatchAsync("ToggleActions", null);
            Assert.True(guarded.TryGetValue("DeleteRequest", out var fresh));
            await guarded.DispatchAsync("ApplyBlockAction", fresh);
            Assert.DoesNotContain(Blocks(adapter), block => block.Id == "block-a");

            var oldPageGrip = new BlockGripCui.BlockGripContext(vm, "block-b");
            var section = adapter.Document.Sections.Single();
            var otherPage = new RichBoardPage { Id = "other-page",
                Blocks = [new RichBoardBlock { Id = "other-block", Text = "Other page" }] };
            section.Pages.Add(otherPage);
            vm.SelectPage(section.Id, otherPage.Id);
            await oldPageGrip.DispatchAsync("ToggleActions", null);
            Assert.True(oldPageGrip.TryGetValue("CanManage", out var canManage));
            Assert.Equal(false, canManage);
            Assert.True(oldPageGrip.TryGetValue("DeleteRequest", out var refused));
            Assert.Null(refused);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Canonical_commands_move_duplicate_delete_with_same_history_and_save_reopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-block-actions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "owner.9to1board");
            using var store = new JsonFileHavenBoardStore(root); await SeedAsync(store, path);
            string copiedId;
            var documentId = (await store.LoadDocumentAtPathAsync(path))!.DocumentId;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var vm = new BoardsViewModel(adapter); vm.FocusBlock("block-b");
                var original = adapter.Snapshot; Assert.False(adapter.CanUndo);
                Assert.True(Flag(vm, "CanMoveBlockUp")); Assert.True(Flag(vm, "CanMoveBlockDown"));
                await vm.DispatchAsync("BlockMoveUp", null);
                Assert.Equal(new[] { "block-b", "block-a", "block-c" }, Blocks(adapter).Select(block => block.Id));
                Assert.Equal("block-b", vm.SelectedBlockId); Assert.False(Flag(vm, "CanMoveBlockUp"));
                Assert.True(await adapter.UndoAsync()); Assert.Equal(original, adapter.Snapshot); Assert.False(adapter.CanUndo);
                vm.RefreshAfterEdit();
                await vm.DispatchAsync("BlockMoveDown", null);
                Assert.Equal(new[] { "block-a", "block-c", "block-b" }, Blocks(adapter).Select(block => block.Id));
                Assert.False(Flag(vm, "CanMoveBlockDown"));
                Assert.True(await adapter.UndoAsync()); Assert.Equal(original, adapter.Snapshot); Assert.False(adapter.CanUndo);
                vm.RefreshAfterEdit();
                await vm.DispatchAsync("BlockDuplicate", null);
                copiedId = vm.SelectedBlockId;
                Assert.NotEqual("block-b", copiedId);
                Assert.Equal(new[] { "block-a", "block-b", copiedId, "block-c" }, Blocks(adapter).Select(block => block.Id));
                Assert.Equal("AlphaBeta", Blocks(adapter)[2].Text);
                Assert.Equal(new[] { false, false, false, false, false, true, true, true, true },
                    Blocks(adapter)[2].Runs.SelectMany(run => Enumerable.Repeat(run.Bold, run.Text.Length)));
                Assert.True(await adapter.UndoAsync()); Assert.Equal(original, adapter.Snapshot); Assert.False(adapter.CanUndo);
                Assert.True(await adapter.RedoAsync());
                vm.RefreshAfterEdit(); vm.FocusBlock("block-a");
                await vm.DispatchAsync("BlockDelete", null);
                Assert.Equal(new[] { "block-b", copiedId, "block-c" }, Blocks(adapter).Select(block => block.Id));
                Assert.Equal("block-b", vm.SelectedBlockId);
                await vm.DispatchAsync("SaveBoard", null);
            }
            await using (var fresh = await ContractSessionAdapter.OpenAsync(store, path))
            {
                Assert.Equal(new[] { "block-b", copiedId, "block-c" }, Blocks(fresh).Select(block => block.Id));
                Assert.Equal("AlphaBeta", Blocks(fresh)[1].Text);
                Assert.Equal(HavenRichBaseline.Superscript, Blocks(fresh)[1].Runs[1].Baseline);
                Assert.Equal("Georgia", Blocks(fresh)[1].Runs[1].FontFamily);
                Assert.Equal(documentId, (await store.LoadDocumentAtPathAsync(path))!.DocumentId);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Duplication_preserves_full_contract_payload_and_refreshes_only_owned_nested_identities()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-block-payload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "payload.9to1board");
            await using (var owner = await RichBoardSession.CreateNewAsync(store, "Payload owner"))
            {
                await owner.MutateAsync(rich =>
                {
                    var page = rich.Sections.Single().Pages.Single(); page.Blocks.Clear();
                    var block = HavenRichBlock.Paragraph("original payload"); block.Id = "block-payload";
                    block.StyleId = "quote"; block.Alignment = HavenRichAlignment.Right; block.LineSpacing = 1.7;
                    block.SpaceBefore = 12; block.SpaceAfter = 9; block.IndentLevel = 2;
                    block.Runs = [new() { Text = block.PlainText, Bold = true, Italic = true, Underline = true,
                        StrikeThrough = true, Baseline = HavenRichBaseline.Subscript, FontFamily = "Georgia", FontSize = 19,
                        Foreground = "#FF112233", Background = "#FF445566" }];
                    // Optional contract fields are preserved even when the working
                    // projection for this block kind does not expose their UI.
                    block.Items = [new() { Id = "item-original", Text = "keep item", Checked = true, Level = 2, Underline = true }];
                    block.Table = HavenRichTable.Create(1, 1); block.Table.Rows[0].Cells[0].Text = "keep cell";
                    block.Table.Rows[0].Cells[0].Runs = [new() { Text = "keep cell", Bold = true }];
                    block.Table.Background = "#FF112233"; block.Table.ColumnWidths = [140]; block.Table.RowHeights = [42];
                    block.Image = new() { Id = "img-original", DisplayName = "original image", MediaType = "image/png",
                        DataBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII=",
                        LocalReference = "sidecar:abc123", Width = 320, AltText = "preserved alt" };
                    block.Graph = new() { Expressions = [new() { Id = "expr-original", Text = "x*x", Color = "#FF334455" }],
                        Points = [new() { X = 3, Y = 9, Label = "preserved point" }], XLabel = "original x" };
                    block.Attachment = new() { Id = "att-original", DisplayName = "original.bin", MediaType = "application/octet-stream",
                        DataBase64 = Convert.ToBase64String(Enumerable.Repeat((byte)42, 123).ToArray()),
                        LocalReference = "sidecar:def456", SizeBytes = 123 };
                    page.Blocks.Add(block);
                });
                var original = JsonSerializer.Serialize(owner.Rich.Sections.Single().Pages.Single().Blocks.Single());
                var pageId = owner.Rich.Sections.Single().Pages.Single().Id;
                string? copiedId = null;
                await owner.MutateAsync(rich => copiedId = HavenRichNotesOps.DuplicateBlock(rich, pageId, "block-payload").Id);
                var blocks = owner.Rich.Sections.Single().Pages.Single().Blocks;
                Assert.Equal(2, blocks.Count); var source = blocks[0]; var copy = blocks[1];
                Assert.Equal(original, JsonSerializer.Serialize(source)); Assert.NotEqual(source.Id, copiedId);
                Assert.NotEqual(source.Items[0].Id, copy.Items[0].Id);
                Assert.NotEqual(source.Table!.Rows[0].Cells[0].Id, copy.Table!.Rows[0].Cells[0].Id);
                Assert.NotEqual(source.Image!.Id, copy.Image!.Id);
                Assert.NotEqual(source.Graph!.GraphId, copy.Graph!.GraphId);
                Assert.NotEqual(source.Graph!.Expressions[0].Id, copy.Graph!.Expressions[0].Id);
                Assert.NotEqual(source.Attachment!.Id, copy.Attachment!.Id);
                Assert.Equal("quote", copy.StyleId); Assert.Equal("sidecar:abc123", copy.Image.LocalReference);
                Assert.Equal("sidecar:def456", copy.Attachment.LocalReference);
                // A detached comparison restores only identities/order; every
                // other serialized field must be exactly the original payload.
                var comparable = JsonSerializer.Deserialize<HavenRichBlock>(JsonSerializer.Serialize(copy))!;
                comparable.Id = source.Id; comparable.Order = source.Order;
                comparable.Items[0].Id = source.Items[0].Id;
                comparable.Table!.Rows[0].Cells[0].Id = source.Table.Rows[0].Cells[0].Id;
                comparable.Image!.Id = source.Image.Id;
                comparable.Graph!.GraphId = source.Graph.GraphId;
                comparable.Graph!.Expressions[0].Id = source.Graph.Expressions[0].Id;
                comparable.Attachment!.Id = source.Attachment.Id;
                Assert.Equal(original, JsonSerializer.Serialize(comparable));
                copy.Runs[0].Bold = false; copy.Table.Rows[0].Cells[0].Text = "copy only";
                Assert.True(source.Runs[0].Bold); Assert.Equal("keep cell", source.Table.Rows[0].Cells[0].Text);
                await owner.SaveAsAsync(path);
            }
            var saved = (await store.LoadDocumentAtPathAsync(path))!.RichNotes!;
            var reopened = saved.Sections.Single().Pages.Single().Blocks;
            Assert.Equal(2, reopened.Count); Assert.Equal("original payload", reopened[1].PlainText);
            Assert.Equal("original.bin", reopened[1].Attachment!.DisplayName);
            Assert.Equal("sidecar:def456", reopened[1].Attachment!.LocalReference);
            Assert.Equal("preserved point", reopened[1].Graph!.Points[0].Label);
            Assert.Equal(123, Convert.FromBase64String(reopened[1].Attachment!.DataBase64!).Length);
            Assert.All(Convert.FromBase64String(reopened[1].Attachment!.DataBase64!), value => Assert.Equal((byte)42, value));
            Assert.NotEmpty(Convert.FromBase64String(reopened[1].Image!.DataBase64!));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Boundary_denied_stale_and_composite_targets_create_no_action_history_or_disk_change()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-block-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "guard.9to1board"); using var store = new JsonFileHavenBoardStore(root);
            await SeedAsync(store, path); await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var page = adapter.Document.Sections.Single().Pages.Single(); var before = adapter.Snapshot;
            var disk = await File.ReadAllBytesAsync(path);
            var first = adapter.CaptureBlockAction(page.Id, "block-a")!;
            var last = adapter.CaptureBlockAction(page.Id, "block-c")!;
            Assert.Equal("AtBoundary", (await adapter.ApplyBlockActionAsync(first, BoardBlockAction.MoveUp, () => true)).Code);
            Assert.Equal("AtBoundary", (await adapter.ApplyBlockActionAsync(last, BoardBlockAction.MoveDown, () => true)).Code);
            var current = adapter.CaptureBlockAction(page.Id, "block-b")!;
            Assert.False((await adapter.ApplyBlockActionAsync(current, BoardBlockAction.Delete, () => false)).Applied);
            Assert.False((await adapter.ApplyBlockActionAsync(current, (BoardBlockAction)999, () => true)).Applied);
            Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo); Assert.False(adapter.CanRedo);
            Assert.Equal(disk, await File.ReadAllBytesAsync(path));
            Assert.Null(adapter.CaptureBlockAction(page.Id, "block-b:item-pretend"));
            current = adapter.CaptureBlockAction(page.Id, "block-b")!;
            Assert.True((await adapter.ApplyBlockActionAsync(current, BoardBlockAction.Duplicate, () => true)).Applied);
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot);
            var historyAfterUndo = adapter.CanUndo;
            Assert.False((await adapter.ApplyBlockActionAsync(current, BoardBlockAction.Delete, () => true)).Applied);
            Assert.Equal(historyAfterUndo, adapter.CanUndo); // equal-content undo does not revive tickets
            current = adapter.CaptureBlockAction(page.Id, "block-b")!;
            Blocks(adapter)[1].Text = "newer unsaved edit";
            var historyBeforeTextStaleAction = adapter.CanUndo;
            Assert.False((await adapter.ApplyBlockActionAsync(current, BoardBlockAction.Delete, () => true)).Applied);
            Assert.Equal(historyBeforeTextStaleAction, adapter.CanUndo);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Typed_block_move_uses_one_undo_step_and_cancelled_move_is_inert()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-block-pointer-move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "pointer.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await SeedAsync(store, path);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var pageId = adapter.Document.Sections.Single().Pages.Single().Id;
            var original = adapter.Snapshot;

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var cancellationTarget = adapter.CaptureBlockAction(pageId, "block-b")!;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await adapter.ApplyBlockMoveAsync(cancellationTarget, 2, () => true, cancelled.Token));
            Assert.Equal(original, adapter.Snapshot);
            Assert.False(adapter.CanUndo);

            var deniedTarget = adapter.CaptureBlockAction(pageId, "block-b")!;
            var denied = await adapter.ApplyBlockMoveAsync(deniedTarget, 2, () => false);
            Assert.False(denied.Applied);
            Assert.Equal(original, adapter.Snapshot);
            Assert.False(adapter.CanUndo);

            var target = adapter.CaptureBlockAction(pageId, "block-a")!;
            var moved = await adapter.ApplyBlockMoveAsync(target, 2, () => true);
            Assert.True(moved.Applied);
            Assert.Equal(new[] { "block-b", "block-c", "block-a" }, Blocks(adapter).Select(block => block.Id));
            Assert.True(adapter.CanUndo);
            Assert.True(await adapter.UndoAsync());
            Assert.Equal(original, adapter.Snapshot);
            Assert.False(adapter.CanUndo);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static bool Flag(BoardsViewModel model, string name) => model.TryGetValue(name, out var value) && value is true;
    private static List<RichBoardBlock> Blocks(ContractSessionAdapter adapter) => adapter.Document.Sections.Single().Pages.Single().Blocks;
    private static async Task SeedAsync(JsonFileHavenBoardStore store, string path, bool freePlacement = false)
    {
        await using var owner = await RichBoardSession.CreateNewAsync(store, "Same block owner");
        await owner.MutateAsync(rich =>
        {
            var page = rich.Sections.Single().Pages.Single(); page.Blocks.Clear();
            if (freePlacement) page.LayoutMode = HavenRichPageLayoutModes.Unlocked;
            page.Blocks.Add(new() { Id = "block-a", PlainText = "first", Order = 0 });
            page.Blocks.Add(new() { Id = "block-b", PlainText = "AlphaBeta", Order = 1, Runs =
                [new() { Text = "Alpha" }, new() { Text = "Beta", Bold = true, Baseline = HavenRichBaseline.Superscript, FontFamily = "Georgia" }] });
            page.Blocks.Add(new() { Id = "block-c", PlainText = "last", Order = 2 });
        });
        await owner.SaveAsAsync(path);
    }
}
