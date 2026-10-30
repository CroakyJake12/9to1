using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsFreeformPresentationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Real_menu_replaces_at_preview_frame_or_cancels_then_moves_and_reopens(bool pane, bool cancel)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-freeform-surface-" + Guid.NewGuid().ToString("N"));
        var theme = TestUiThread.Run(() => (BoardsTheme.Mode, CuiSurfacePaletteCatalog.ActiveTheme, Application.Current!.RequestedThemeVariant));
        TestUiThread.Run(() =>
        {
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow; BoardsTheme.SetMode(BoardsThemeMode.Dark);
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        });
        try
        {
            using var documents = new BoardsLocalDocuments("freeform-surface", root);
            var id = await documents.CreateAsync(); string pageId; string anchor;
            await using (var seed = await documents.OpenAsync(id))
            {
                var page = seed.Document.Sections[0].Pages[0];
                if (pane)
                {
                    var tabberId = await seed.AddTabberAsync(page.Id, page.Blocks[0].Id);
                    page = seed.Document.Sections[0].Pages[0];
                    var childId = page.Blocks.Single(block => block.Id == tabberId).Tabber!.Panes[0].PageId;
                    page = seed.Document.Sections[0].Pages.Single(page => page.Id == childId);
                }
                pageId = page.Id; var block = page.Blocks[0]; anchor = block.Id;
                block.Text = "Positioned paragraph to replace"; page.LayoutMode = HavenRichPageLayoutModes.Unlocked;
                block.Layout = HavenRichBlockLayouts.Absolute; block.X = 200; block.Y = 100;
                block.Width = 320; block.Height = 150; block.ZOrder = 3;
                seed.MarkDirty(); await seed.SaveAsync();
            }
            var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenLocalDocumentAsync(documents, id, (_, _) => Task.CompletedTask));
            BoardsTabberInsertionTests.OnUi(() => opening); var presentation = await opening;
            Window? window = null; string? boxId = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    window = new Window { Width = 1180, Height = 900, Content = presentation.Content }; window.Show();
                    TextBox? editor = null;
                    BoardsTabberLifetimeTests.Settle(window, () => (editor = window.GetVisualDescendants().OfType<TextBox>()
                        .FirstOrDefault(box => box.Text == "Positioned paragraph to replace")) is not null);
                    editor!.Focus(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    Button? insert = null;
                    BoardsTabberLifetimeTests.Settle(window, () => (insert = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
                        ToolbarBuilder.HasInsertMenu(button) && button.GetVisualAncestors().OfType<Border>().Any(border =>
                            border.Tag is BlockReorderRow row && row.BlockId == anchor))) is not null);
                    var vm = presentation.ViewModel.ToolbarTarget; Assert.Equal(pageId, vm.SelectedPageId);
                    var adapter = Assert.IsType<ContractSessionAdapter>(vm.Session); var before = adapter.Snapshot;
                    Click(window, insert!);
                    var menu = Assert.IsType<InsertMenuContext>(ToolbarBuilder.InsertMenuFor(insert!));
                    var replace = BoardsCuiControlFinder.FindByAutomationId<Control>(menu.Popup.Child!, "InsertReplace")!;
                    Click(window, replace);
                    Assert.Equal(BlockInsertionPlacement.Replace, vm.InsertionPreview!.Placement);
                    window.KeyTextInput("Freeform box");
                    BoardsTabberLifetimeTests.Settle(window, () => menu.Search.Text == "Freeform box");
                    var item = menu.Popup.Child!.GetVisualDescendants().OfType<Button>().Single(button =>
                        AutomationProperties.GetName(button) == "Insert Freeform box");
                    Assert.True(item.IsEffectivelyEnabled);
                    var rowControl = insert!.GetVisualAncestors().OfType<Border>().First(border => border.Tag is BlockReorderRow);
                    var outline = BoardsCuiControlFinder.FindByAutomationId<Control>(rowControl, "InsertionReplaceOutline")!;
                    Assert.True(outline.IsEffectivelyVisible);
                    var preview = new Rect(outline.TranslatePoint(default, window)!.Value, outline.Bounds.Size);
                    Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
                    if (!pane && !cancel) Capture(window, "freeform-replace-preview");
                    if (cancel)
                    {
                        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs();
                        Assert.False(menu.Popup.IsOpen); Assert.Null(vm.InsertionPreview);
                        Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo); return;
                    }
                    Click(window, item); BoardsTabberInsertionTests.Await(menu.WhenActionsIdleAsync());
                    Assert.False(menu.Popup.IsOpen); Assert.Null(vm.InsertionPreview);
                    var objects = vm.GetCanvasObjectsAsync(); BoardsTabberInsertionTests.Await(objects);
                    var box = Assert.Single(objects.Result); boxId = box.Id;
                    Border? shown = null;
                    BoardsTabberLifetimeTests.Settle(window, () => (shown = window.GetVisualDescendants().OfType<Border>()
                        .FirstOrDefault(border => border.Name == "canvas_" + box.Id)) is not null);
                    var rendered = Assert.IsType<Border>(shown);
                    Capture(window, pane ? "freeform-in-pane" : "freeform-replaced");
                    Assert.Equal(3, rendered.ZIndex);
                    var actual = new Rect(rendered.TranslatePoint(default, window)!.Value, rendered.Bounds.Size);
                    Assert.Equal(preview, actual);
                    rendered.Focus(); window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                    window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                    BoardsTabberLifetimeTests.Settle(window, () => vm.GetCanvasObjectsAsync().GetAwaiter().GetResult().Single().X == 208);
                    var undoMove = adapter.UndoAsync().AsTask(); BoardsTabberInsertionTests.Await(undoMove); Assert.True(undoMove.Result);
                    Assert.Equal(200, vm.GetCanvasObjectsAsync().GetAwaiter().GetResult().Single().X);
                    var undoReplace = adapter.UndoAsync().AsTask(); BoardsTabberInsertionTests.Await(undoReplace); Assert.True(undoReplace.Result);
                    Assert.Equal(before, adapter.Snapshot); Assert.Empty(vm.GetCanvasObjectsAsync().GetAwaiter().GetResult()); Assert.False(adapter.CanUndo);
                    for (var i = 0; i < 2; i++) { var redo = adapter.RedoAsync().AsTask(); BoardsTabberInsertionTests.Await(redo); Assert.True(redo.Result); }
                    Assert.False(adapter.CanRedo);
                });
                BoardsTabberInsertionTests.OnUi(() => presentation.SaveAsync().AsTask());
            }
            finally
            {
                TestUiThread.Run(() => window?.Close());
                BoardsTabberInsertionTests.OnUi(() => presentation.DisposeAsync().AsTask());
            }
            await using var reopened = await documents.OpenAsync(id);
            var persisted = await reopened.GetCanvasObjectsAsync(pageId);
            if (cancel) { Assert.Empty(persisted); Assert.Contains(reopened.Document.Sections[0].Pages.Single(page => page.Id == pageId).Blocks, block => block.Id == anchor); }
            else
            {
                var box = Assert.Single(persisted); Assert.Equal(boxId, box.Id); Assert.Equal(208, box.X); Assert.Equal(100, box.Y);
                Assert.Equal(320, box.Width); Assert.Equal(150, box.Height); Assert.Equal(3, box.ZOrder);
                Assert.DoesNotContain(reopened.Document.Sections[0].Pages.Single(page => page.Id == pageId).Blocks, block => block.Id == anchor);
            }
        }
        finally
        {
            TestUiThread.Run(() => { CuiSurfacePaletteCatalog.ActiveTheme = theme.ActiveTheme; BoardsTheme.SetMode(theme.Mode); Application.Current!.RequestedThemeVariant = theme.RequestedThemeVariant; });
            if (Directory.Exists(root)) Directory.Delete(root, true);
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
        if (Environment.GetEnvironmentVariable("NINETOONE_FREEFORM_CAPTURE_DIR") is not { Length: > 0 } path) return;
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Directory.CreateDirectory(path);
        using var bitmap = new RenderTargetBitmap(new PixelSize(1180, 900), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(path, name + ".png"));
    }
}
