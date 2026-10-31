using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsNestedContinuityTests
{
    private const string ChildText = "Nested selection stays here";

    [Theory]
    [InlineData(3, 11)]
    [InlineData(11, 3)]
    [InlineData(7, 7)]
    public Task Parent_rebuild_retains_child_selection_and_one_canonical_edit(int start, int end) =>
        Run(async (adapter, vm, window, shell, host, search) =>
        {
            var before = adapter.Snapshot;
            TestUiThread.Run(() =>
            {
                var input = ChildEditor(host);
                input.Focus(); input.CaretIndex = end; input.SelectionStart = start; input.SelectionEnd = end;
                var caret = input.CaretIndex;
                BlockRenderer.Rebuild(host, vm, []);
                BoardsTabberLifetimeTests.Settle(window, () => ChildEditor(host).IsFocused);
                Dispatcher.UIThread.RunJobs();
                var restored = ChildEditor(host);
                Assert.True(restored.IsFocused);
                Assert.Equal(caret, restored.CaretIndex);
                Assert.Equal(start, restored.SelectionStart);
                Assert.Equal(end, restored.SelectionEnd);
                window.KeyTextInput("!");
            });
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            var expected = ChildText[..Math.Min(start, end)] + "!" + ChildText[Math.Max(start, end)..];
            Assert.Equal(expected, ActiveChild(saved, vm).Blocks[0].PlainText);
            Assert.True(await adapter.UndoAsync());
            Assert.Equal(before, adapter.Snapshot);
            Assert.False(adapter.CanUndo);
            Assert.True(await adapter.RedoAsync());
            saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            Assert.Equal(expected, ActiveChild(saved, vm).Blocks[0].PlainText);
        });

    [Fact]
    public Task Detached_child_render_restores_selection_only_after_its_parent_remounts() =>
        Run((adapter, vm, window, shell, host, search) =>
        {
            TestUiThread.Run(() =>
            {
                var input = ChildEditor(host);
                input.Focus(); input.CaretIndex = 4; input.SelectionStart = 12; input.SelectionEnd = 4;
                // Capture is the renderer's pre-detach hook. Rendering while the
                // parent is detached forces the child completion to precede remount.
                vm.CaptureNestedEditorFocus();
                shell.Children.Remove(host);
                BlockRenderer.Rebuild(host, vm, []);
                Dispatcher.UIThread.RunJobs();
                Assert.False(ChildEditor(host).IsFocused);
                shell.Children.Add(host);
                BoardsTabberLifetimeTests.Settle(window, () => ChildEditor(host).IsFocused);
                var restored = ChildEditor(host);
                Assert.Equal(12, restored.SelectionStart); Assert.Equal(4, restored.SelectionEnd);
                Assert.Equal(4, restored.CaretIndex);
            });
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData("search")]
    [InlineData("search-then-blur")]
    [InlineData("page-round-trip")]
    [InlineData("section-round-trip")]
    [InlineData("tab-round-trip")]
    [InlineData("detach-reattach")]
    [InlineData("edit-mode-round-trip")]
    public Task Delayed_child_remount_does_not_override_newer_user_intent(string route) =>
        Run((adapter, vm, window, shell, host, search) =>
        {
            TestUiThread.Run(() =>
            {
                var input = ChildEditor(host);
                input.Focus(); input.CaretIndex = 6;
                vm.CaptureNestedEditorFocus();
                shell.Children.Remove(host);
                BlockRenderer.Rebuild(host, vm, []);
                Dispatcher.UIThread.RunJobs();
                if (route.StartsWith("search", StringComparison.Ordinal))
                {
                    search.Focus();
                    if (route == "search-then-blur") shell.Children.Remove(search);
                }
                else if (route == "page-round-trip")
                {
                    var page = vm.SelectedPageId;
                    Assert.True(vm.SelectPageById("other")); Assert.True(vm.SelectPageById(page));
                }
                else if (route == "section-round-trip")
                {
                    var section = vm.SelectedSectionId;
                    vm.SelectSection("other-section"); vm.SelectSection(section);
                }
                else if (route == "tab-round-trip")
                {
                    var block = vm.PageBlocks.Single(block => block.Kind == "tabber");
                    var selected = block.Tabber!.SelectedPaneId;
                    var other = block.Tabber.Panes.First(pane => pane.Id != selected).Id;
                    BoardsTabberInsertionTests.Await(adapter.TryChangeTabberAsync(vm.SelectedPageId, block.Id, "select", other));
                    BoardsTabberInsertionTests.Await(adapter.TryChangeTabberAsync(vm.SelectedPageId, block.Id, "select", selected));
                }
                else if (route == "detach-reattach")
                {
                    vm.Detach(); vm.Attach(shell);
                }
                else
                {
                    BoardsTabberInsertionTests.Await(vm.DispatchAsync("ToggleEditView", null).AsTask());
                    BoardsTabberInsertionTests.Await(vm.DispatchAsync("ToggleEditView", null).AsTask());
                }
                shell.Children.Add(host);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.False(ChildEditor(host).IsFocused);
                if (route == "search") Assert.True(search.IsFocused);
            });
            return Task.CompletedTask;
        });

    [Fact]
    public Task Disposed_pane_drops_its_pending_restore_and_late_edits() =>
        Run(async (adapter, vm, window, shell, host, search) =>
        {
            var before = adapter.Snapshot;
            TextBox? retained = null;
            TestUiThread.Run(() =>
            {
                retained = ChildEditor(host); retained.Focus(); retained.CaretIndex = 6;
                vm.CaptureNestedEditorFocus(); shell.Children.Remove(host);
                BoardsTabberInsertionTests.Await(vm.Tabbers.DisposeAsync().AsTask());
                shell.Children.Add(host); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.False(retained.IsFocused);
                retained.Text = "Late disposed edit";
            });
            await adapter.SaveAsync();
            Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
        });

    private static TextBox ChildEditor(Control root) => root.GetVisualDescendants().OfType<TextBox>()
        .Single(input => input.Text == ChildText);

    private static HavenRichPage ActiveChild(HavenRichNotes saved, BoardsViewModel vm)
    {
        var tabs = vm.PageBlocks.Single(block => block.Kind == "tabber").Tabber!;
        var pageId = tabs.Panes.Single(pane => pane.Id == tabs.SelectedPaneId).PageId;
        return saved.Sections[0].Pages.Single(page => page.Id == pageId);
    }

    private static Task Run(Func<ContractSessionAdapter, BoardsViewModel, Window, StackPanel, StackPanel, TextBox, Task> check) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            Window? window = null; StackPanel? shell = null; StackPanel? host = null; TextBox? search = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    shell = new StackPanel(); host = new StackPanel(); search = new TextBox { Text = "Search" };
                    shell.Children.Add(search); shell.Children.Add(host); vm.Attach(shell);
                    window = new Window { Width = 1100, Height = 900, Content = shell };
                    window.Show(); BlockRenderer.Rebuild(host, vm, []);
                    BoardsTabberLifetimeTests.Settle(window, () => host.GetVisualDescendants().OfType<TextBox>()
                        .Any(input => input.Text == ChildText));
                    Dispatcher.UIThread.RunJobs();
                });
                await check(adapter, vm, window!, shell!, host!, search!);
            }
            finally
            {
                TestUiThread.Run(() => window?.Close());
                BoardsTabberInsertionTests.OnUi(() => vm.DisposeDatabasesAsync().AsTask());
            }
        }, rich =>
        {
            var parent = rich.Sections[0].Pages[0];
            var tabs = HavenTabberOps.Add(rich, parent.Id, "anchor");
            HavenTabberOps.Change(rich, parent.Id, tabs.Id, "add", null, null);
            foreach (var pane in tabs.Tabber!.Panes)
                rich.Sections[0].Pages.Single(page => page.Id == pane.PageId).Blocks =
                    [HavenRichBlock.Paragraph(ChildText)];
            rich.Sections[0].Pages.Add(new HavenRichPage { Id = "other", Title = "Other page" });
            rich.Sections.Add(new HavenRichSection { Id = "other-section", Title = "Other section",
                Pages = [new HavenRichPage { Id = "other-section-page", Title = "Other section page" }] });
        });
}
