// Integration tests: the three independent page dimensions required by V3-BOARDS-FEA-03
// ("Locked controls constrain movement/layout as defined; View prevents editing; Snap changes
// manipulation behaviour. Do not collapse all three into one read-only toggle") and by
// V3-BOARDS-ACC-02 ("Verify Edit/View, Locked and Snap independently").
//
// BF-09 in the supplied Boards acceptance recipe set gives the method: "Exercise Edit/View x
// Locked/Unlocked x Snap on/off ... View blocks mutation but permits supported navigation/selection.
// Locked constrains the source-defined layout operations, not all editing or ink. Snap affects
// applicable manipulation. Toggle UI, inspect canonical state, perform the operation, and verify both
// agree; never test a disconnected checkbox alone." Every assertion below therefore compares the
// view-model state with the canonical document behind it. The ink expectations come from the v3.1
// boards-ink rule "Edit + Locked still permits page ink" and rule 5, "Theme changes restyle the
// controls and preserve their stored stroke colours".

using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsModeMatrixTests
{
    [Theory]
    [InlineData(HavenRichPageLayoutModes.Locked, true)]    // locked layout,  snap on
    [InlineData(HavenRichPageLayoutModes.Locked, false)]   // locked layout,  snap off
    [InlineData(HavenRichPageLayoutModes.Unlocked, true)]  // free placement, snap on
    [InlineData(HavenRichPageLayoutModes.Unlocked, false)] // free placement, snap off
    public async Task View_mode_refuses_every_mutation_kind_whatever_the_other_two_dimensions_say(
        string layoutMode, bool snapOn)
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, Path.Combine(root, "view.9to1board"));
            var viewModel = new BoardsViewModel(adapter);
            var pageId = adapter.Document.Sections[0].Pages[0].Id;
            Assert.True(viewModel.SelectPageById(pageId));

            // Set the other two dimensions while still editing, so View mode is genuinely combined with
            // each pair instead of being tested against a default page.
            if (adapter.Document.Sections[0].Pages[0].LayoutMode != layoutMode)
                await viewModel.DispatchAsync("ToggleLayoutMode", null);
            if (viewModel.IsSnapEnabled != snapOn) await viewModel.DispatchAsync("ToggleSnap", null);
            Assert.Equal(layoutMode, adapter.Document.Sections[0].Pages[0].LayoutMode);
            Assert.Equal(snapOn, viewModel.IsSnapEnabled);

            // A real block to place and a real canvas box to move, created before View mode starts.
            await viewModel.DispatchAsync("AddBlock", null);
            var blockId = adapter.Document.Sections[0].Pages[0].Blocks[^1].Id;
            var boxId = await adapter.AddCanvasObjectAsync(pageId, "Text", "Held", 48, 72);
            var blockBefore = adapter.Snapshot;

            await viewModel.DispatchAsync("SetEditModeOff", null);
            Assert.False(viewModel.IsEditMode);
            Assert.True((bool)viewModel.Get("IsViewingMode")!);

            // Structural, text, spatial and drawing mutations are all refused, and none of them may
            // leave a partial change behind.
            await viewModel.DispatchAsync("AddBlock", null);
            Assert.Equal("View mode: switch to Edit to change this board.", (string)viewModel.Get("StatusText")!);

            await viewModel.DispatchAsync("ToggleLayoutMode", null);
            Assert.Equal(layoutMode, adapter.Document.Sections[0].Pages[0].LayoutMode);

            await viewModel.DispatchAsync("ToggleSnap", null);
            Assert.Equal(snapOn, adapter.Document.Sections[0].Pages[0].SnapToGrid);

            await viewModel.DispatchAsync("ToggleCanvasLock", null);
            Assert.False(adapter.Document.Sections[0].Pages[0].IsCanvasLocked);

            await viewModel.CommitInkStrokeAsync(pageId, [(12d, 20d), (40d, 66d)]);
            Assert.Empty(await adapter.GetInkStrokesAsync(pageId));

            await viewModel.AddCanvasBoxAsync();
            Assert.Single(await adapter.GetCanvasObjectsAsync(pageId));

            await viewModel.MoveCanvasBoxAsync(boxId, 300, 300);
            Assert.Equal((48d, 72d), PositionOf(await adapter.GetCanvasObjectsAsync(pageId), boxId));

            await viewModel.PlaceBlockAsync(pageId, blockId, 64, 96, 320, 120, 2);
            var untouched = adapter.Document.Sections[0].Pages[0].Blocks.Single(item => item.Id == blockId);
            Assert.Equal(HavenRichBlockLayouts.Flow, untouched.Layout);

            await adapter.SaveAsync();
            Assert.Equal(blockBefore, adapter.Snapshot);

            // View mode still permits reading, navigation and restyling: the page and its content stay
            // addressable and selection changes no document bytes.
            Assert.True(viewModel.SelectPageById(pageId));
            Assert.True((bool)viewModel.Get("IsViewingMode")!);
            await viewModel.DispatchAsync("ToggleTheme", null);
            Assert.Equal(blockBefore, adapter.Snapshot);
            Assert.True((bool)viewModel.Get("IsViewingMode")!);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Locked_layout_constrains_block_placement_only_and_still_permits_ink()
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "locked.9to1board");
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var viewModel = new BoardsViewModel(adapter);
            var pageId = adapter.Document.Sections[0].Pages[0].Id;
            await viewModel.DispatchAsync("AddBlock", null);
            var blockId = adapter.Document.Sections[0].Pages[0].Blocks[^1].Id;
            Assert.Equal(HavenRichPageLayoutModes.Locked, adapter.Document.Sections[0].Pages[0].LayoutMode);

            // "Locked controls constrain movement/layout as defined": positioning a flow block is the
            // operation the layout lock owns, so the request is refused and the block keeps flow layout.
            await viewModel.PlaceBlockAsync(pageId, blockId, 64, 96, 320, 120, 2);
            Assert.Equal(HavenRichBlockLayouts.Flow,
                adapter.Document.Sections[0].Pages[0].Blocks.Single(b => b.Id == blockId).Layout);
            Assert.False((bool)viewModel.Get("IsFreeLayout")!);

            // The same page in Edit + Locked still takes page ink: locking the layout is not a read-only
            // master switch (boards-ink rule 2), so ink lands while the placement refusal stands.
            await adapter.CommitInkStrokeAsync(pageId, [(8d, 8d, 0.4), (72d, 96d, 0.6)], 2.5, "#FFE6A364", "Pen");
            var ink = Assert.Single(await adapter.GetInkStrokesAsync(pageId));
            Assert.Equal("#FFE6A364", ink.Color);

            // Free placement is the dimension that actually releases the constraint, and then the same
            // placement lands exactly.
            await viewModel.DispatchAsync("ToggleLayoutMode", null);
            Assert.Equal(HavenRichPageLayoutModes.Unlocked, adapter.Document.Sections[0].Pages[0].LayoutMode);
            await viewModel.PlaceBlockAsync(pageId, blockId, 64, 96, 320, 120, 2);
            var placed = adapter.Document.Sections[0].Pages[0].Blocks.Single(b => b.Id == blockId);
            Assert.Equal(HavenRichBlockLayouts.Absolute, placed.Layout);
            Assert.Equal((64d, 96d, 320d, 120d, 2), (placed.X, placed.Y, placed.Width, placed.Height, placed.ZOrder));

            // The ink written under Locked survives the layout change, the save and a fresh open.
            await adapter.SaveAsync();
            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var saved = reopened.Document.Sections[0].Pages[0];
            Assert.Equal(HavenRichPageLayoutModes.Unlocked, saved.LayoutMode);
            Assert.Equal(HavenRichBlockLayouts.Absolute, saved.Blocks.Single(b => b.Id == blockId).Layout);
            Assert.Equal("#FFE6A364", Assert.Single(await reopened.GetInkStrokesAsync(pageId)).Color);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Canvas_lock_and_layout_mode_are_different_dimensions_and_neither_blocks_page_ink()
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, Path.Combine(root, "canvas.9to1board"));
            var viewModel = new BoardsViewModel(adapter);
            var pageId = adapter.Document.Sections[0].Pages[0].Id;
            var boxId = await adapter.AddCanvasObjectAsync(pageId, "Text", "Keep position", 48, 72);

            // Layout locked, canvas unlocked: the canvas box still moves, so the layout lock has not
            // swallowed the canvas dimension.
            Assert.Equal(HavenRichPageLayoutModes.Locked, adapter.Document.Sections[0].Pages[0].LayoutMode);
            await viewModel.MoveCanvasBoxAsync(boxId, 112, 88);
            Assert.Equal((112d, 88d), PositionOf(await adapter.GetCanvasObjectsAsync(pageId), boxId));
            Assert.Equal(HavenRichPageLayoutModes.Locked, adapter.Document.Sections[0].Pages[0].LayoutMode);

            // Canvas locked, layout unchanged: the box is held and the refusal names the reason.
            await viewModel.DispatchAsync("ToggleCanvasLock", null);
            Assert.True(adapter.Document.Sections[0].Pages[0].IsCanvasLocked);
            await viewModel.MoveCanvasBoxAsync(boxId, 300, 300);
            Assert.Equal((112d, 88d), PositionOf(await adapter.GetCanvasObjectsAsync(pageId), boxId));
            Assert.Equal("Canvas layout is locked. Unlock it to move or change boxes.", (string)viewModel.Get("StatusText")!);

            // Ink is page content rather than a canvas-box edit, so it stays available with the canvas
            // locked, and the lock keeps its own label independent of the layout label.
            await adapter.CommitInkStrokeAsync(pageId, [(16d, 24d), (200d, 180d)]);
            Assert.Single(await adapter.GetInkStrokesAsync(pageId));
            Assert.NotEqual((string)viewModel.Get("CanvasLockLabel")!, viewModel.LayoutModeLabel);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Snap_changes_only_where_a_manipulation_commits_and_never_the_other_two_dimensions()
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "snap.9to1board");
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var viewModel = new BoardsViewModel(adapter);
            var pageId = adapter.Document.Sections[0].Pages[0].Id;
            await viewModel.DispatchAsync("ToggleLayoutMode", null); // free placement, where moving is allowed
            var boxId = await adapter.AddCanvasObjectAsync(pageId, "Text", "Snap me", 40, 40);

            var layoutBefore = adapter.Document.Sections[0].Pages[0].LayoutMode;
            var editBefore = viewModel.IsEditMode;

            // Snap off: the requested coordinate is committed exactly as drawn.
            Assert.False(adapter.Document.Sections[0].Pages[0].SnapToGrid);
            await viewModel.MoveCanvasBoxAsync(boxId, 137, 245);
            Assert.Equal((137d, 245d), PositionOf(await adapter.GetCanvasObjectsAsync(pageId), boxId));

            // Snap on: the identical manipulation commits on the shared placement grid instead.
            await viewModel.DispatchAsync("ToggleSnap", null);
            Assert.True(adapter.Document.Sections[0].Pages[0].SnapToGrid);
            Assert.Equal(viewModel.IsSnapEnabled, adapter.Document.Sections[0].Pages[0].SnapToGrid);
            await viewModel.MoveCanvasBoxAsync(boxId, 137, 245);
            var (snappedX, snappedY) = PositionOf(await adapter.GetCanvasObjectsAsync(pageId), boxId);
            Assert.Equal(0, snappedX % HavenRichNotesOps.PlacementGridUnits);
            Assert.Equal(0, snappedY % HavenRichNotesOps.PlacementGridUnits);
            Assert.NotEqual((137d, 245d), (snappedX, snappedY));
            Assert.Equal("Snap on", viewModel.SnapLabel);

            // Neither snap change touched the other two dimensions.
            Assert.Equal(editBefore, viewModel.IsEditMode);
            Assert.Equal(layoutBefore, adapter.Document.Sections[0].Pages[0].LayoutMode);

            // And the other way round: changing the layout mode leaves snap alone.
            await viewModel.DispatchAsync("ToggleLayoutMode", null);
            Assert.True(adapter.Document.Sections[0].Pages[0].SnapToGrid);
            Assert.Equal(HavenRichPageLayoutModes.Locked, adapter.Document.Sections[0].Pages[0].LayoutMode);

            await adapter.SaveAsync();
            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.True(reopened.Document.Sections[0].Pages[0].SnapToGrid);
            Assert.Equal(HavenRichPageLayoutModes.Locked, reopened.Document.Sections[0].Pages[0].LayoutMode);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Authored_ink_colour_survives_a_theme_switch_a_save_and_a_fresh_reopen()
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "ink.9to1board");
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var viewModel = new BoardsViewModel(adapter);
            var pageId = adapter.Document.Sections[0].Pages[0].Id;

            // boards-ink rule 5: "The orange annotations are authored content. Theme changes restyle the
            // controls and preserve their stored stroke colours."
            await viewModel.CommitInkStrokeAsync(pageId, [(16d, 24d, 0.5), (200d, 180d, 0.8)], 3, "#FFE6A364", "Pen");
            var documentBeforeTheme = adapter.Snapshot;
            await viewModel.DispatchAsync("ToggleTheme", null);
            Assert.Equal(documentBeforeTheme, adapter.Snapshot);
            var kept = Assert.Single(await adapter.GetInkStrokesAsync(pageId));
            Assert.Equal("#FFE6A364", kept.Color);
            Assert.Equal(3d, kept.Width);
            Assert.Equal(2, kept.Points.Count);

            await adapter.SaveAsync();
            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var stroke = Assert.Single(await reopened.GetInkStrokesAsync(reopened.Document.Sections[0].Pages[0].Id));
            Assert.Equal("#FFE6A364", stroke.Color);
            Assert.Equal((16d, 24d), (stroke.Points[0].X, stroke.Points[0].Y));
            Assert.Equal((200d, 180d), (stroke.Points[1].X, stroke.Points[1].Y));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static (double X, double Y) PositionOf(IReadOnlyList<CanvasBoxView> boxes, string boxId)
    {
        var box = boxes.Single(item => item.Id == boxId);
        return (box.X, box.Y);
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "boards-mode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
    }
}
