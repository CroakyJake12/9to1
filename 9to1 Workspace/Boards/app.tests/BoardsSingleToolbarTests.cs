using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsSingleToolbarTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task One_top_toolbar_formats_the_focused_pane_and_parent_with_exact_history(bool backwards)
    {
        TestUiThread.Run(() => { });
        var root = Path.Combine(Path.GetTempPath(), "boards-single-toolbar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var documents = new BoardsLocalDocuments("single-toolbar", root);
            var id = await documents.CreateAsync();
            string parentId, childId;
            await using (var seed = await documents.OpenAsync(id))
            {
                var parent = seed.Document.Sections[0].Pages[0]; parentId = parent.Id;
                parent.Blocks[0].Text = "Parent paragraph"; seed.MarkDirty();
                var tabberId = await seed.AddTabberAsync(parentId, parent.Blocks[0].Id);
                var tabs = seed.Document.Sections[0].Pages[0].Blocks.Single(block => block.Id == tabberId).Tabber!;
                childId = tabs.Panes[0].PageId;
                seed.Document.Sections[0].Pages.Single(page => page.Id == childId).Blocks[0].Text = "Child paragraph";
                seed.MarkDirty(); await seed.SaveAsync();
            }
            var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenLocalDocumentAsync(documents, id, (_, _) => Task.CompletedTask));
            BoardsTabberInsertionTests.OnUi(() => opening);
            var presentation = await opening;
            Window? window = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    window = new Window { Width = 1180, Height = 900, Content = presentation.Content }; window.Show();
                    var vm = presentation.ViewModel; var adapter = Assert.IsType<ContractSessionAdapter>(vm.Session);
                    TextBox Editor(string text) => window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Text == text);
                    RichBoardBlock Block(string page) => adapter.Document.Sections[0].Pages.Single(item => item.Id == page).Blocks[0];
                    BoardsTabberLifetimeTests.Settle(window, () => window.GetVisualDescendants().OfType<TextBox>().Any(box => box.Text == "Child paragraph"));
                    var child = Editor("Child paragraph"); child.Focus();
                    child.SelectionStart = backwards ? 7 : 2; child.SelectionEnd = backwards ? 2 : 7;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(childId, vm.ToolbarTarget.SelectedPageId);
                    Assert.Equal(parentId, vm.SelectedPageId);
                    Assert.True(vm.Get("CanFormatTextRange") is true, "The selected pane range must enable the top formatting controls.");
                    Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(), combo => AutomationProperties.GetName(combo) == "Font size");
                    var before = adapter.Snapshot;
                    Click(window, BoardsCuiControlFinder.FindByAutomationId<Control>(presentation.Content, "RangeBold")!);
                    BoardsTabberLifetimeTests.Settle(window, () => Block(childId).Runs.Any(run => run.Bold) && child.IsFocused);
                    Assert.Equal(Enumerable.Range(0, "Child paragraph".Length).Select(i => i is >= 2 and < 7),
                        Block(childId).Runs.SelectMany(run => Enumerable.Repeat(run.Bold, run.Text.Length)));
                    Assert.False(Block(parentId).Bold); Assert.DoesNotContain(Block(parentId).Runs, run => run.Bold);
                    Assert.True(child.IsFocused); Assert.Equal(backwards ? 7 : 2, child.SelectionStart); Assert.Equal(backwards ? 2 : 7, child.SelectionEnd);
                    Assert.Equal(true, vm.Get("RangeBoldSelected"));
                    var formatted = adapter.Snapshot;
                    var undo = adapter.UndoAsync().AsTask(); BoardsTabberInsertionTests.Await(undo);
                    Assert.True(undo.Result); Assert.Equal(before, adapter.Snapshot);
                    var redo = adapter.RedoAsync().AsTask(); BoardsTabberInsertionTests.Await(redo);
                    Assert.True(redo.Result); Assert.Equal(formatted, adapter.Snapshot);
                    BoardsTabberLifetimeTests.Settle(window, () => window.GetVisualDescendants().OfType<TextBox>().Any(box => box.Text == "Child paragraph"));
                    Editor("Child paragraph").Focus(); Dispatcher.UIThread.RunJobs();
                    var size = window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationProperties.GetName(combo) == "Font size");
                    size.SelectedItem = "24";
                    BoardsTabberLifetimeTests.Settle(window, () => Block(childId).FontSize == 24);
                    Assert.NotEqual(24, Block(parentId).FontSize);
                    var parent = Editor("Parent paragraph"); parent.Focus(); parent.SelectAll(); Dispatcher.UIThread.RunJobs();
                    Assert.Same(vm, vm.ToolbarTarget);
                    Click(window, BoardsCuiControlFinder.FindByAutomationId<Control>(presentation.Content, "RangeItalic")!);
                    BoardsTabberLifetimeTests.Settle(window, () => Block(parentId).Runs.Any(run => run.Italic));
                    Assert.All(Block(parentId).Runs, run => Assert.True(run.Italic));
                    Assert.DoesNotContain(Block(childId).Runs, run => run.Italic);
                    if (Environment.GetEnvironmentVariable("NINETOONE_SINGLE_TOOLBAR_CAPTURE_DIR") is { Length: > 0 } capturePath)
                    {
                        Directory.CreateDirectory(capturePath); window.UpdateLayout();
                        using var bitmap = new RenderTargetBitmap(new PixelSize(1180, 900), new Vector(96, 96));
                        bitmap.Render(window); bitmap.Save(Path.Combine(capturePath, backwards ? "backward-range.png" : "single-toolbar.png"));
                    }
                });
                BoardsTabberInsertionTests.OnUi(() => presentation.SaveAsync().AsTask());
            }
            finally
            {
                TestUiThread.Run(() => window?.Close());
                BoardsTabberInsertionTests.OnUi(() => presentation.DisposeAsync().AsTask());
            }
            await using var reopened = await documents.OpenAsync(id);
            var childSaved = reopened.Document.Sections[0].Pages.Single(page => page.Id == childId).Blocks[0];
            Assert.Equal(24, childSaved.FontSize); Assert.Contains(childSaved.Runs, run => run.Bold);
            Assert.All(reopened.Document.Sections[0].Pages.Single(page => page.Id == parentId).Blocks[0].Runs, run => Assert.True(run.Italic));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("view")]
    [InlineData("remove")]
    public Task The_previous_pane_cannot_receive_commands_after_its_toolbar_destination_ends(string route) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var owner = vm.PageBlocks.First(block => block.Kind == "tabber");
            var pane = TestUiThread.Run(() => vm.CreatePaneModel(owner.Tabber!.Panes.Single(item => item.Id == owner.Tabber.SelectedPaneId).PageId));
            try
            {
                if (route == "switch") BoardsTabberInsertionTests.OnUi(() => vm.ChangeTabberAsync(owner.Id, "add"));
                else if (route == "remove") BoardsTabberInsertionTests.OnUi(() => vm.ReplaceBlockWithKindAsync("paragraph", owner.Id));
                else BoardsTabberInsertionTests.OnUi(() => vm.DispatchAsync("ToggleEditView", null).AsTask());
                var before = adapter.Snapshot;
                BoardsTabberInsertionTests.OnUi(async () =>
                {
                    await pane.DispatchAsync("ToggleBold", null); pane.SetSelectedFontSize(36);
                    await adapter.SaveAsync();
                });
                Assert.Equal(before, adapter.Snapshot);
            }
            finally
            {
                TestUiThread.Run(pane.Dispose); await pane.DisposeDatabasesAsync();
            }
        }, BoardsTabberInsertionTests.SeedTabbers);

    private static void Click(Window window, Control control)
    {
        control.BringIntoView(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
    }
}
