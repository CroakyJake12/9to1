// UX27-BOARD-016/017/018 — the owner's Enter rule on the rich Boards page:
// Enter ends the current BlockID and starts the next one (a new block of the same
// kind, placed immediately after the original); Shift+Enter keeps the text inside
// the same BlockID. These tests drive the owning view-model operations that the
// rendered editors call, then save and reopen the real .9to1board file.
//
// BLOCK MODEL: the contract stores a list as one block holding Items[], while the
// editor projects each item to its own "{list block}:{item id}" view row. Seeding
// therefore uses Items, and a divider needs its Divider payload to project at all.
//
// Rendered key-path checks use TestUiThread; contract and view-model checks use
// isolated stores without constructing controls.

using CakeOS.Apps.Boards.Contract;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsEnterBehaviourTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rendered_code_enter_stays_inside_its_block(bool shift)
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/code-enter");
        var vm = new BoardsViewModel(session);
        var page = session.Document.Sections[0].Pages[0];
        page.Blocks = [new RichBoardBlock { Id = "code-block", StyleId = "code", Text = "alphabeta" }];
        TestUiThread.Run(() =>
        {
            var host = new StackPanel();
            BlockRenderer.Rebuild(host, vm, []);
            var window = new Window { Content = host, Width = 800, Height = 600 };
            window.Show();
            try
            {
                var input = Assert.Single(host.GetLogicalDescendants().OfType<TextBox>());
                input.Focus();
                input.CaretIndex = 5;
                input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter,
                    KeyModifiers = shift ? KeyModifiers.Shift : KeyModifiers.None });
                Assert.Single(page.Blocks);
                // Same requirement as before, with the control state reported so a failure
                // identifies whether the break was lost in the input or in the document write.
                var expected = "alpha" + input.NewLine + "beta";
                Assert.True(page.Blocks[0].Text == expected,
                    "document text was " + System.Text.Json.JsonSerializer.Serialize(page.Blocks[0].Text)
                    + " while the control held " + System.Text.Json.JsonSerializer.Serialize(input.Text));
                Assert.Equal("code", page.Blocks[0].StyleId);
                Assert.Equal("code-block", page.Blocks[0].Id);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Rendered_code_control_enter_exits_to_an_empty_paragraph_without_changing_code()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/code-exit");
        var vm = new BoardsViewModel(session);
        var page = session.Document.Sections[0].Pages[0];
        page.Blocks = [new RichBoardBlock { Id = "code-block", StyleId = "code", Text = "original code" }];
        TestUiThread.Run(() =>
        {
            var host = new StackPanel();
            BlockRenderer.Rebuild(host, vm, []);
            var window = new Window { Content = host, Width = 800, Height = 600 };
            window.Show();
            try
            {
                var input = Assert.Single(host.GetLogicalDescendants().OfType<TextBox>());
                input.Focus();
                input.SelectionStart = 0;
                input.SelectionEnd = input.Text!.Length;
                input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter,
                    KeyModifiers = KeyModifiers.Control });
                Assert.Equal(2, page.Blocks.Count);
                Assert.Equal("original code", page.Blocks[0].Text);
                Assert.Equal("code", page.Blocks[0].StyleId);
                Assert.Equal(string.Empty, page.Blocks[1].Text);
                Assert.Equal("paragraph", page.Blocks[1].Kind);
                Assert.Equal("normal", page.Blocks[1].StyleId);
                BlockRenderer.Rebuild(host, vm, []);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var next = Assert.Single(host.GetLogicalDescendants().OfType<TextBox>(), box => box.Name == "t_" + page.Blocks[1].Id);
                Assert.True(next.IsFocused);
                Assert.Equal(0, next.CaretIndex);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Rendered_editor_enter_replaces_selection_and_focuses_the_new_paragraph()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/enter-ui");
        var vm = new BoardsViewModel(session);
        var page = session.Document.Sections[0].Pages[0];
        page.Blocks = [new RichBoardBlock { Id = "heading", Kind = "heading", Text = "alpha REMOVE omega" }];
        TestUiThread.Run(() =>
        {
            var host = new StackPanel();
            BlockRenderer.Rebuild(host, vm, []);
            var window = new Window { Content = host, Width = 800, Height = 600 };
            window.Show();
            var input = Assert.Single(host.GetLogicalDescendants().OfType<TextBox>());
            input.Focus();
            input.SelectionStart = 6;
            input.SelectionEnd = 13;
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(2, page.Blocks.Count);
            Assert.Equal("alpha ", page.Blocks[0].Text);
            Assert.Equal("omega", page.Blocks[1].Text);
            Assert.Equal("paragraph", page.Blocks[1].Kind);
            BlockRenderer.Rebuild(host, vm, []);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var following = Assert.Single(host.GetLogicalDescendants().OfType<TextBox>(), box => box.Name == "t_" + page.Blocks[1].Id);
            Assert.True(following.IsFocused);
            Assert.Equal(0, following.CaretIndex);
            window.Close();
        });
    }

    [Fact]
    public async Task Rendered_heading_accepts_shift_enter_without_allocating_a_sibling()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/heading-soft-break");
        var vm = new BoardsViewModel(session);
        var page = session.Document.Sections[0].Pages[0];
        page.Blocks = [new RichBoardBlock { Id = "heading", Kind = "heading", Text = "alpha" }];
        TestUiThread.Run(() =>
        {
            var host = new StackPanel();
            BlockRenderer.Rebuild(host, vm, []);
            var input = Assert.Single(host.GetLogicalDescendants().OfType<TextBox>());
            Assert.True(input.AcceptsReturn);
            input.CaretIndex = 5;
            var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Shift };
            input.RaiseEvent(key);
            Assert.Single(page.Blocks);
        });
    }

    [Fact]
    public async Task Selected_range_is_deleted_and_split_as_one_undoable_edit_with_marks_preserved()
    {
        var root = TempRoot("boards-enter-range");
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "range.9to1board");
            await SeedAsync(store, path);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var vm = new BoardsViewModel(adapter);
            Blocks(adapter)[0].Runs = [new HavenRichTextRun { Text = Blocks(adapter)[0].Text, Italic = true }];
            adapter.MarkDirty();
            await adapter.SaveAsync();
            var original = adapter.Snapshot;
            var undoBefore = adapter.CanUndo;
            Assert.True(await vm.SplitTextBlockRangeAsync("p-sum", 10, 6));
            Assert.Equal("x^2 + ", Blocks(adapter)[0].Text);
            Assert.Equal("3x", Blocks(adapter)[1].Text);
            Assert.True(Assert.Single(Blocks(adapter)[0].Runs).Italic);
            Assert.True(Assert.Single(Blocks(adapter)[1].Runs).Italic);
            Assert.True(await adapter.UndoAsync());
            Assert.Equal(original, adapter.Snapshot);
            Assert.Equal(undoBefore, adapter.CanUndo);
            Assert.True(await adapter.RedoAsync());
            Assert.Equal(4, Blocks(adapter).Count);
            await adapter.SaveAsync();
            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.Equal("3x", Blocks(reopened)[1].Text);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Heading_enter_continues_as_paragraph_and_never_cuts_a_grapheme()
    {
        var rich = HavenRichNotes.Create();
        var page = rich.Sections.Single().Pages.Single();
        var heading = HavenRichBlock.Heading("A😀e\u0301Z");
        heading.Runs = [new HavenRichTextRun { Text = heading.PlainText, Italic = true }];
        page.Blocks = [heading];
        var body = HavenRichNotesOps.SplitTextBlockRange(rich, page.Id, heading.Id, 2, 4);
        Assert.Equal("A", heading.PlainText);
        Assert.Equal("Z", body.PlainText);
        Assert.Equal(HavenRichBlockKind.Paragraph, body.Kind);
        Assert.Equal("normal", body.StyleId);
        Assert.True(Assert.Single(heading.Runs).Italic);
        Assert.True(Assert.Single(body.Runs).Italic);
    }

    [Fact]
    public async Task Enter_splits_a_paragraph_at_the_caret_into_the_next_block_and_survives_save_reopen()
    {
        var root = TempRoot("boards-enter-paragraph");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "maths.9to1board");
            using (var store = new JsonFileHavenBoardStore(root))
            {
                await SeedAsync(store, path);
                await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
                {
                    var vm = new BoardsViewModel(adapter);
                    await EnsureEditModeAsync(vm);
                    vm.FocusBlock("p-sum");

                    Assert.True(await vm.SplitTextBlockAtAsync("p-sum", "x^2 + ".Length));
                    var blocks = Blocks(adapter);
                    Assert.Equal(4, blocks.Count);
                    Assert.Equal("x^2 + ", blocks[0].Text);
                    Assert.Equal("y = 3x", blocks[1].Text);
                    Assert.Equal("paragraph", blocks[1].Kind);
                    Assert.Equal("left right", blocks[2].Text);
                    Assert.Equal(vm.SelectedBlockId, blocks[1].Id);

                    await vm.DispatchAsync("SaveBoard", null);
                }
                await using (var fresh = await ContractSessionAdapter.OpenAsync(store, path))
                {
                    var reopened = Blocks(fresh);
                    Assert.Equal(4, reopened.Count);
                    Assert.Equal("x^2 + ", reopened[0].Text);
                    Assert.Equal("y = 3x", reopened[1].Text);
                    Assert.Equal("paragraph", reopened[1].Kind);
                }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Enter_in_a_bulleted_row_splits_it_in_place_and_keeps_the_same_kind()
    {
        var root = TempRoot("boards-enter-bulleted");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "maths.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await SeedAsync(store, path);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var vm = new BoardsViewModel(adapter);
            await EnsureEditModeAsync(vm);

            // The renderer names list-row editors chk_{rowId}; the previous code resolved
            // only "checklist" kinds, so text typed in a bulleted row was dropped and
            // Enter produced a checkbox item.
            Assert.True(await vm.SplitTextBlockAtAsync("list-1:i-1", "left ".Length));
            var blocks = Blocks(adapter);
            Assert.Equal(4, blocks.Count);
            Assert.Equal("left ", blocks[1].Text);
            Assert.Equal("right", blocks[2].Text);
            Assert.Equal("bulleted", blocks[1].Kind);
            Assert.Equal("bulleted", blocks[2].Kind);
            // The new row stays inside the same list block and directly after its source.
            Assert.StartsWith("list-1:", blocks[2].Id, StringComparison.Ordinal);
            Assert.Equal("div-1", blocks[3].Id);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Enter_at_the_end_of_a_list_row_adds_the_next_row_directly_after_it()
    {
        var root = TempRoot("boards-enter-list-end");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "maths.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await SeedAsync(store, path);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var vm = new BoardsViewModel(adapter);
            await EnsureEditModeAsync(vm);

            await vm.ListEnterAsync("list-1:i-1", "left right".Length);
            var blocks = Blocks(adapter);
            Assert.Equal(4, blocks.Count);
            // The new row sits between the original row and the following divider,
            // never appended at the end of the page.
            Assert.Equal("list-1:i-1", blocks[1].Id);
            Assert.Equal("bulleted", blocks[2].Kind);
            Assert.Equal(string.Empty, blocks[2].Text);
            Assert.Equal("div-1", blocks[3].Id);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task A_split_list_row_keeps_its_position_through_save_and_reopen()
    {
        var root = TempRoot("boards-enter-list-order");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "maths.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await SeedAsync(store, path);
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var vm = new BoardsViewModel(adapter);
                await EnsureEditModeAsync(vm);
                Assert.True(await vm.SplitTextBlockAtAsync("list-1:i-1", "left ".Length));
                await vm.DispatchAsync("SaveBoard", null);
            }
            await using (var fresh = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var reopened = Blocks(fresh);
                Assert.Equal(4, reopened.Count);
                Assert.Equal("left ", reopened[1].Text);
                Assert.Equal("right", reopened[2].Text);
                Assert.Equal("bulleted", reopened[1].Kind);
                Assert.Equal("bulleted", reopened[2].Kind);
                Assert.Equal("divider", reopened[3].Kind);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Soft_break_keeps_text_in_one_block_and_a_non_splittable_block_reports_explicitly()
    {
        var root = TempRoot("boards-enter-soft");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "maths.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await SeedAsync(store, path);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var vm = new BoardsViewModel(adapter);
            await EnsureEditModeAsync(vm);

            // Shift+Enter inside a list row: same BlockID, one extra line, no sibling.
            vm.CommitListSoftBreak("list-1:i-1", "left\nright");
            var afterSoft = Blocks(adapter);
            Assert.Equal(3, afterSoft.Count);
            Assert.Equal("left\nright", afterSoft[1].Text);

            // Enter on a divider cannot split; the surface must say so rather than
            // swallow the key press.
            Assert.False(await vm.SplitTextBlockAtAsync("div-1", 0));
            Assert.Equal(3, Blocks(adapter).Count);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string TempRoot(string name) =>
        Path.Combine(Path.GetTempPath(), name + "-" + Guid.NewGuid().ToString("N"));

    private static List<RichBoardBlock> Blocks(ContractSessionAdapter adapter) =>
        adapter.Document.Sections.Single().Pages.Single().Blocks;

    private static async Task EnsureEditModeAsync(BoardsViewModel vm)
    {
        if (!vm.IsEditMode)
            await vm.DispatchAsync("ToggleEditView", null);
        Assert.True(vm.IsEditMode);
    }

    private static async Task SeedAsync(JsonFileHavenBoardStore store, string path)
    {
        await using var owner = await RichBoardSession.CreateNewAsync(store, "Maths revision");
        await owner.MutateAsync(rich =>
        {
            var page = rich.Sections.Single().Pages.Single();
            page.Blocks.Clear();
            page.Blocks.Add(new() { Id = "p-sum", PlainText = "x^2 + y = 3x", Order = 0 });
            page.Blocks.Add(new()
            {
                Id = "list-1",
                Kind = HavenRichBlockKind.BulletList,
                Items = [new HavenRichListItem { Id = "i-1", Text = "left right" }],
                Order = 1,
            });
            page.Blocks.Add(new()
            {
                Id = "div-1",
                Kind = HavenRichBlockKind.Divider,
                Divider = new HavenRichDivider(),
                Order = 2,
            });
        });
        await owner.SaveAsAsync(path);
    }
}
