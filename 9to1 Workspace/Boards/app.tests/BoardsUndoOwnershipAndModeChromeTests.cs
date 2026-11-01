using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

/// <summary>
/// Regression coverage for the 1 November 2026 native review of the running standalone Boards app.
/// Each case answers an observation made on screen with the real X11 build:
///   * the block text controls kept a private character-level undo stack beside the canonical board
///     history, so Ctrl+Z removed typed characters while the run marks stayed;
///   * the empty-page "+ Add your first block" affordance wrote the literal words "New block" into the
///     document and bypassed the canonical insertion path, giving the block a non-canonical id;
///   * Edit/View and the layout mode existed only inside the "…" overflow, so the persistent page-mode
///     switch the specification requires was not visible at all;
///   * the labelled AppFlowy stage-one comparison surface was offered in the owner-facing file menu.
/// </summary>
[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsUndoOwnershipAndModeChromeTests
{
    [Fact]
    public void Document_text_controls_expose_no_private_undo_stack()
    {
        var fixture = OpenMountedBlocks();
        try
        {
            // Read the control facts on the UI thread, then assert off it: Avalonia objects are
            // thread-affine and the test thread does not own them.
            var found = TestUiThread.Run(() => fixture.Window.GetVisualDescendants().OfType<TextBox>()
                .Where(candidate => candidate.Name is { Length: > 0 } name &&
                                    (name.StartsWith("t_", StringComparison.Ordinal) ||
                                     name.StartsWith("chk_", StringComparison.Ordinal) ||
                                     name.StartsWith("cell_", StringComparison.Ordinal)))
                .Select(candidate => (candidate.Name!, UndoLimit: candidate.UndoLimit))
                .ToList());
            Assert.NotEmpty(found);
            Assert.All(found, entry => Assert.Equal(0, entry.UndoLimit));
        }
        finally { CloseMountedBlocks(fixture); }
    }

    [Fact]
    public async Task Formatting_is_undone_as_formatting_and_the_typed_text_survives()
    {
        var fixture = OpenMountedBlocks();
        try
        {
            var textBefore = fixture.Adapter.Document.Sections[0].Pages[0].Blocks[0].Text;
            TestUiThread.Run(() =>
            {
                var input = fixture.Window.GetVisualDescendants().OfType<TextBox>().Single();
                input.Focus();
                input.SelectionStart = 0;
                input.SelectionEnd = 2;
                fixture.ViewModel.DispatchAsync("ToggleBold", null).AsTask().GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();
            });
            Assert.Contains(fixture.Adapter.Document.Sections[0].Pages[0].Blocks[0].Runs, run => run.Bold);

            Assert.True(await fixture.Adapter.UndoAsync());
            var restored = fixture.Adapter.Document.Sections[0].Pages[0].Blocks[0];
            Assert.Equal(textBefore, restored.Text);
            Assert.DoesNotContain(restored.Runs, run => run.Bold);
        }
        finally { CloseMountedBlocks(fixture); }
    }

    [Fact]
    public async Task Empty_page_add_block_inserts_one_empty_canonical_paragraph_and_undoes_in_one_step()
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, Path.Combine(root, "board.9to1board"));
            using var vm = new BoardsViewModel(adapter);
            // Every count is read back out of the session, because Undo/Redo rebuild the document
            // object graph: a page reference captured before the change belongs to the old snapshot.
            static RichBoardPage PageOf(ContractSessionAdapter session) => session.Document.Sections[0].Pages[0];
            var before = PageOf(adapter).Blocks.Count;
            var beforeSnapshot = adapter.Snapshot;

            await vm.DispatchAsync("AddBlock", null);

            var after = PageOf(adapter).Blocks;
            Assert.Equal(before + 1, after.Count);
            var inserted = after[^1];
            Assert.Equal("paragraph", inserted.Kind);
            Assert.Equal(string.Empty, inserted.Text);
            Assert.StartsWith("block-", inserted.Id, StringComparison.Ordinal);
            Assert.DoesNotContain(after, block => block.Text.Contains("New block", StringComparison.Ordinal) ||
                                                  block.Text.Contains("New paragraph", StringComparison.Ordinal));
            Assert.Equal(inserted.Id, vm.SelectedBlockId);

            await vm.DispatchAsync("Undo", null);
            Assert.Equal(before, PageOf(adapter).Blocks.Count);
            Assert.Equal(beforeSnapshot, adapter.Snapshot);
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task Page_mode_switch_is_persistent_chrome_and_the_experimental_surface_is_not_offered()
    {
        var root = NewDirectory();
        BoardsViewModel? viewModel = null;
        Window? window = null;
        Control? content = null;
        CuiControlLoader? loader = null;
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, Path.Combine(root, "chrome.9to1board"));
            TestUiThread.Run(() =>
            {
                viewModel = new BoardsViewModel(adapter);
                loader = BoardsCuiDocuments.CreateLoader();
                loader.SetBindingContext(viewModel);
                loader.SetActionDispatcher(viewModel);
                var loaded = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.MainSourceName));
                Assert.DoesNotContain(loaded.Diagnostics, item => item.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
                content = Assert.IsAssignableFrom<Control>(loaded.Root);
                loader.WireBindings(content);
                viewModel.Attach(content);
                window = new Window { Content = content, Width = 1200, Height = 800 };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            });
            var ui = content!;

            var observations = TestUiThread.Run(() =>
            {
                var topLevel = window!;
                var edit = BoardsCuiControlFinder.FindByAutomationId<Button>(ui, "EditModeButton");
                var view = BoardsCuiControlFinder.FindByAutomationId<Button>(ui, "ViewModeButton");
                var layout = BoardsCuiControlFinder.FindByAutomationId<Button>(ui, "LayoutModeButton");
                var before = adapter.Snapshot;
                var startedEditing = (bool?)viewModel!.Get("IsEditingMode");
                var startedViewing = (bool?)viewModel.Get("IsViewingMode");
                // Selecting the mode that is already active must not flip the page.
                viewModel.DispatchAsync("SetEditModeOn", null).AsTask().GetAwaiter().GetResult();
                var stillEditing = (bool?)viewModel.Get("IsEditingMode");
                // View mode blocks mutation while navigation stays available.
                viewModel.DispatchAsync("SetEditModeOff", null).AsTask().GetAwaiter().GetResult();
                var viewingNow = (bool?)viewModel.Get("IsViewingMode");
                var editingNow = (bool?)viewModel.Get("IsEditingMode");
                viewModel.DispatchAsync("AddBlock", null).AsTask().GetAwaiter().GetResult();
                viewModel.DispatchAsync("ToggleBold", null).AsTask().GetAwaiter().GetResult();
                var snapshotWhileViewing = adapter.Snapshot;
                var viewModeMessage = (string?)viewModel.Get("StatusText");
                // Back to Edit, where the same insertion is accepted.
                viewModel.DispatchAsync("SetEditModeOn", null).AsTask().GetAwaiter().GetResult();
                var editingAgain = (bool?)viewModel.Get("IsEditingMode");
                viewModel.DispatchAsync("AddBlock", null).AsTask().GetAwaiter().GetResult();
                var snapshotAfterEditing = adapter.Snapshot;
                return new
                {
                    EditFound = edit is not null,
                    ViewFound = view is not null,
                    LayoutFound = layout is not null,
                    EditContent = edit?.Content?.ToString(),
                    ViewContent = view?.Content?.ToString(),
                    LayoutContent = layout?.Content?.ToString(),
                    Enabled = edit?.IsEffectivelyEnabled == true && view?.IsEffectivelyEnabled == true &&
                              layout?.IsEffectivelyEnabled == true,
                    // A control that exists in the compiled document can still measure to nothing on
                    // the mounted surface, which is exactly what the 1 November native review caught.
                    Visible = edit?.IsEffectivelyVisible == true && view?.IsEffectivelyVisible == true &&
                              layout?.IsEffectivelyVisible == true,
                    Measured = edit?.Bounds.Width > 40 && view?.Bounds.Width > 40 && layout?.Bounds.Width > 40,
                    Heights = edit?.Bounds.Height > 16 && view?.Bounds.Height > 16 && layout?.Bounds.Height > 16,
                    EditTop = edit?.Bounds.Top ?? -1,
                    // The overflow is authored as a menu item, so its runtime control is a MenuItem.
                    // Bounds are parent-relative, so the left-of-overflow order is compared in window
                    // coordinates rather than between two different parents.
                    OverflowTop = BoardsCuiControlFinder.FindByAutomationId<MenuItem>(ui, "OverflowButton")?.Bounds.Top ?? -1,
                    EditWindowX = edit?.TranslatePoint(new Avalonia.Point(0, 0), topLevel)?.X ?? -1,
                    OverflowWindowX = BoardsCuiControlFinder.FindByAutomationId<MenuItem>(ui, "OverflowButton")
                        ?.TranslatePoint(new Avalonia.Point(0, 0), topLevel)?.X ?? -1,
                    EditWidth = edit?.Bounds.Width ?? -1,
                    ViewWidth = view?.Bounds.Width ?? -1,
                    LayoutWidth = layout?.Bounds.Width ?? -1,
                    SnapFound = BoardsCuiControlFinder.FindByAutomationId<Button>(ui, "SnapButton") is not null,
                    SnapVisible = BoardsCuiControlFinder.FindByAutomationId<Button>(ui, "SnapButton")?.IsEffectivelyVisible == true,
                    SnapWidth = BoardsCuiControlFinder.FindByAutomationId<Button>(ui, "SnapButton")?.Bounds.Width ?? -1,
                    StartedEditing = startedEditing,
                    StartedViewing = startedViewing,
                    StillEditing = stillEditing,
                    ViewingNow = viewingNow,
                    EditingNow = editingNow,
                    ViewModeMessage = viewModeMessage,
                    UnchangedWhileViewing = string.Equals(before, snapshotWhileViewing, StringComparison.Ordinal),
                    EditingAgain = editingAgain,
                    MutatedAfterEditing = !string.Equals(snapshotWhileViewing, snapshotAfterEditing, StringComparison.Ordinal),
                    // The stage-one AppFlowy comparison surface stays a per-process developer seam
                    // (BOARDS_STAGE_ONE_PREVIEW=1), not an owner-facing entry in the board's file menu.
                    StageOneOffered = BoardsCuiControlFinder.FindByAutomationId<MenuItem>(ui, "FileStageOneSurface") is not null,
                    RenameOffered = BoardsCuiControlFinder.FindByAutomationId<MenuItem>(ui, "RenameBoardMenuItem") is not null,
                    SaveOffered = BoardsCuiControlFinder.FindByAutomationId<MenuItem>(ui, "BoardSwitcherSave") is not null,
                };
            });

            Assert.True(observations.EditFound, "the mounted page chrome must expose an Edit control");
            Assert.True(observations.ViewFound, "the mounted page chrome must expose a View control");
            Assert.True(observations.LayoutFound, "the mounted page chrome must expose the layout-mode control");
            Assert.Equal("Edit", observations.EditContent);
            Assert.Equal("View", observations.ViewContent);
            Assert.False(string.IsNullOrWhiteSpace(observations.LayoutContent));
            Assert.True(observations.Enabled);
            Assert.True(observations.Visible, "the mode controls are not effectively visible on the mounted surface");
            Assert.True(observations.Measured,
                $"the mode controls measured too small: Edit={observations.EditWidth} View={observations.ViewWidth} Layout={observations.LayoutWidth}");
            Assert.True(observations.Heights);
            Assert.True(observations.SnapFound, "the mounted chrome must expose the Snap control");
            Assert.True(observations.SnapVisible, "the Snap control is not effectively visible");
            Assert.True(observations.SnapWidth > 40, $"the Snap control measured {observations.SnapWidth} wide");
            Assert.True(observations.EditTop >= 0 && observations.EditTop < 120,
                $"the mode switch sits at y={observations.EditTop}, outside the identity band");
            Assert.True(observations.OverflowTop >= 0 && observations.OverflowTop < 120,
                $"the overflow sits at y={observations.OverflowTop}, outside the identity band");
            Assert.True(observations.EditWindowX >= 0 && observations.OverflowWindowX > observations.EditWindowX,
                $"the mode switch (x={observations.EditWindowX}) must sit to the left of the overflow (x={observations.OverflowWindowX})");

            Assert.Equal(true, observations.StartedEditing);
            Assert.Equal(false, observations.StartedViewing);
            Assert.Equal(true, observations.StillEditing);
            Assert.Equal(true, observations.ViewingNow);
            Assert.Equal(false, observations.EditingNow);
            Assert.True(observations.UnchangedWhileViewing, "View mode must not mutate the board");
            Assert.Contains("View mode", observations.ViewModeMessage ?? string.Empty);
            Assert.Equal(true, observations.EditingAgain);
            Assert.True(observations.MutatedAfterEditing);

            Assert.False(observations.StageOneOffered);
            Assert.True(observations.RenameOffered);
            Assert.True(observations.SaveOffered);
        }
        finally
        {
            TestUiThread.Run(() =>
            {
                window?.Close();
                loader?.Dispose();
                viewModel?.Dispose();
            });
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Clicking_the_blank_part_of_a_paragraph_reaches_the_editor_and_typing_writes_to_the_document()
    {
        // Measured natively on 1 November 2026: in a fresh board the owner could not start typing.
        // A click anywhere in the blank area of a paragraph block never reached the text editor,
        // because the block row's indicator overlay covered the row and took the pointer.
        var fixture = OpenMountedBlocks(string.Empty);
        try
        {
            var blockId = fixture.Adapter.Document.Sections[0].Pages[0].Blocks[0].Id;
            var observed = TestUiThread.Run(() =>
            {
                var window = fixture.Window;
                var row = window.GetVisualDescendants().OfType<Border>().Single(border =>
                    border.Tag is BlockReorderRow info && info.BlockId == blockId);
                Assert.True(row.Bounds.Width > 120, $"the block row measured only {row.Bounds.Width} wide");
                var point = row.TranslatePoint(new Point(row.Bounds.Width - 24, row.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, Avalonia.Input.MouseButton.Left);
                window.MouseUp(point, Avalonia.Input.MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                var editor = window.GetVisualDescendants().OfType<TextBox>().Single();
                var focused = editor.IsFocused;
                if (focused)
                {
                    window.KeyTextInput("hello");
                    Dispatcher.UIThread.RunJobs();
                }
                return (focused, Text: fixture.Adapter.Document.Sections[0].Pages[0].Blocks[0].Text);
            });
            Assert.True(observed.focused, "a click in the blank part of the paragraph did not reach its text editor");
            Assert.Equal("hello", observed.Text);
        }
        finally { CloseMountedBlocks(fixture); }
    }

    [Fact]
    public async Task Snap_is_a_persisted_page_state_that_View_mode_cannot_change()
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "snap.9to1board");
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            using var vm = new BoardsViewModel(adapter);
            var pageId = adapter.Document.Sections[0].Pages[0].Id;
            Assert.False(adapter.Document.Sections[0].Pages[0].SnapToGrid);

            // View mode must not be able to mutate the stored page state.
            await vm.DispatchAsync("SetEditModeOff", null);
            await vm.DispatchAsync("ToggleSnap", null);
            Assert.False(adapter.Document.Sections[0].Pages[0].SnapToGrid);
            await vm.DispatchAsync("SetEditModeOn", null);

            var layoutLabelBefore = vm.Get("LayoutModeLabel");
            await vm.DispatchAsync("ToggleSnap", null);
            Assert.True(adapter.Document.Sections[0].Pages[0].SnapToGrid);
            Assert.Equal("Snap on", vm.Get("SnapLabel"));
            // Independent of the other two dimensions: neither the layout mode nor Edit/View moved.
            Assert.Equal(layoutLabelBefore, vm.Get("LayoutModeLabel"));
            Assert.Equal(true, vm.Get("IsEditingMode"));
            await adapter.SaveAsync();

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.True(reopened.Document.Sections[0].Pages[0].SnapToGrid);
        }
        finally { DeleteDirectory(root); }
    }

    private static (string Root, ContractSessionAdapter Adapter, BoardsViewModel ViewModel, Window Window)
        OpenMountedBlocks(string seedText = "KeepThisText")
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "blocks.9to1board");
            var adapter = ContractSessionAdapter.OpenAsync(store, path).GetAwaiter().GetResult();
            var block = adapter.Document.Sections[0].Pages[0].Blocks[0];
            block.Text = seedText;
            block.Runs = [];
            adapter.MarkDirty();
            adapter.SaveAsync().GetAwaiter().GetResult();

            return TestUiThread.Run(() =>
            {
                var viewModel = new BoardsViewModel(adapter);
                var host = new StackPanel();
                viewModel.Attach(host);
                BlockRenderer.Rebuild(host, viewModel, []);
                var window = new Window { Content = host, Width = 800, Height = 600 };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                return (root, adapter, viewModel, window);
            });
        }
        catch
        {
            DeleteDirectory(root);
            throw;
        }
    }

    private static void CloseMountedBlocks((string Root, ContractSessionAdapter Adapter, BoardsViewModel ViewModel,
        Window Window) fixture)
    {
        TestUiThread.Run(() =>
        {
            fixture.Window.Close();
            fixture.ViewModel.Dispose();
        });
        fixture.Adapter.DisposeAsync().GetAwaiter().GetResult();
        DeleteDirectory(fixture.Root);
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "boards-undo-chrome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
    }
}
