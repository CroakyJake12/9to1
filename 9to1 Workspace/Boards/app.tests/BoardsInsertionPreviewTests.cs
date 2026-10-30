using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using CakeOS.Apps.Boards.Contract;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Text.Json;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsInsertionPreviewTests
{
    [Theory]
    [InlineData("Above", "InsertionAboveEdge")]
    [InlineData("Below", "InsertionBelowEdge")]
    [InlineData("Replace", "InsertionReplaceOutline")]
    public Task Actual_pointer_previews_the_target_without_mutation_or_geometry_changes(string placement, string overlay) =>
        Run(f =>
        {
            f.Open();
            var before = JsonSerializer.Serialize(f.Session.Document);
            var status = f.Session.Status;
            var undo = f.Session.CanUndo;
            var bounds = f.Row.Bounds;
            var editor = f.Row.GetVisualDescendants().OfType<TextBox>().Single();
            var textBounds = editor.Bounds;
            var rebuilds = 0;
            f.Vm.RebuildRequested += () => rebuilds++;
            var destination = f.Find(f.Context.Popup.Child!, "Insert" + placement);
            f.Move(destination); f.Idle();
            Assert.Equal(placement, f.Vm.InsertionPreview?.Placement.ToString());
            var shown = f.Find(f.Row, overlay);
            Assert.True(shown.IsEffectivelyVisible);
            Assert.False(f.Find(f.Row, "BlockInsertionPreview").IsHitTestVisible);
            Assert.True(shown.Bounds.Width > 0);
            if (placement == "Above") Assert.Equal(0, shown.TranslatePoint(default, f.Row)!.Value.Y - 2, 1);
            if (placement == "Below") Assert.Equal(f.Row.Bounds.Height - 2,
                shown.TranslatePoint(default, f.Row)!.Value.Y + shown.Bounds.Height, 1);
            Assert.Equal(bounds, f.Row.Bounds);
            Assert.Equal(textBounds, editor.Bounds);
            Assert.Equal(before, JsonSerializer.Serialize(f.Session.Document));
            Assert.Equal(status, f.Session.Status);
            Assert.Equal(undo, f.Session.CanUndo);
            Assert.Equal(0, rebuilds);
            if (Environment.GetEnvironmentVariable("NINETOONE_PREVIEW_CAPTURE_DIR") is { Length: > 0 } captures)
            {
                Directory.CreateDirectory(captures);
                using var bitmap = new RenderTargetBitmap(new PixelSize(800, 650), new Vector(96, 96));
                bitmap.Render(f.Window); bitmap.Save(Path.Combine(captures, placement + ".png"));
            }
            f.Window.MouseMove(new Point(760, 550)); f.Idle();
            Assert.Null(f.Vm.InsertionPreview);
            Assert.False(shown.IsEffectivelyVisible);
        });

    [Fact]
    public Task Keyboard_destination_choice_pins_preview_through_type_stage_and_escape_clears_it() => Run(f =>
    {
        f.Open();
        f.Key(Key.Down);
        Assert.Equal(BlockInsertionPlacement.Above, f.Vm.InsertionPreview?.Placement);
        f.Key(Key.Down);
        Assert.Equal(BlockInsertionPlacement.Below, f.Vm.InsertionPreview?.Placement);
        f.Key(Key.Down);
        Assert.Equal(BlockInsertionPlacement.Replace, f.Vm.InsertionPreview?.Placement);
        f.Key(Key.Enter);
        Assert.Equal("replace", f.Context.Placement);
        Assert.Equal(BlockInsertionPlacement.Replace, f.Vm.InsertionPreview?.Placement);
        Assert.Contains(f.Context.Popup.Child!.GetVisualDescendants().OfType<Button>(),
            button => AutomationProperties.GetName(button) == "Insert Paragraph");
        f.Window.MouseMove(new Point(760, 550)); f.Idle();
        Assert.Equal(BlockInsertionPlacement.Replace, f.Vm.InsertionPreview?.Placement);
        f.Key(Key.Escape);
        Assert.False(f.Context.Popup.IsOpen);
        Assert.Null(f.Vm.InsertionPreview);
        Assert.True(f.Insert.IsFocused);
        f.Open();
        Assert.Null(f.Context.Placement);
        Assert.NotNull(f.Find(f.Context.Popup.Child!, "InsertAbove"));
    });

    private static async Task Run(Action<Fixture> action)
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/preview-" + Guid.NewGuid().ToString("N"));
        TestUiThread.Run(() => { using var fixture = new Fixture(session); action(fixture); });
    }

    [Theory]
    [InlineData("Above")]
    [InlineData("Below")]
    [InlineData("Replace")]
    public Task Actual_destination_then_type_commits_at_the_previewed_position_and_reopens(string placement) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot;
            var f = TestUiThread.Run(() => new Fixture(adapter, seed: false, vm));
            try
            {
                TestUiThread.Run(() =>
                {
                    f.Open(); f.Click(f.Find(f.Context.Popup.Child!, "Insert" + placement));
                    Assert.Equal(placement, vm.InsertionPreview?.Placement.ToString());
                    var item = f.Context.Popup.Child!.GetVisualDescendants().OfType<Button>()
                        .Single(button => AutomationProperties.GetName(button) == "Insert Table");
                    f.Click(item); Assert.Null(vm.InsertionPreview); Assert.False(f.Context.Popup.IsOpen);
                });
                var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
                var blocks = saved.Sections[0].Pages[0].Blocks;
                Assert.Equal(placement == "Replace" ? 3 : 4, blocks.Count);
                Assert.Equal(HavenRichBlockKind.Table, blocks[placement == "Below" ? 2 : 1].Kind);
                Assert.Equal("first", blocks[0].Id); Assert.Equal("last", blocks[^1].Id);
                Assert.Equal(placement != "Replace", blocks.Any(block => block.Id == "anchor"));
                Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            }
            finally { TestUiThread.Run(f.Dispose); }
        });

    [Fact]
    public Task Unsupported_type_rows_are_actually_disabled_and_explain_the_limitation() => Run(f =>
    {
        f.Open(); f.Click(f.Find(f.Context.Popup.Child!, "InsertReplace"));
        foreach (var label in new[] { "Image…", "Attachment…", "Freeform box" })
        {
            var row = f.Context.Popup.Child!.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "Insert " + label);
            Assert.False(row.IsEffectivelyEnabled);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(row)));
            f.Click(row); Assert.True(f.Context.Popup.IsOpen);
        }
        Assert.Equal(3, f.Session.Document.Sections[0].Pages[0].Blocks.Count);
    });

    [Fact]
    public Task Free_mode_list_preview_and_replacement_target_exactly_one_canonical_list() =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot;
            var f = TestUiThread.Run(() => new Fixture(adapter, seed: false, vm, "anchor:one", spatialHost: true));
            try
            {
                TestUiThread.Run(() =>
                {
                    Assert.Equal(4, f.Host.Children.Count);
                    Assert.Equal(2, f.Row.GetVisualDescendants().OfType<TextBox>().Count());
                    f.Open(); f.Move(f.Find(f.Context.Popup.Child!, "InsertReplace")); f.Idle();
                    Assert.Equal("anchor", vm.InsertionPreview!.Owner.AnchorBlockId);
                    Assert.Equal("anchor:one", vm.InsertionPreview.Owner.Row.Id);
                    var overlay = f.Find(f.Row, "InsertionReplaceOutline");
                    Assert.True(overlay.IsEffectivelyVisible); Assert.Equal(f.Row.Bounds.Height - 4, overlay.Bounds.Height, 1);
                    f.Click(f.Find(f.Context.Popup.Child!, "InsertReplace"));
                    f.Click(f.Context.Popup.Child!.GetVisualDescendants().OfType<Button>()
                        .Single(button => AutomationProperties.GetName(button) == "Insert Table"));
                });
                var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
                var blocks = saved.Sections[0].Pages[0].Blocks;
                Assert.Equal(new[] { "first", "TABLE", "other-list", "last" },
                    blocks.Select(block => block.Kind == HavenRichBlockKind.Table ? "TABLE" : block.Id));
                Assert.Equal("Preserve other", Assert.Single(blocks[2].Items).Text);
                Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            }
            finally { TestUiThread.Run(f.Dispose); }
        }, rich =>
        {
            var page = rich.Sections[0].Pages[0]; page.LayoutMode = HavenRichPageLayoutModes.Unlocked;
            var list = page.Blocks[1]; list.Kind = HavenRichBlockKind.BulletList;
            list.Items = new[] { "one", "two" }.Select(id => new HavenRichListItem { Id = id, Text = "Keep " + id }).ToList();
            page.Blocks.Insert(2, new HavenRichBlock { Id = "other-list", Kind = HavenRichBlockKind.NumberedList,
                Items = [new HavenRichListItem { Id = "other", Text = "Preserve other" }] });
            for (var i = 0; i < page.Blocks.Count; i++) page.Blocks[i].Order = i;
        });

    [Fact]
    public Task Retired_type_row_cannot_commit_after_change_or_a_new_destination() => Run(f =>
    {
        f.Open(); f.Click(f.Find(f.Context.Popup.Child!, "InsertBelow"));
        var stale = f.Context.Popup.Child!.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == "Insert Paragraph");
        var change = f.Context.Popup.Child!.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == "Change the insert destination");
        f.Click(change);
        var before = JsonSerializer.Serialize(f.Session.Document);
        stale.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); f.Idle();
        Assert.Equal(before, JsonSerializer.Serialize(f.Session.Document));
        Assert.True(f.Context.Popup.IsOpen); Assert.Null(f.Context.Placement);
        f.Click(f.Find(f.Context.Popup.Child!, "InsertAbove"));
        stale.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); f.Idle();
        Assert.Equal(before, JsonSerializer.Serialize(f.Session.Document));
        Assert.True(f.Context.Popup.IsOpen); Assert.Equal("above", f.Context.Placement);
    });

    [Fact]
    public Task Pointer_and_keyboard_owners_are_independent_and_the_latest_remaining_one_is_visible() => Run(f =>
    {
        f.Open(); f.Key(Key.Down);
        f.Move(f.Find(f.Context.Popup.Child!, "InsertBelow")); f.Idle();
        Assert.Equal(BlockInsertionPlacement.Below, f.Vm.InsertionPreview?.Placement);
        f.Window.MouseMove(new Point(760, 550)); f.Idle();
        Assert.Equal(BlockInsertionPlacement.Above, f.Vm.InsertionPreview?.Placement);
        f.Key(Key.Down);
        f.Move(f.Find(f.Context.Popup.Child!, "InsertReplace")); f.Idle();
        f.Context.Search.Focus(); f.Idle();
        Assert.Equal(BlockInsertionPlacement.Replace, f.Vm.InsertionPreview?.Placement);
        f.Window.MouseMove(new Point(760, 550)); f.Idle(); Assert.Null(f.Vm.InsertionPreview);
    });

    [Theory]
    [InlineData("outside")]
    [InlineData("detach")]
    [InlineData("view")]
    [InlineData("page")]
    [InlineData("target")]
    [InlineData("same-id")]
    [InlineData("dispose")]
    public Task Preview_and_popup_retire_with_their_actual_owner(string transition) => Run(f =>
    {
        f.Open(); f.Move(f.Find(f.Context.Popup.Child!, "InsertReplace")); f.Idle();
        Assert.NotNull(f.Vm.InsertionPreview);
        switch (transition)
        {
            case "outside":
                var point = new Point(760, 550); f.Window.MouseDown(point, MouseButton.Left);
                f.Window.MouseUp(point, MouseButton.Left); break;
            case "detach": f.Host.Children.Remove(f.Row); break;
            case "view": f.Await(f.Vm.DispatchAsync("ToggleEditView", null).AsTask()); break;
            case "page":
                var section = f.Session.Document.Sections[0]; var next = new RichBoardPage();
                section.Pages.Add(next); f.Vm.SelectPage(section.Id, next.Id); break;
            case "target":
                f.Session.Document.Sections[0].Pages[0].Blocks.RemoveAt(1); f.Vm.RefreshAfterEdit(); break;
            case "same-id":
                f.Session.Document.Sections[0].Pages[0].Blocks[1] = new RichBoardBlock { Id = "anchor", Text = "new owner" };
                f.Vm.RefreshAfterEdit(); break;
            case "dispose": f.Vm.Dispose(); break;
        }
        f.Idle(); Assert.Null(f.Vm.InsertionPreview); Assert.False(f.Context.Popup.IsOpen);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Real_session_refresh_or_disposal_clears_the_mounted_preview(bool dispose) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot; var version = adapter.DocumentVersion;
            var fixture = TestUiThread.Run(() => new Fixture(adapter, seed: false, vm));
            try
            {
                TestUiThread.Run(() =>
                {
                    fixture.Open(); fixture.Move(fixture.Find(fixture.Context.Popup.Child!, "InsertReplace")); fixture.Idle();
                    Assert.NotNull(vm.InsertionPreview); Assert.False(adapter.CanUndo);
                });
                if (dispose) await adapter.DisposeAsync();
                else Assert.False(await adapter.UndoAsync()); // no preview transaction, but a real projection refresh
                TestUiThread.Run(() =>
                {
                    Dispatcher.UIThread.RunJobs(); fixture.Idle();
                    Assert.Null(vm.InsertionPreview); Assert.False(fixture.Context.Popup.IsOpen);
                });
                if (!dispose) { Assert.Equal(before, adapter.Snapshot); Assert.Equal(version, adapter.DocumentVersion); }
            }
            finally { TestUiThread.Run(fixture.Dispose); }
        });

    [Fact]
    public async Task Two_live_boards_with_the_same_block_ids_do_not_share_preview_ownership()
    {
        await using var first = new InMemoryRichBoardSession(); await first.OpenAsync("memory://boards/preview-first");
        await using var second = new InMemoryRichBoardSession(); await second.OpenAsync("memory://boards/preview-second");
        TestUiThread.Run(() =>
        {
            using var a = new Fixture(first); using var b = new Fixture(second);
            a.Open(); a.Click(a.Find(a.Context.Popup.Child!, "InsertBelow"));
            b.Open(); b.Move(b.Find(b.Context.Popup.Child!, "InsertReplace")); b.Idle();
            a.Context.Close(); a.Idle(); b.Idle();
            Assert.Null(a.Vm.InsertionPreview);
            Assert.Equal(BlockInsertionPlacement.Replace, b.Vm.InsertionPreview?.Placement);
            Assert.Same(second, b.Vm.InsertionPreview!.Owner.Session);
            Assert.True(b.Context.Popup.IsOpen);
        });
    }

    private sealed class Fixture : IDisposable
    {
        internal IRichBoardSession Session { get; }
        internal BoardsViewModel Vm { get; }
        internal StackPanel Host { get; }
        internal Window Window { get; }
        internal Border Row { get; }
        internal Button Insert { get; }
        internal InsertMenuContext Context { get; }
        internal Fixture(IRichBoardSession session, bool seed = true, BoardsViewModel? viewModel = null,
            string rowId = "anchor", bool spatialHost = false)
        {
            Session = session; Vm = viewModel ?? new BoardsViewModel(session);
            if (seed)
            {
                var page = session.Document.Sections[0].Pages[0]; page.Blocks.Clear();
                foreach (var id in new[] { "first", "anchor", "last" })
                    page.Blocks.Add(new RichBoardBlock { Id = id, Kind = "paragraph", Text = "Unchanged " + id });
            }
            Host = new StackPanel { Width = 600, Margin = new Thickness(76, 20, 0, 0) };
            BlockRenderer.Rebuild(Host, Vm, [], spatialHost ? new Canvas() : null);
            Window = new Window { Width = 800, Height = 650, Content = Host };
            Window.Show(); Window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Row = Assert.Single(Host.Children.OfType<Border>(), row => row.Tag is BlockReorderRow target && target.BlockId == rowId);
            Insert = Assert.Single(Row.GetVisualDescendants().OfType<Button>(), ToolbarBuilder.HasInsertMenu);
            Context = Assert.IsType<InsertMenuContext>(ToolbarBuilder.InsertMenuFor(Insert));
        }
        internal Control Find(Control root, string id) =>
            Assert.IsAssignableFrom<Control>(BoardsCuiControlFinder.FindByAutomationId<Control>(root, id));
        internal Point Center(Control control) => control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)!.Value;
        internal void Move(Control control) => Window.MouseMove(Center(control));
        internal void Click(Control control)
        {
            control.BringIntoView(); Window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var point = Center(control); Window.MouseMove(point);
            Window.MouseDown(point, MouseButton.Left); Window.MouseUp(point, MouseButton.Left); Idle();
        }
        internal void Open() { Click(Insert); Assert.True(Context.Popup.IsOpen); }
        internal void Key(Key key)
        {
            Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null); Idle();
        }
        internal void Idle()
        {
            Window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Await(Context.WhenActionsIdleAsync());
            Window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        }
        internal void Await(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            Assert.True(task.IsCompleted, "Insertion actions did not settle.");
            task.GetAwaiter().GetResult();
        }
        public void Dispose() { Window.Close(); Dispatcher.UIThread.RunJobs(); Vm.Dispose(); }
    }

    [Fact]
    public async Task Actual_inline_plus_opens_destinations_before_the_type_list()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/actual-insert-menu");
        TestUiThread.Run(() =>
        {
            using var vm = new BoardsViewModel(session);
            var page = session.Document.Sections[0].Pages[0];
            page.Blocks.Clear();
            page.Blocks.Add(new RichBoardBlock { Id = "anchor", Kind = "paragraph", Text = "Unchanged content" });
            var host = new StackPanel { Width = 600, Margin = new Thickness(76, 20, 0, 0) };
            BlockRenderer.Rebuild(host, vm, []);
            var window = new Window { Width = 800, Height = 600, Content = host };
            window.Show();
            try
            {
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var insert = Assert.Single(host.GetVisualDescendants().OfType<Button>(),
                    ToolbarBuilder.HasInsertMenu);
                var point = insert.TranslatePoint(new Point(16, 16), window)!.Value;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var context = Assert.IsType<InsertMenuContext>(ToolbarBuilder.InsertMenuFor(insert));
                var popup = context.Popup;
                Assert.True(popup.IsOpen);
                var names = popup.Child!.GetSelfAndVisualDescendants().OfType<Control>()
                    .Select(AutomationProperties.GetName).ToArray();
                Assert.Contains("Add Block Above", names);
                Assert.Contains("Add Block Below", names);
                Assert.Contains("Replace Current Block", names);
                Assert.DoesNotContain("Insert Paragraph", names);
                Assert.Equal("Unchanged content", Assert.Single(page.Blocks).Text);
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        });
    }
}
