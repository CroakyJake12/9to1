using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using System.Reflection;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsTabberFocusTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task A_name_publication_queued_behind_save_keeps_the_current_metadata_controls_usable(bool reattach) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var owner = vm.PageBlocks.First(block => block.Kind == "tabber");
            Window? window = null;
            var real = (RichBoardSession)typeof(ContractSessionAdapter).GetField("_real", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(adapter)!;
            var gate = (SemaphoreSlim)typeof(RichBoardSession).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(real)!;
            var gateHeld = 0;
            void ReleaseGate() { if (Interlocked.Exchange(ref gateHeld, 0) != 0) gate.Release(); }
            try
            {
                TestUiThread.Run(() =>
                {
                    window = new Window { Width = 1180, Height = 900, Content = vm.Tabbers.Mount(owner) };
                    window.Show();
                    if (reattach)
                    {
                        vm.Detach();
                        var root = new StackPanel(); vm.Attach(root);
                        root.Children.Add(vm.Tabbers.Mount(owner)); window.Content = root;
                    }
                    BoardsTabberLifetimeTests.Settle(window, () => window.GetVisualDescendants().OfType<TextBox>().Any(box =>
                        AutomationProperties.GetName(box) == "Paragraph editor"));
                });
                await gate.WaitAsync(); gateHeld = 1;
                TestUiThread.Run(() =>
                {
                    var name = window!.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Tab name");
                    name.Text = "Saved before UI resumes";
                    name.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
                    var save = Task.Run(async () =>
                    {
                        var queued = adapter.SaveAsync().AsTask();
                        ReleaseGate();
                        await queued;
                    });
                    Assert.True(save.Wait(TimeSpan.FromSeconds(10)), "The canonical save must not wait for a blocked UI dispatcher.");
                });
                TestUiThread.Run(() =>
                {
                    BoardsTabberLifetimeTests.Settle(window!, () => window!.GetVisualDescendants().OfType<Button>().Any(button =>
                        AutomationProperties.GetName(button) == "Open tab Saved before UI resumes"));
                    var count = vm.PageBlocks.Single(block => block.Id == owner.Id).Tabber!.Panes.Count;
                    var add = window!.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Add tab");
                    add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    BoardsTabberLifetimeTests.Settle(window!, () => vm.PageBlocks.Single(block => block.Id == owner.Id).Tabber!.Panes.Count == count + 1);
                });
            }
            finally
            {
                ReleaseGate();
                TestUiThread.Run(() => window?.Close());
                BoardsTabberInsertionTests.OnUi(() => vm.DisposeDatabasesAsync().AsTask());
            }
        }, BoardsTabberInsertionTests.SeedTabbers);

    [Fact]
    public Task Metadata_rename_does_not_baseline_away_later_unmerged_child_text() =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var owner = vm.PageBlocks.First(block => block.Kind == "tabber");
            var pane = owner.Tabber!.Panes[0];
            var child = adapter.Document.Sections[0].Pages.Single(page => page.Id == pane.PageId);
            var originalRow = child.Blocks[0];
            var real = (RichBoardSession)typeof(ContractSessionAdapter).GetField("_real", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(adapter)!;
            var gate = (SemaphoreSlim)typeof(RichBoardSession).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(real)!;
            await gate.WaitAsync();
            Task<bool>? rename = null;
            try
            {
                rename = adapter.TryChangeTabberAsync(vm.SelectedPageId, owner.Id, "rename", pane.Id, "Canonical new name");
                Assert.False(rename.IsCompleted);
                TestUiThread.Run(() => originalRow.Text = "Later editor draft must still save");
            }
            finally { gate.Release(); }
            Assert.True(await rename!);
            Assert.Same(originalRow, adapter.Document.Sections[0].Pages.Single(page => page.Id == pane.PageId).Blocks[0]);
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            var savedChild = saved.Sections[0].Pages.Single(page => page.Id == pane.PageId);
            Assert.Equal("Canonical new name", savedChild.Title);
            Assert.Equal("Later editor draft must still save", savedChild.Blocks[0].PlainText);
        }, BoardsTabberInsertionTests.SeedTabbers);

    [Theory]
    [InlineData("neighbor", false)]
    [InlineData("neighbor", true)]
    [InlineData("toolbar", false)]
    [InlineData("toolbar", true)]
    [InlineData("editor", false)]
    [InlineData("editor", true)]
    [InlineData("neighbor-insert", true)]
    [InlineData("toolbar-insert", true)]
    [InlineData("toolbar-long", true)]
    [InlineData("neighbor-long", true)]
    [InlineData("editor-long", true)]
    [InlineData("editor-cancel-long", true)]
    public async Task Rename_blur_preserves_the_first_neighbor_or_child_gesture(string route, bool held)
    {
        TestUiThread.Run(() => { });
        var target = route.Split('-')[0];
        var longTitle = route.EndsWith("-long", StringComparison.Ordinal);
        var insert = route.EndsWith("-insert", StringComparison.Ordinal);
        var cancelPointer = route.Contains("-cancel-", StringComparison.Ordinal);
        var title = longTitle ? new string('W', 128) : "Renamed without losing the next action";
        var root = Path.Combine(Path.GetTempPath(), "boards-tabber-focus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var documents = new BoardsLocalDocuments("tabber-focus", root);
            var id = await documents.CreateAsync();
            string anchor, ownerId, paneId, childPageId;
            await using (var seed = await documents.OpenAsync(id))
            {
                var page = seed.Document.Sections[0].Pages[0];
                anchor = page.Blocks[0].Id; page.Blocks[0].Text = "Neighbor retains its destination";
                seed.MarkDirty();
                ownerId = await seed.AddTabberAsync(page.Id, anchor);
                if (longTitle)
                    for (var i = 0; i < 3; i++) await seed.ChangeTabberAsync(page.Id, ownerId, "add");
                if (longTitle && target == "neighbor")
                {
                    seed.Document.Sections[0].Pages[0].Blocks.Reverse();
                    seed.MarkDirty(); await seed.SaveAsync();
                }
                var tabs = seed.Document.Sections[0].Pages[0].Blocks.Single(block => block.Id == ownerId).Tabber!;
                var pane = tabs.Panes.Single(item => item.Id == tabs.SelectedPaneId);
                paneId = pane.Id; childPageId = pane.PageId;
                await seed.SaveAsync();
            }
            var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenLocalDocumentAsync(documents, id, (_, _) => Task.CompletedTask));
            BoardsTabberInsertionTests.OnUi(() => opening);
            var presentation = await opening;
            Window? window = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    window = new Window { Width = 1180, Height = 900, Content = presentation.Content };
                    window.Show();
                    BoardsTabberLifetimeTests.Settle(window, () => window.GetVisualDescendants().OfType<TextBox>().Any(box =>
                        AutomationProperties.GetName(box) == "Paragraph editor" && string.IsNullOrEmpty(box.Text)));
                    var vm = presentation.ViewModel;
                    var adapter = Assert.IsType<ContractSessionAdapter>(vm.Session);
                    var before = adapter.Snapshot;
                    var name = window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Tab name");
                    var childRoot = name.GetVisualAncestors().OfType<StackPanel>().First(panel =>
                        panel.GetVisualDescendants().OfType<Button>().Any(button => AutomationProperties.GetName(button) == "Add tab"));
                    Control control = target switch
                    {
                        "neighbor" => window.GetVisualDescendants().OfType<Button>().Single(button => ToolbarBuilder.HasInsertMenu(button) &&
                            button.GetVisualAncestors().OfType<Border>().Any(border => border.Tag is BlockReorderRow row && row.BlockId == anchor)),
                        "toolbar" => childRoot.GetVisualDescendants().OfType<Button>().Single(button => ToolbarBuilder.InsertMenuFor(button) is { AnchorBlockId: null }),
                        _ => childRoot.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Paragraph editor")
                    };
                    control.BringIntoView(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    name.Focus(); name.SelectAll(); window.KeyTextInput(title);
                    var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                    IPointer? pressedPointer = null;
                    control.AddHandler(InputElement.PointerPressedEvent, (_, args) => pressedPointer = args.Pointer,
                        RoutingStrategies.Bubble, handledEventsToo: true);
                    window.MouseMove(point); window.MouseDown(point, MouseButton.Left);
                    if (held) BoardsTabberLifetimeTests.Settle(window, () => vm.PageBlocks.Single(block => block.Id == ownerId)
                        .Tabber!.Panes.Single(pane => pane.Id == paneId).Title == title);
                    if (cancelPointer)
                    {
                        Assert.NotNull(pressedPointer); pressedPointer.Capture(null);
                        BoardsTabberLifetimeTests.Settle(window, () => window.GetVisualDescendants().OfType<Button>().Any(button =>
                            AutomationProperties.GetName(button) == "Open tab " + title));
                    }
                    window.MouseUp(point, MouseButton.Left);
                    BoardsTabberLifetimeTests.Settle(window, () => vm.PageBlocks.Single(block => block.Id == ownerId)
                        .Tabber!.Panes.Single(pane => pane.Id == paneId).Title == title);
                    if (cancelPointer)
                    {
                        Assert.Single(adapter.Document.Sections[0].Pages.Single(page => page.Id == childPageId).Blocks);
                    }
                    else if (target == "editor")
                    {
                        Assert.True(control.IsFocused, "The first click must leave the actual child editor focused.");
                        window.KeyTextInput("First child keystrokes stay in the pane");
                        BoardsTabberLifetimeTests.Settle(window, () => adapter.Document.Sections[0].Pages.Single(page => page.Id == childPageId)
                            .Blocks.Any(block => block.Text == "First child keystrokes stay in the pane"));
                    }
                    else
                    {
                        var menu = Assert.IsType<InsertMenuContext>(ToolbarBuilder.InsertMenuFor(control));
                        Assert.True(menu.Popup.IsOpen, "The first click must leave the real insertion menu open after rename.");
                        Assert.Equal(target == "neighbor" ? anchor : null, menu.AnchorBlockId);
                        Assert.True(menu.Search.IsFocused);
                        var afterRename = adapter.Snapshot;
                        if (insert)
                        {
                            if (target == "neighbor")
                            {
                                Click(window, BoardsCuiControlFinder.FindByAutomationId<Control>(menu.Popup.Child!, "InsertAbove")!);
                                Assert.Equal(BlockInsertionPlacement.Above, vm.InsertionPreview!.Placement);
                            }
                            window.KeyTextInput("Paragraph"); Dispatcher.UIThread.RunJobs();
                            Capture(window, route + "-after-rename");
                            Click(window, menu.Popup.Child!.GetVisualDescendants().OfType<Button>().Single(button =>
                                AutomationProperties.GetName(button) == "Insert Paragraph"));
                            BoardsTabberInsertionTests.Await(menu.WhenActionsIdleAsync());
                            var destination = adapter.Document.Sections[0].Pages.Single(page => page.Id ==
                                (target == "neighbor" ? vm.SelectedPageId : childPageId));
                            Assert.Equal(target == "neighbor" ? 3 : 2, destination.Blocks.Count);
                            if (target == "neighbor") Assert.Equal(anchor, destination.Blocks[1].Id);
                        }
                        else
                        {
                            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                            Dispatcher.UIThread.RunJobs();
                        }
                        Assert.False(menu.Popup.IsOpen);
                        var after = adapter.Snapshot;
                        if (insert)
                        {
                            var undoInsert = adapter.UndoAsync().AsTask(); BoardsTabberInsertionTests.Await(undoInsert);
                            Assert.True(undoInsert.Result); Assert.Equal(afterRename, adapter.Snapshot);
                        }
                        var undo = adapter.UndoAsync().AsTask(); BoardsTabberInsertionTests.Await(undo);
                        Assert.True(undo.Result); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
                        var redo = adapter.RedoAsync().AsTask(); BoardsTabberInsertionTests.Await(redo);
                        Assert.True(redo.Result); Assert.Equal(afterRename, adapter.Snapshot);
                        if (insert)
                        {
                            var redoInsert = adapter.RedoAsync().AsTask(); BoardsTabberInsertionTests.Await(redoInsert);
                            Assert.True(redoInsert.Result);
                        }
                        Assert.Equal(after, adapter.Snapshot);
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
            var tabber = reopened.Document.Sections[0].Pages[0].Blocks.Single(block => block.Id == ownerId);
            var savedPane = tabber.Tabber!.Panes.Single(pane => pane.Id == paneId);
            Assert.Equal(paneId, savedPane.Id); Assert.Equal(childPageId, savedPane.PageId);
            Assert.Equal(title, savedPane.Title);
            var child = reopened.Document.Sections[0].Pages.Single(page => page.Id == childPageId);
            Assert.Equal(ownerId, child.TabberOwnerBlockId);
            Assert.Equal(reopened.Document.Sections[0].Pages[0].Id, child.ParentPageId);
            if (target == "editor" && !cancelPointer) Assert.Equal("First child keystrokes stay in the pane", Assert.Single(child.Blocks).Text);
        }
        finally { Directory.Delete(root, true); }
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
        if (Environment.GetEnvironmentVariable("NINETOONE_TABBER_FOCUS_CAPTURE_DIR") is not { Length: > 0 } path) return;
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Directory.CreateDirectory(path);
        using var bitmap = new RenderTargetBitmap(new PixelSize(1180, 900), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(path, name + ".png"));
    }
}
