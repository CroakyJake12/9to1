using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsTabberPresentationTests
{
    [Theory]
    [InlineData("Above")]
    [InlineData("Below")]
    [InlineData("Replace")]
    [InlineData("Cancel")]
    public async Task Actual_embedded_inline_destination_creates_editable_persisted_tabber(string placement)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-tabber-surface-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var theme = TestUiThread.Run(() => (BoardsTheme.Mode, CuiSurfacePaletteCatalog.ActiveTheme, Application.Current!.RequestedThemeVariant));
        TestUiThread.Run(() =>
        {
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow; BoardsTheme.SetMode(BoardsThemeMode.Dark);
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        });
        try
        {
            using var documents = new BoardsLocalDocuments("tabber-profile", root);
            var id = await documents.CreateAsync();
            string anchor;
            await using (var seed = await documents.OpenAsync(id))
            {
                var page = seed.Document.Sections[0].Pages[0];
                var original = Assert.Single(page.Blocks); original.Text = "Keep original until committed"; anchor = original.Id;
                page.Blocks.Insert(0, new RichBoardBlock { Id = "first", Text = "First stays first" });
                page.Blocks.Add(new RichBoardBlock { Id = "last", Text = "Last stays last" });
                seed.MarkDirty(); await seed.SaveAsync();
            }
            var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenLocalDocumentAsync(documents, id, (_, _) => Task.CompletedTask));
            BoardsTabberInsertionTests.OnUi(() => opening);
            var presentation = await opening;
            Window? window = null;
            string? blockId = null, paneId = null, pageId = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    window = new Window { Width = 1180, Height = 760, Content = presentation.Content };
                    window.Show();
                    Button? insert = null;
                    BoardsTabberLifetimeTests.Settle(window, () => (insert = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
                        ToolbarBuilder.HasInsertMenu(button) && button.GetVisualAncestors().OfType<Border>().Any(border =>
                            border.Tag is BlockReorderRow row && row.BlockId == anchor))) is not null);
                    var vm = presentation.ViewModel;
                    var adapter = Assert.IsType<ContractSessionAdapter>(vm.Session);
                    var before = adapter.Snapshot;
                    Click(window, insert!);
                    var menu = Assert.IsType<InsertMenuContext>(ToolbarBuilder.InsertMenuFor(insert!));
                    Click(window, BoardsCuiControlFinder.FindByAutomationId<Control>(menu.Popup.Child!, "Insert" + (placement == "Cancel" ? "Replace" : placement))!);
                    Assert.Equal(placement == "Cancel" ? "Replace" : placement, vm.InsertionPreview!.Placement.ToString());
                    window.KeyTextInput("Tabber");
                    BoardsTabberLifetimeTests.Settle(window, () => menu.Search.Text == "Tabber");
                    var item = menu.Popup.Child!.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Insert Tabber");
                    Assert.True(item.IsEffectivelyEnabled);
                    if (placement == "Cancel")
                    {
                        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs();
                        Assert.False(menu.Popup.IsOpen); Assert.Null(vm.InsertionPreview);
                        Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo); return;
                    }
                    if (placement == "Replace") Capture(window, "tabber-replace-choice");
                    Click(window, item); BoardsTabberInsertionTests.Await(menu.WhenActionsIdleAsync());
                    Assert.False(menu.Popup.IsOpen); Assert.Null(vm.InsertionPreview);
                    var tabber = Assert.Single(vm.PageBlocks, block => block.Kind == "tabber");
                    blockId = tabber.Id; paneId = Assert.Single(tabber.Tabber!.Panes).Id; pageId = tabber.Tabber.Panes[0].PageId;
                    Assert.Equal(placement == "Below" ? 2 : 1, vm.PageBlocks.ToList().IndexOf(tabber));
                    TextBox? editor = null;
                    BoardsTabberLifetimeTests.Settle(window, () => (editor = window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box =>
                        AutomationProperties.GetName(box) == "Paragraph editor" && string.IsNullOrEmpty(box.Text))) is not null);
                    editor!.Focus(); window.KeyTextInput("Nested content from the actual pane editor");
                    BoardsTabberLifetimeTests.Settle(window, () => adapter.Document.Sections.SelectMany(section => section.Pages)
                        .Any(page => page.Id == pageId && page.Blocks.Any(block => block.Text == "Nested content from the actual pane editor")));
                    if (placement == "Replace")
                    {
                        Capture(window, "tabber-committed-pane");
                        Click(window, window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Add tab"));
                        BoardsTabberLifetimeTests.Settle(window, () => vm.PageBlocks.Single(block => block.Id == blockId).Tabber!.Panes.Count == 2 &&
                            window.GetVisualDescendants().OfType<TextBox>().Any(box => AutomationProperties.GetName(box) == "Tab name" && box.Text == "Tab 2"));
                        var name = window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Tab name");
                        var beforeGesture = adapter.Snapshot;
                        name.Focus(); name.SelectAll(); window.KeyTextInput("Second pane");
                        Click(window, window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Open tab Tab 1"));
                        BoardsTabberLifetimeTests.Settle(window, () => vm.PageBlocks.Single(block => block.Id == blockId).Tabber is { } tabs &&
                            tabs.SelectedPaneId == paneId && tabs.Panes.Any(pane => pane.Title == "Second pane"));
                        var afterGesture = adapter.Snapshot;
                        var undo = adapter.UndoAsync().AsTask(); BoardsTabberInsertionTests.Await(undo);
                        Assert.True(undo.Result); Assert.Equal(beforeGesture, adapter.Snapshot);
                        var redo = adapter.RedoAsync().AsTask(); BoardsTabberInsertionTests.Await(redo);
                        Assert.True(redo.Result); Assert.Equal(afterGesture, adapter.Snapshot);
                    }
                });
                BoardsTabberInsertionTests.OnUi(() => presentation.ViewModel.Session.SaveAsync().AsTask());
            }
            finally
            {
                TestUiThread.Run(() => window?.Close());
                BoardsTabberInsertionTests.OnUi(() => presentation.DisposeAsync().AsTask());
            }
            await using var reopened = await documents.OpenAsync(id);
            var parent = reopened.Document.Sections[0].Pages[0];
            Assert.Equal("first", parent.Blocks[0].Id); Assert.Equal("last", parent.Blocks[^1].Id);
            if (placement == "Cancel")
            { Assert.DoesNotContain(parent.Blocks, block => block.Kind == "tabber"); Assert.Equal(3, parent.Blocks.Count); }
            else
            {
                var tabber = parent.Blocks.Single(block => block.Id == blockId);
                Assert.Equal(paneId, tabber.Tabber!.SelectedPaneId); Assert.Equal(pageId, tabber.Tabber.Panes[0].PageId);
                var child = reopened.Document.Sections[0].Pages.Single(page => page.Id == pageId);
                Assert.Equal(parent.Id, child.ParentPageId); Assert.Equal(blockId, child.TabberOwnerBlockId);
                Assert.Equal("Nested content from the actual pane editor", Assert.Single(child.Blocks).Text);
            }
        }
        finally
        {
            TestUiThread.Run(() =>
            { CuiSurfacePaletteCatalog.ActiveTheme = theme.ActiveTheme; BoardsTheme.SetMode(theme.Mode); Application.Current!.RequestedThemeVariant = theme.RequestedThemeVariant; });
            Directory.Delete(root, true);
        }
    }

    private static void Click(Window window, Control control)
    {
        control.BringIntoView(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
    }
    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("NINETOONE_TABBER_CAPTURE_DIR") is not { Length: > 0 } path) return;
        window.MouseMove(new Point(1150, 720)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        Directory.CreateDirectory(path);
        using var bitmap = new RenderTargetBitmap(new PixelSize(1180, 760), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(path, name + ".png"));
    }
}
