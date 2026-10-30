using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsTabberLifetimeTests
{
    [Fact]
    public Task Attachment_admission_merges_a_new_projected_block_before_resolving_its_canonical_target() =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var path = Path.Combine(Path.GetDirectoryName(adapter.FilePath!)!, "pending.txt");
            await File.WriteAllTextAsync(path, "Prepared attachment");
            // The existing toolbar first creates a projected block; its deferred merge need
            // not have reached the canonical model when attachment admission takes the gate.
            TestUiThread.Run(() => adapter.Document.Sections[0].Pages[0].Blocks.Add(new RichBoardBlock
                { Id = "pending-attachment", Kind = "paragraph", Text = "Projected first" }));
            await adapter.AttachFileToBlockAsync(vm.SelectedPageId, "pending-attachment", path);
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            var block = saved.Sections[0].Pages[0].Blocks.Single(block => block.Id == "pending-attachment");
            Assert.Equal("Projected first", block.PlainText); Assert.Equal("pending.txt", block.Attachment!.DisplayName);
        });

    [Fact]
    public Task Removed_mounted_pane_is_recreated_after_undo_and_retained_controls_stay_inert() =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var owner = vm.PageBlocks.First(block => block.Kind == "tabber");
            Window? window = null; TextBox? oldName = null; TextBox? oldText = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    window = new Window { Width = 1000, Height = 700, Content = vm.Tabbers.Mount(owner) };
                    window.Show();
                    Settle(window, () => window.GetVisualDescendants().OfType<TextBox>().Any(box => box.Text?.StartsWith("Keep pane ") == true));
                    oldName = window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Tab name");
                    oldText = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Text?.StartsWith("Keep pane ") == true);
                });
                var before = adapter.Snapshot;
                BoardsTabberInsertionTests.OnUi(() => vm.ReplaceBlockWithKindAsync("paragraph", owner.Id));
                Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot);
                BoardsTabberInsertionTests.OnUi(async () =>
                {
                    oldText!.Text = "late old pane edit";
                    oldName!.Text = "late old tab rename";
                    oldName.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
                    await adapter.SaveAsync();
                });
                Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
                TestUiThread.Run(() =>
                {
                    window!.Content = vm.Tabbers.Mount(vm.PageBlocks.Single(block => block.Id == owner.Id));
                    Settle(window, () => window.GetVisualDescendants().OfType<TextBox>().Any(box => box.Text?.StartsWith("Keep pane ") == true));
                    var fresh = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Text?.StartsWith("Keep pane ") == true);
                    Assert.NotSame(oldText, fresh); fresh.Focus(); fresh.SelectAll();
                    window.KeyTextInput("Fresh restored owner works");
                    Settle(window, () => adapter.Document.Sections[0].Pages.Any(page => page.TabberOwnerBlockId == owner.Id &&
                        page.Blocks.Any(block => block.Text == "Fresh restored owner works")));
                });
                var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
                var pane = saved.Sections[0].Pages.Single(page => page.Id == owner.Tabber!.Panes.Single(pane => pane.Id == owner.Tabber.SelectedPaneId).PageId);
                Assert.Equal("Fresh restored owner works", pane.Blocks[0].PlainText);
            }
            finally
            {
                TestUiThread.Run(() => window?.Close());
                BoardsTabberInsertionTests.OnUi(() => vm.DisposeDatabasesAsync().AsTask());
            }
        }, BoardsTabberInsertionTests.SeedTabbers);

    [Fact]
    public Task Ordinary_pane_typing_refresh_keeps_its_current_tab_controls_live() =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var owner = vm.PageBlocks.First(block => block.Kind == "tabber");
            Window? window = null; TextBox? name = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    window = new Window { Width = 1000, Height = 700, Content = vm.Tabbers.Mount(owner) };
                    window.Show();
                    Settle(window, () => window.GetVisualDescendants().OfType<TextBox>().Any(box => box.Text?.StartsWith("Keep pane ") == true));
                    name = window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Tab name");
                    var text = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Text?.StartsWith("Keep pane ") == true);
                    text.Focus(); text.SelectAll(); window.KeyTextInput("Ordinary child typing");
                    Settle(window, () => adapter.Document.Sections[0].Pages.Any(page => page.Blocks.Any(block => block.Text == "Ordinary child typing")));
                });
                await adapter.SaveAsync();
                BoardsTabberInsertionTests.OnUi(async () =>
                {
                    name!.Text = "Still live"; name.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
                    await adapter.SaveAsync();
                });
                Assert.Contains(vm.PageBlocks.Single(block => block.Id == owner.Id).Tabber!.Panes, pane => pane.Title == "Still live");
            }
            finally
            {
                TestUiThread.Run(() => window?.Close());
                BoardsTabberInsertionTests.OnUi(() => vm.DisposeDatabasesAsync().AsTask());
            }
        }, BoardsTabberInsertionTests.SeedTabbers);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Inactive_parent_immediately_fences_retained_pane_authoring(bool navigate) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var owner = vm.PageBlocks.First(block => block.Kind == "tabber");
            var child = TestUiThread.Run(() => vm.CreatePaneModel(owner.Tabber!.Panes[0].PageId));
            try
            {
                var before = adapter.Snapshot;
                TestUiThread.Run(() =>
                {
                    if (navigate) vm.SelectPageById("other");
                    else BoardsTabberInsertionTests.Await(vm.DispatchAsync("ToggleEditView", null).AsTask());
                    Assert.False(child.CanEdit);
                    child.EditCheck("done_" + child.PageBlocks.First(block => block.Kind == "checklist").Id, true);
                    child.FocusBlock(child.PageBlocks.First(block => block.Kind == "table").Id);
                    Assert.False(child.TrySetValue("ContextHasHeaderRow", true));
                });
                await adapter.SaveAsync(); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            }
            finally { TestUiThread.Run(child.Dispose); }
        }, rich =>
        {
            BoardsTabberInsertionTests.SeedTabbers(rich);
            var child = rich.Sections[0].Pages.First(page => page.TabberOwnerBlockId is not null);
            child.Blocks = [new HavenRichBlock { Kind = HavenRichBlockKind.Checklist, Items = [new HavenRichListItem { Text = "Unchecked" }] },
                new HavenRichBlock { Kind = HavenRichBlockKind.Table, Table = HavenRichTable.Create(1, 1), Order = 1 }];
            rich.Sections[0].Pages.Add(new HavenRichPage { Id = "other", Title = "Other" });
        });

    [Fact]
    public Task Retired_pane_attachment_picker_cannot_write_into_same_ids_restored_by_undo() =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var owner = vm.PageBlocks.First(block => block.Kind == "tabber");
            var child = TestUiThread.Run(() => vm.CreatePaneModel(owner.Tabber!.Panes[0].PageId));
            var chosen = new TaskCompletionSource<string?>();
            var path = Path.Combine(Path.GetDirectoryName(adapter.FilePath!)!, "late.txt");
            await File.WriteAllTextAsync(path, "Late data");
            try
            {
                var before = adapter.Snapshot;
                var pending = TestUiThread.Run(() => { child.PickFileAsync = _ => chosen.Task; return child.AttachFileAsync(child.PageBlocks[0].Id); });
                Assert.False(pending.IsCompleted);
                BoardsTabberInsertionTests.OnUi(() => vm.ReplaceBlockWithKindAsync("paragraph", owner.Id));
                Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot);
                chosen.SetResult(path); BoardsTabberInsertionTests.OnUi(() => pending);
                await adapter.SaveAsync(); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            }
            finally { chosen.TrySetResult(null); TestUiThread.Run(child.Dispose); }
        }, BoardsTabberInsertionTests.SeedTabbers);

    internal static void Settle(Window window, Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); if (ready()) return; Thread.Sleep(1); }
        while (DateTime.UtcNow < deadline);
        Assert.True(ready(), "Tabber surface did not settle.");
    }
}
