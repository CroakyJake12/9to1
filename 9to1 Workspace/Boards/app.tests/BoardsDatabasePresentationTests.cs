using System.Text.Json;
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
using Haven.Core;
using HavenOS.Apps.Data.RecordsUI;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsDatabasePresentationTests
{
    [Theory]
    [InlineData("Above", false)]
    [InlineData("Below", false)]
    [InlineData("Replace", false)]
    [InlineData("Cancel", false)]
    [InlineData("Recover", false)]
    [InlineData("Replace", true)]
    public async Task Actual_inline_menu_previews_and_inserts_Data_with_real_record_edit_and_reopen(string placement, bool pane)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-data-surface-" + Guid.NewGuid().ToString("N"));
        using var source = new BoardsDatabaseInsertionTests.Source();
        var theme = TestUiThread.Run(() => (BoardsTheme.Mode, CuiSurfacePaletteCatalog.ActiveTheme, Application.Current!.RequestedThemeVariant));
        TestUiThread.Run(() =>
        {
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow; BoardsTheme.SetMode(BoardsThemeMode.Dark);
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        });
        try
        {
            using var documents = new BoardsLocalDocuments("cycle8-test", root);
            var id = await documents.CreateAsync(); string anchor, pageId;
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
                pageId = page.Id; anchor = page.Blocks[0].Id;
                page.Blocks[0].Text = "Keep original until database is linked";
                page.Blocks.Insert(0, new RichBoardBlock { Id = "first", Text = "First stays first" });
                page.Blocks.Add(new RichBoardBlock { Id = "last", Text = "Last stays last" });
                seed.MarkDirty(); await seed.SaveAsync();
            }
            BoardsViewModel? targetModel = null;
            source.BeforeReturn = async () =>
            {
                var data = source.Created!; var table = data.Snapshot.Tables.Single();
                var field = table.RelationalSchema!.Fields.Single(field => field.Name == "Name").FieldID;
                await data.CreateRecordAsync(table.Id, new Dictionary<Guid, DataScalarRecordValue>
                    { [field] = new(DataCellKind.Text, JsonSerializer.SerializeToElement("Database seed record")) });
                if (placement == "Recover") await Dispatcher.UIThread.InvokeAsync(() => targetModel!.PageBlocks[1].Text = "Newer original content");
            };
            var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenLocalDocumentAsync(documents, id,
                (_, _) => Task.CompletedTask, embeddedArtifactHost: source));
            BoardsTabberInsertionTests.OnUi(() => opening); var presentation = await opening;
            Window? window = null; HavenBoardDatabaseReference? reference = null; string? blockId = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    window = new Window { Width = 1180, Height = 900, Content = presentation.Content }; window.Show();
                    Button? insert = null;
                    BoardsTabberLifetimeTests.Settle(window, () => (insert = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
                        ToolbarBuilder.HasInsertMenu(button) && button.GetVisualAncestors().OfType<Border>().Any(border =>
                            border.Tag is BlockReorderRow row && row.BlockId == anchor))) is not null);
                    window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Text == "Keep original until database is linked").Focus();
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    // Focusing an editor publishes its live projection. Locate the currently
                    // mounted inline trigger after that publication, not the retired control.
                    BoardsTabberLifetimeTests.Settle(window, () => (insert = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
                        ToolbarBuilder.HasInsertMenu(button) && button.GetVisualAncestors().OfType<Border>().Any(border =>
                            border.Tag is BlockReorderRow row && row.BlockId == anchor))) is not null);
                    var target = presentation.ViewModel.ToolbarTarget; Assert.Equal(pageId, target.SelectedPageId);
                    targetModel = target;
                    var adapter = Assert.IsType<ContractSessionAdapter>(target.Session); var before = adapter.Snapshot;
                    Click(window, insert!);
                    var menu = Assert.IsType<InsertMenuContext>(ToolbarBuilder.InsertMenuFor(insert!));
                    var choice = placement is "Cancel" or "Recover" ? "Replace" : placement;
                    Click(window, BoardsCuiControlFinder.FindByAutomationId<Control>(menu.Popup.Child!, "Insert" + choice)!);
                    Assert.Equal(choice, target.InsertionPreview!.Placement.ToString());
                    window.KeyTextInput("New database");
                    BoardsTabberLifetimeTests.Settle(window, () => menu.Search.Text == "New database");
                    var item = menu.Popup.Child!.GetVisualDescendants().OfType<Button>()
                        .Single(button => AutomationProperties.GetName(button) == "Insert New database");
                    Assert.True(item.IsEffectivelyEnabled);
                    if (placement == "Cancel")
                    {
                        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs();
                        Assert.False(menu.Popup.IsOpen); Assert.Null(target.InsertionPreview);
                        Assert.Equal(0, source.Creates); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo); return;
                    }
                    if (placement == "Replace" && !pane) Capture(window, "database-replace-preview");
                    Click(window, item); BoardsTabberInsertionTests.Await(menu.WhenActionsIdleAsync());
                    Assert.False(menu.Popup.IsOpen); Assert.Null(target.InsertionPreview); Assert.Equal(1, source.Creates);
                    if (placement == "Recover")
                    {
                        Assert.True(target.HasPendingDatabaseLink); Assert.Equal("Newer original content", target.PageBlocks[1].Text);
                        BoardsTabberInsertionTests.Await(adapter.SaveAsync().AsTask()); before = adapter.Snapshot;
                        Button? retryInsert = null;
                        BoardsTabberLifetimeTests.Settle(window, () => (retryInsert = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
                            ToolbarBuilder.HasInsertMenu(button) && button.GetVisualAncestors().OfType<Border>().Any(border =>
                                border.Tag is BlockReorderRow row && row.BlockId == "last"))) is not null);
                        Click(window, retryInsert!);
                        var retryMenu = Assert.IsType<InsertMenuContext>(ToolbarBuilder.InsertMenuFor(retryInsert!));
                        Click(window, BoardsCuiControlFinder.FindByAutomationId<Control>(retryMenu.Popup.Child!, "InsertAbove")!);
                        window.KeyTextInput("Link created database");
                        BoardsTabberLifetimeTests.Settle(window, () => retryMenu.Search.Text == "Link created database");
                        var retry = retryMenu.Popup.Child!.GetVisualDescendants().OfType<Button>()
                            .Single(button => AutomationProperties.GetName(button) == "Insert Link created database");
                        Assert.True(retry.IsEffectivelyEnabled); Click(window, retry);
                        BoardsTabberInsertionTests.Await(retryMenu.WhenActionsIdleAsync());
                        Assert.False(target.HasPendingDatabaseLink); Assert.Equal(1, source.Creates);
                        Assert.Equal("Newer original content", target.PageBlocks[1].Text);
                    }
                    var block = Assert.Single(target.PageBlocks, block => block.Kind == "database");
                    blockId = block.Id; reference = block.Database;
                    Assert.Equal(placement is "Below" or "Recover" ? 2 : 1, target.PageBlocks.ToList().IndexOf(block));
                    DataRecordViewControl? records = null;
                    BoardsTabberLifetimeTests.Settle(window, () => (records = window.GetVisualDescendants().OfType<DataRecordViewControl>().FirstOrDefault()) is not null &&
                        records!.GetVisualDescendants().OfType<TextBox>().Any(box => box.Text == "Database seed record"));
                    var recordView = Assert.IsType<DataRecordViewControl>(records);
                    var field = recordView.GetVisualDescendants().OfType<TextBox>().Single(box => box.Text == "Database seed record");
                    field.Focus(); field.SelectAll(); window.KeyTextInput("Edited through actual embedded Data");
                    Assert.True(recordView.HasUnsavedDrafts);
                    Click(window, recordView.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Save record")));
                    BoardsTabberInsertionTests.Await(recordView.WhenIdleAsync()); Assert.False(recordView.HasUnsavedDrafts);
                    if (placement == "Replace") Capture(window, pane ? "database-in-pane" : "database-linked-record");
                    var after = adapter.Snapshot;
                    var undo = adapter.UndoAsync().AsTask(); BoardsTabberInsertionTests.Await(undo);
                    Assert.True(undo.Result); Assert.Equal(before, adapter.Snapshot);
                    if (placement != "Recover") Assert.False(adapter.CanUndo);
                    var redo = adapter.RedoAsync().AsTask(); BoardsTabberInsertionTests.Await(redo);
                    Assert.True(redo.Result); Assert.Equal(after, adapter.Snapshot);
                });
                BoardsTabberInsertionTests.OnUi(() => presentation.SaveAsync().AsTask());
            }
            finally
            {
                TestUiThread.Run(() =>
                {
                    if (window is not null)
                        foreach (var view in window.GetVisualDescendants().OfType<DataRecordViewControl>()) view.DiscardDrafts();
                    window?.Close();
                });
                BoardsTabberInsertionTests.OnUi(() => presentation.DisposeAsync().AsTask());
            }
            await using var reopened = await documents.OpenAsync(id);
            var pageSaved = reopened.Document.Sections[0].Pages.Single(page => page.Id == pageId);
            Assert.Equal("first", pageSaved.Blocks[0].Id); Assert.Equal("last", pageSaved.Blocks[^1].Id);
            if (placement == "Cancel") Assert.DoesNotContain(pageSaved.Blocks, block => block.Database is not null);
            else
            {
                Assert.Equal(reference, pageSaved.Blocks.Single(block => block.Id == blockId).Database);
                await using var data = await source.OpenDatabaseAsync(reference!.WorkbookID);
                Assert.Equal(reference.StoreID, data.Reference.StoreID);
                // Records map stable row identities; values live in the canonical sheet cells.
                var name = data.Snapshot.Tables.Single().RelationalSchema!.Fields.Single(field => field.Name == "Name").FieldID;
                Assert.Equal("Edited through actual embedded Data", Assert.Single(data.Project(reference.ViewID).Rows).Cells[name]!.Value);
                Assert.DoesNotContain("Edited through actual embedded Data", JsonSerializer.Serialize(reopened.Document));
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
        if (Environment.GetEnvironmentVariable("NINETOONE_DATABASE_CAPTURE_DIR") is not { Length: > 0 } path) return;
        window.MouseMove(new Point(1150, 870)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Directory.CreateDirectory(path);
        using var bitmap = new RenderTargetBitmap(new PixelSize(1180, 900), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(path, name + ".png"));
    }
}
