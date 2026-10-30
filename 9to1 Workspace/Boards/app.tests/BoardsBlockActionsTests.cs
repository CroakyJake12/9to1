using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsBlockActionsTests
{
    // Load-level check, reported on its own: the authored grip document must produce a real toggle and
    // publish its menu host. Every higher-level menu test degrades silently if this fails, so a fallback
    // "unavailable" TextBlock here names the real cause instead of a null in a gesture test.
    [Fact]
    public async Task The_authored_grip_document_loads_a_real_toggle_and_publishes_its_menu_host()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-grip-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "load.9to1board");
            await SeedAsync(store, path, false);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var vm = new BoardsViewModel(adapter);

            var built = TestUiThread.Run(() => BlockGripCui.Build(vm, "block-a"));
            var observed = TestUiThread.Run(() =>
            {
                var toggle = built.GetSelfAndVisualDescendants().OfType<Button>()
                    .SingleOrDefault(button => button.Name == "BlockGripToggle");
                var host = built.GetSelfAndVisualDescendants().OfType<Control>()
                    .Select(BlockGripCui.HostFor)
                    .OfType<BlockGripCui.BlockActionsHost>()
                    .SingleOrDefault();
                string[] menuNames = Array.Empty<string>();
                if (host is not null)
                {
                    var menuSurface = host.Menu;
                    menuNames = AuthoredControls(menuSurface).OfType<MenuItem>()
                        .Select(button => button.Name ?? string.Empty)
                        .ToArray();
                }
                return (Fallback: built is TextBlock, FallbackText: (built as TextBlock)?.Text ?? string.Empty,
                    Toggle: toggle is not null, Host: host, MenuNames: menuNames);
            });

            Assert.False(observed.Fallback, "the authored grip fell back to the unavailable message: "
                + observed.FallbackText);
            Assert.True(observed.Toggle, "the authored grip has no BlockGripToggle button");
            Assert.NotNull(observed.Host);
            var host = observed.Host!;
            Assert.NotNull(host.Menu);
            foreach (var expected in new[] { "BlockDelete", "BlockDuplicate", "BlockMoveUp", "BlockMoveDown",
                "BlockDesignMenuToggle", "BlockMoveMenuToggle" })
                Assert.Contains(expected, observed.MenuNames);
            Assert.Equal(0, host.OpenedCount);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // The owner's 29 October block menu must actually appear when the grip is opened. This check is
    // deliberately gesture-free (it drives the authored command instead of pointer routing) so a failure
    // names the broken stage: hosting, opening, or the rows being visible where they live.
    [Fact]
    public async Task Opening_the_authored_grip_menu_presents_every_mockup_row_in_the_floating_surface()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/grip-menu-host");
        var viewModel = new BoardsViewModel(session);
        TestUiThread.Run(() =>
        {
            var page = session.Document.Sections[0].Pages[0];
            page.Blocks.Clear();
            page.Blocks.Add(new RichBoardBlock { Id = "first", Kind = "paragraph", Text = "first" });
            page.Blocks.Add(new RichBoardBlock { Id = "second", Kind = "paragraph", Text = "second" });
            return true;
        });

        var fixture = TestUiThread.Run(() =>
        {
            var host = new StackPanel { Width = 600 };
            BlockRenderer.Rebuild(host, viewModel, []);
            var window = new Window { Width = 720, Height = 520, Content = host };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            // The row Border is tagged BlockReorderRow by BlockRenderer and holds exactly this block's
            // gutter, so the grip root is the Button-typed ancestor of the toggle inside that row.
            var row = Assert.Single(host.GetLogicalDescendants().OfType<Border>(), border =>
                border.Tag is BlockReorderRow reorder && reorder.BlockId == "second");
            var trigger = Assert.Single(row.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "BlockGripToggle");
            var gripRoot = Assert.IsAssignableFrom<Control>(trigger.Parent);
            return (Window: window, Row: row, GripRoot: gripRoot, Trigger: trigger);
        });
        try
        {
            // STAGE 1 - hosting: the authored menu and its C# floating surface must be reachable from the
            // grip root, otherwise the menu can never be shown to the owner.
            // Unique path to this grip's own surface: the authored menu is the Popup's child, and the
            // Popup is logically parented to the gutter trigger (BlockGripCui.Build), so it is not
            // confused with the row/menu Border tags the renderer and CUI also write.
            var host = BlockGripCui.HostFor(fixture.Trigger);
            Assert.NotNull(host);
            var popup = host!.Popup;
            TestUiThread.Run(() =>
            {
                Assert.Same(fixture.Trigger, popup.PlacementTarget);
                Assert.NotNull(host.Menu);
                Assert.False(host.Popup.IsOpen, "the block menu must stay closed until the grip is opened");
            });

            // STAGE 2 - opening: the authored ToggleActions command must open that same surface.
            TestUiThread.Run(() => fixture.Trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            // Synchronous pump: TestUiThread.Run takes a plain Func, so an async lambda inside it is a
            // compile error (measured 29 October). RunJobs on the UI thread settles the authored action.
            TestUiThread.Run(() =>
            {
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(25);
                Dispatcher.UIThread.RunJobs();
                return 0;
            });

            // STAGE 3 - rows: the mockup's rows must exist inside that surface.
            var names = TestUiThread.Run(() => AuthoredControls(host.Menu).OfType<MenuItem>()
                .Select(button => button.Name)
                .Where(name => name is not null)
                .ToArray());
            Assert.Contains("BlockDelete", names);
            Assert.Contains("BlockDuplicate", names);
            Assert.Contains("BlockMoveUp", names);
            Assert.Contains("BlockMoveDown", names);
            Assert.Contains("BlockDesignMenuToggle", names);
            TestUiThread.Run(() => Assert.True(host.Popup.IsOpen,
                "MENU OPEN CHAIN press=" + host.PressCount + " click=" + host.ClickCount
                + " authored=" + host.ToggleCommandsReceived + " attempts=" + host.OpenAttempts
                + " opened=" + host.OpenedCount + " closed=" + host.ClosedCount));
        }
        finally
        {
            TestUiThread.Run(() => fixture.Window.Close());
        }
    }

    [Theory]
    [InlineData("move", false)]
    [InlineData("click", false)]
    [InlineData("escape", false)]
    [InlineData("stale", false)]
    [InlineData("return-to-grip", false)]
    [InlineData("move", true)]
    [InlineData("click", true)]
    [InlineData("escape", true)]
    public async Task Actual_grip_pointer_routes_move_click_cancel_and_revision_refusal(string gesture, bool freePlacement)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-actual-grip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "pointer.9to1board");
            await SeedAsync(store, path, freePlacement);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var vm = new BoardsViewModel(adapter);
            if (!vm.IsEditMode) await vm.DispatchAsync("ToggleEditView", null);
            var original = adapter.Snapshot;
            var fixture = TestUiThread.Run(() =>
            {
                var host = new StackPanel { Width = 600, Margin = new Thickness(76, 0, 0, 0),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
                var absoluteHost = new Canvas();
                var surface = new Grid();
                surface.Children.Add(absoluteHost); surface.Children.Add(host);
                BlockRenderer.Rebuild(host, vm, [], absoluteHost);
                var window = new Window { Width = 760, Height = 600, Content = surface };
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var row = Assert.Single(host.Children.OfType<Border>(), value =>
                    value.Tag is BlockReorderRow { BlockId: "block-a" });
                var grip = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => button.Name == "BlockGripToggle");
                // The authored grip keeps its action row inactive until the grip is opened
                // (BlockGripCui.cui: Active="{Binding IsExpanded}"). The menu target is therefore
                // resolved after the gesture, from the same row, instead of being captured at rest.
                var start = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
                var end = host.TranslatePoint(new Point(start.X, host.Bounds.Height + 12), window)!.Value;
                return (Window: window, Host: host, Row: row, Grip: grip, Start: start, End: end);
            });
            var releasedTop = double.NaN;
            var landingTops = new List<double>();
            try
            {
                TestUiThread.Run(() =>
                {
                    fixture.Row.PropertyChanged += (_, change) =>
                    {
                        if (change.Property == Visual.RenderTransformProperty &&
                            change.NewValue is Avalonia.Media.TranslateTransform && !fixture.Grip.IsPressed)
                            landingTops.Add(fixture.Row.TranslatePoint(new Point(0, 0), fixture.Host)!.Value.Y);
                    };
                    fixture.Window.MouseMove(fixture.Start);
                    fixture.Window.MouseDown(fixture.Start, MouseButton.Left);
                    Assert.True(fixture.Grip.IsPressed);
                    if (gesture != "click")
                    {
                        fixture.Window.MouseMove(fixture.End);
                        Dispatcher.UIThread.RunJobs();
                        if (freePlacement) Assert.IsType<Avalonia.Media.TranslateTransform>(fixture.Row.RenderTransform);
                        // The guide line now lives on the row's zero-footprint overlay: it marks the
                        // slot edge without adding thickness to the row, which used to shift the text
                        // under a dragging block (owner 29 October: keep the line, for accessibility).
                        else Assert.Contains(fixture.Host.Children.OfType<Border>(), row =>
                            row.GetVisualDescendants().OfType<Border>().Any(outline =>
                                outline.Name == "BlockRowOutline" &&
                                (outline.BorderThickness.Top == 3 || outline.BorderThickness.Bottom == 3)));
                        if (gesture == "escape")
                            fixture.Grip.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                        if (gesture == "stale") vm.EditText("t_block-b", "Changed during drag");
                        if (gesture is "return-to-grip" or "escape") fixture.Window.MouseMove(fixture.Start);
                    }
                    if (gesture == "move" && !freePlacement)
                        releasedTop = fixture.Row.TranslatePoint(new Point(0, 0), fixture.Host)!.Value.Y;
                    fixture.Window.MouseUp(gesture is "click" or "return-to-grip" or "escape" ? fixture.Start : fixture.End, MouseButton.Left);
                    Dispatcher.UIThread.RunJobs();
                });
                if (gesture is "move" or "stale")
                {
                    for (var attempt = 0; attempt < 100; attempt++)
                    {
                        if (TestUiThread.Run(() => gesture == "move" ?
                            (freePlacement ? Blocks(adapter)[0].Layout == HavenRichBlockLayouts.Absolute : Blocks(adapter)[0].Id == "block-b") :
                            vm.TryGetValue("StatusText", out var status) && status?.ToString()?.Contains("Block move refused", StringComparison.Ordinal) == true)) break;
                        await Task.Delay(10);
                    }
                }
                if (!freePlacement && gesture is "move" or "return-to-grip")
                {
                    // A successful drop commits before its settling animation finishes. Retain the
                    // cleanup assertion, but allow the bounded animation its real timer frames.
                    for (var attempt = 0; attempt < 100; attempt++)
                    {
                        if (TestUiThread.Run(() =>
                        {
                            // RunJobs drains queued work but never services the platform timer.
                            // Pump the real dispatcher briefly so the production settle timer runs.
                            using var frame = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
                            Dispatcher.UIThread.MainLoop(frame.Token);
                            return fixture.Host.Children.OfType<Border>().All(row => row.RenderTransform is null);
                        })) break;
                    }
                }
                TestUiThread.Run(() =>
                {
                    Assert.False(fixture.Grip.IsPressed);
                    // The authored block menu floats in a C#-owned Popup hosted off the block row
                    // (29 October: the gutter is a 32-unit column, so a 232-unit menu cannot render
                    // inside it). The row subtree search is retained AND the floating host is searched,
                    // so the assertions below still enforce the same behaviour - a real revealed row
                    // for a click, and no revealed row for drag/escape/stale - they only follow where
                    // the rows actually live.
                    var hosts = fixture.Grip.GetSelfAndVisualDescendants().OfType<Control>()
                        .Select(BlockGripCui.HostFor)
                        .OfType<BlockGripCui.BlockActionsHost>()
                        .Distinct()
                        .ToArray();
                    var revealed = fixture.Window.GetSelfAndVisualDescendants().OfType<MenuItem>()
                        .Concat(fixture.Grip.GetLogicalDescendants().OfType<MenuItem>())
                        .Concat(hosts.Where(host => host.Popup.IsOpen)
                            .SelectMany(host => host.Menu.GetSelfAndVisualDescendants().OfType<MenuItem>()))
                        .Distinct()
                        .SingleOrDefault(button => button.Name == "BlockDuplicate");
                    if (gesture == "click")
                    {
                        // Staged so the first failure names the broken stage: hosting, then opening,
                        // then the row actually being visible where it lives.
                        var ownHost = Assert.Single(hosts);
                        Assert.True(ownHost.Popup.IsOpen, "STAGE: the grip's own menu surface did not open");
                        var inHost = ownHost.Menu.GetSelfAndVisualDescendants().OfType<MenuItem>()
                            .SingleOrDefault(button => button.Name == "BlockDuplicate");
                        Assert.NotNull(inHost);
                        Assert.True(inHost!.IsEffectivelyVisible, "STAGE: the menu row is present but not visible");
                        Assert.NotNull(revealed);
                        Assert.True(revealed!.IsEffectivelyVisible);
                    }
                    else
                    {
                        // Drag, Escape-return and stale-ticket gestures must never open the menu.
                        Assert.All(hosts, host => Assert.False(host.Popup.IsOpen));
                        Assert.True(revealed is null || !revealed.IsEffectivelyVisible);
                    }
                    Assert.DoesNotContain(fixture.Host.Children.OfType<Border>(), row =>
                        row.GetVisualDescendants().OfType<Border>().Any(outline =>
                            outline.Name == "BlockRowOutline" &&
                            (outline.BorderThickness.Top == 3 || outline.BorderThickness.Bottom == 3)));
                    Assert.Null(fixture.Row.RenderTransform);
                    Assert.All(fixture.Host.Children.OfType<Border>(), row => Assert.Null(row.RenderTransform));
                    if (gesture == "move" && !freePlacement)
                        Assert.Equal(releasedTop, Assert.Single(landingTops), precision: 6);
                    if (gesture == "stale") Assert.Empty(landingTops);
                    Assert.Equal(gesture == "move" && !freePlacement ? new[] { "block-b", "block-c", "block-a" } :
                        new[] { "block-a", "block-b", "block-c" }, Blocks(adapter).Select(block => block.Id));
                    if (freePlacement)
                        Assert.Equal(gesture == "move" ? HavenRichBlockLayouts.Absolute : HavenRichBlockLayouts.Flow,
                            Blocks(adapter)[0].Layout);
                    if (gesture == "stale")
                    {
                        Assert.Equal("Changed during drag", Blocks(adapter)[1].Text);
                        Assert.True(vm.TryGetValue("StatusText", out var status));
                        Assert.Contains("Block move refused", status?.ToString() ?? string.Empty);
                    }
                });
                if (gesture == "move")
                {
                    Assert.True(await adapter.UndoAsync());
                    Assert.Equal(original, adapter.Snapshot);
                    Assert.False(adapter.CanUndo);
                }
                else if (gesture != "stale")
                {
                    Assert.Equal(original, adapter.Snapshot);
                    Assert.False(adapter.CanUndo);
                }
            }
            finally { TestUiThread.Run(() => fixture.Window.Close()); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // CUI detaches inactive submenu cells from layout while retaining their authored controls.
    // Loading proves all authored rows exist; opening/gesture checks separately require visible rows
    // in the actual popup. Menu Item is a MenuItem, never an Avalonia Button.
    private static IEnumerable<Control> AuthoredControls(Control root)
    {
        yield return root;
        var children = root is CuiContainerControl container
            ? container.AuthoredChildren
            : root.GetVisualChildren().OfType<Control>();
        foreach (var child in children)
            foreach (var descendant in AuthoredControls(child))
                yield return descendant;
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("escape")]
    [InlineData("trigger")]
    public async Task Authored_menu_dismisses_and_reopens_with_one_click(string dismissal)
    {
        await WithMenuAsync(async fixture =>
        {
            await OpenMenuAsync(fixture);
            if (dismissal == "escape" && Environment.GetEnvironmentVariable("NINETOONE_BOARDS_MENU_CAPTURE") is { Length: > 0 } capturePath)
                TestUiThread.Run(() =>
                {
                    using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(760, 600), new Vector(96, 96));
                    bitmap.Render(fixture.Window);
                    bitmap.Save(capturePath);
                });
            TestUiThread.Run(() =>
            {
                if (dismissal == "outside")
                {
                    var outside = new Point(fixture.Window.Bounds.Width - 8, fixture.Window.Bounds.Height - 8);
                    fixture.Window.MouseDown(outside, MouseButton.Left);
                    fixture.Window.MouseUp(outside, MouseButton.Left);
                }
                else if (dismissal == "escape")
                    MenuRow(fixture, "BlockDelete").RaiseEvent(new KeyEventArgs
                        { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                else fixture.Trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            await DrainMenuAsync(fixture);
            TestUiThread.Run(() => Assert.False(fixture.Host.Popup.IsOpen));
            await OpenMenuAsync(fixture);
            TestUiThread.Run(() =>
            {
                Assert.Equal(2, fixture.Host.OpenedCount);
                Assert.Equal(dismissal == "trigger" ? 3 : 2, fixture.Host.ToggleCommandsReceived);
            });
            Assert.Equal(3, Blocks(fixture.Adapter).Count);
            Assert.False(fixture.Adapter.CanUndo);
        });
    }

    [Fact]
    public async Task Authored_menu_keyboard_opens_submenu_then_escapes_to_its_trigger()
    {
        await WithMenuAsync(async fixture =>
        {
            await OpenMenuAsync(fixture);
            TestUiThread.Run(() =>
            {
                var move = MenuRow(fixture, "BlockMoveMenuToggle");
                Assert.True(move.Focus());
                SendKey(fixture, PhysicalKey.Enter);
            });
            await DrainMenuAsync(fixture);
            TestUiThread.Run(() =>
            {
                Assert.True(MenuRow(fixture, "BlockMoveUp").IsEffectivelyVisible);
                var down = MenuRow(fixture, "BlockMoveDown");
                Assert.True(down.Focus());
                SendKey(fixture, PhysicalKey.Escape);
                Assert.True(fixture.Host.Popup.IsOpen);
                Assert.True(MenuRow(fixture, "BlockMoveMenuToggle").IsFocused);
                Assert.DoesNotContain(fixture.Host.Menu.GetVisualDescendants().OfType<MenuItem>(),
                    row => row.Name == "BlockMoveDown" && row.IsEffectivelyVisible);
                SendKey(fixture, PhysicalKey.Escape);
                Assert.False(fixture.Host.Popup.IsOpen);
                Assert.True(fixture.Trigger.IsFocused);
            });
        });
    }

    [Theory]
    [InlineData(HavenRichBlockKind.Paragraph)]
    [InlineData(HavenRichBlockKind.Checklist)]
    [InlineData(HavenRichBlockKind.BulletList)]
    [InlineData(HavenRichBlockKind.NumberedList)]
    public async Task Authored_duplicate_finishes_without_waiting_on_itself_and_keeps_one_undo(HavenRichBlockKind kind)
    {
        await WithMenuAsync(async fixture =>
        {
            var before = fixture.Adapter.Snapshot;
            var beforeCount = Blocks(fixture.Adapter).Count;
            await OpenMenuAsync(fixture);
            TestUiThread.Run(() =>
            {
                var duplicate = MenuRow(fixture, "BlockDuplicate");
                Assert.True(duplicate.Focus());
                SendKey(fixture, PhysicalKey.Enter);
            });
            await DrainMenuAsync(fixture);
            TestUiThread.Run(() => Assert.False(fixture.Host.Popup.IsOpen));
            var blocks = Blocks(fixture.Adapter);
            Assert.Equal(beforeCount + 1, blocks.Count);
            Assert.Equal(2, blocks.Count(block => block.Text == "AlphaBeta"));
            Assert.Equal(beforeCount + 1, blocks.Select(block => block.Id).Distinct().Count());
            TestUiThread.Run(() =>
            {
                var focused = Assert.IsType<TextBox>(fixture.Window.FocusManager!.GetFocusedElement());
                var editor = kind == HavenRichBlockKind.Paragraph ? "t_" : "chk_";
                Assert.Equal(editor + fixture.ViewModel.SelectedBlockId.Replace(':', '_'), focused.Name);
                Assert.Same(fixture.Window, TopLevel.GetTopLevel(focused));
            });
            Assert.True(await fixture.Adapter.UndoAsync());
            Assert.Equal(before, fixture.Adapter.Snapshot);
            Assert.False(fixture.Adapter.CanUndo);
        }, liveRebuild: true, kind: kind);
    }

    [Fact]
    public async Task Authored_design_converts_existing_text_and_disables_lossy_destinations()
    {
        await WithMenuAsync(async fixture =>
        {
            var before = fixture.Adapter.Snapshot;
            await OpenMenuAsync(fixture);
            TestUiThread.Run(() =>
            {
                Assert.True(MenuRow(fixture, "BlockDesignMenuToggle").Focus());
                SendKey(fixture, PhysicalKey.Enter);
            });
            await DrainMenuAsync(fixture);
            TestUiThread.Run(() =>
            {
                foreach (var name in new[] { "Text", "Checklist", "Bulleted", "Numbered", "Table", "Image", "Divider" })
                    Assert.False(MenuRow(fixture, "BlockTurnInto" + name).IsEnabled);
                var heading = MenuRow(fixture, "BlockTurnIntoHeading");
                Assert.True(heading.IsEffectivelyVisible);
                Assert.True(heading.IsEnabled);
                Assert.True(heading.Focus());
                SendKey(fixture, PhysicalKey.Enter);
            });
            await DrainMenuAsync(fixture);
            Assert.Equal(3, Blocks(fixture.Adapter).Count);
            var changed = Blocks(fixture.Adapter).Single(block => block.Id == "block-b");
            Assert.Equal("heading", changed.Kind);
            Assert.Equal("AlphaBeta", changed.Text);
            Assert.Equal("Georgia", changed.Runs[1].FontFamily);
            TestUiThread.Run(() =>
            {
                Assert.False(fixture.Host.Popup.IsOpen);
                Assert.Equal("block-b", fixture.ViewModel.SelectedBlockId);
                Assert.Equal("t_block-b", Assert.IsType<TextBox>(fixture.Window.FocusManager!.GetFocusedElement()).Name);
            });
            Assert.True(await fixture.Adapter.UndoAsync());
            Assert.Equal(before, fixture.Adapter.Snapshot);
            Assert.False(fixture.Adapter.CanUndo);
        }, liveRebuild: true);
    }

    [Fact]
    public async Task Design_does_not_refresh_its_ticket_after_the_open_block_changes()
    {
        await WithMenuAsync(async fixture =>
        {
            var context = new BlockGripCui.BlockGripContext(fixture.ViewModel, "block-b");
            await DispatchContextAsync(context, "ToggleActions");
            TestUiThread.Run(() => fixture.ViewModel.EditText("t_block-b", "Newer text to preserve"));
            var before = fixture.Adapter.Snapshot;
            await DispatchContextAsync(context, "ToggleDesignMenu");
            await DispatchContextAsync(context, "TurnInto:heading");
            Assert.Equal(before, fixture.Adapter.Snapshot);
            Assert.Equal("paragraph", Blocks(fixture.Adapter)[1].Kind);
            Assert.Equal("Newer text to preserve", Blocks(fixture.Adapter)[1].Text);
            Assert.False(context.IsExpanded);
            Assert.True(fixture.ViewModel.TryGetValue("StatusText", out var status));
            Assert.Contains("Block conversion refused", status?.ToString() ?? string.Empty);
        });
    }

    [Theory]
    [InlineData("ToggleMoveMenu")]
    [InlineData("ToggleDesignMenu")]
    public async Task Opening_a_submenu_never_renews_a_stale_ticket(string submenu)
    {
        await WithMenuAsync(async fixture =>
        {
            var context = new BlockGripCui.BlockGripContext(fixture.ViewModel, "block-b");
            await DispatchContextAsync(context, "ToggleActions");
            Assert.True(context.TryGetValue("DuplicateRequest", out var original));
            TestUiThread.Run(() => fixture.ViewModel.EditText("t_block-b", "Newer unsaved text"));
            await DispatchContextAsync(context, submenu);
            Assert.True(context.TryGetValue("DuplicateRequest", out var retained));
            Assert.Same(original, retained);
            await DispatchContextAsync(context, "ApplyBlockAction", retained);
            Assert.Equal(3, Blocks(fixture.Adapter).Count);
            Assert.Equal("Newer unsaved text", Blocks(fixture.Adapter)[1].Text);
            Assert.True(fixture.ViewModel.TryGetValue("StatusText", out var status));
            Assert.Contains("Block action refused", status?.ToString() ?? string.Empty);

            await DispatchContextAsync(context, "ToggleActions");
            Assert.True(context.TryGetValue("DuplicateRequest", out var queued));
            TestUiThread.Run(context.Close);
            await DispatchContextAsync(context, "ApplyBlockAction", queued);
            Assert.Equal(3, Blocks(fixture.Adapter).Count);
        });
    }

    private sealed record MenuFixture(Window Window, Button Trigger, BlockGripCui.BlockActionsHost Host,
        BoardsViewModel ViewModel, ContractSessionAdapter Adapter);

    private static async Task WithMenuAsync(Func<MenuFixture, Task> check, bool liveRebuild = false,
        HavenRichBlockKind kind = HavenRichBlockKind.Paragraph)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-menu-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "menu.9to1board");
            await SeedAsync(store, path, kind: kind);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var fixture = TestUiThread.Run(() =>
            {
                var vm = new BoardsViewModel(adapter);
                var panel = new StackPanel { Width = 600, Margin = new Thickness(50) };
                BlockRenderer.Rebuild(panel, vm, []);
                if (liveRebuild)
                    vm.RebuildRequested += () => Dispatcher.UIThread.Post(() => BlockRenderer.Rebuild(panel, vm, []));
                var window = new Window { Width = 760, Height = 600, Content = panel };
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var targetId = kind == HavenRichBlockKind.Paragraph ? "block-b" : "block-b:item-one";
                var row = panel.Children.OfType<Border>().Single(item =>
                    item.Tag is BlockReorderRow info && info.BlockId == targetId);
                var trigger = row.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "BlockGripToggle");
                return new MenuFixture(window, trigger, Assert.IsType<BlockGripCui.BlockActionsHost>(BlockGripCui.HostFor(trigger)), vm, adapter);
            });
            try { await check(fixture); }
            finally { TestUiThread.Run(() => { fixture.Window.Close(); fixture.ViewModel.Dispose(); }); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static MenuItem MenuRow(MenuFixture fixture, string name) =>
        AuthoredControls(fixture.Host.Menu).OfType<MenuItem>().Single(row => row.Name == name);

    private static void SendKey(MenuFixture fixture, PhysicalKey key)
    {
        fixture.Window.KeyPressQwerty(key, RawInputModifiers.None);
        fixture.Window.KeyReleaseQwerty(key, RawInputModifiers.None);
    }

    private static async Task DispatchContextAsync(BlockGripCui.BlockGripContext context, string command, object? parameter = null)
    {
        var action = TestUiThread.Run(() => context.DispatchAsync(command, parameter).AsTask());
        await DrainUiAsync(action);
    }

    private static async Task OpenMenuAsync(MenuFixture fixture)
    {
        TestUiThread.Run(() => fixture.Trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        await DrainMenuAsync(fixture);
        TestUiThread.Run(() =>
        {
            fixture.Window.UpdateLayout();
            Assert.True(fixture.Host.Popup.IsOpen);
            Assert.True(fixture.Host.Popup.IsUsingOverlayLayer);
            Assert.True(MenuRow(fixture, "BlockDuplicate").IsEffectivelyVisible);
        });
    }

    private static async Task DrainMenuAsync(MenuFixture fixture)
    {
        var actions = TestUiThread.Run(() => fixture.Host.WhenActionsIdleAsync());
        await DrainUiAsync(actions);
        TestUiThread.Run(() => { Dispatcher.UIThread.RunJobs(); fixture.Window.UpdateLayout(); });
    }

    private static async Task DrainUiAsync(Task actions)
    {
        for (var attempt = 0; attempt < 200 && !actions.IsCompleted; attempt++)
        {
            TestUiThread.Run(() => Dispatcher.UIThread.RunJobs());
            await Task.Delay(10);
        }
        await actions.WaitAsync(TimeSpan.FromSeconds(5));
        TestUiThread.Run(() => Dispatcher.UIThread.RunJobs());
    }

    [Fact]
    public async Task Grip_refreshes_after_typing_but_an_open_menu_cannot_apply_a_stale_ticket()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-grip-edit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "grip.9to1board");
            await SeedAsync(store, path);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var vm = new BoardsViewModel(adapter);
            var grip = new BlockGripCui.BlockGripContext(vm, "block-b");
            vm.EditText("t_block-b", "Typed before opening");
            await grip.DispatchAsync("ToggleActions", null);
            Assert.True(grip.TryGetValue("DuplicateRequest", out var request));
            await grip.DispatchAsync("ApplyBlockAction", request);
            Assert.Equal(4, Blocks(adapter).Count);
            Assert.Equal(2, Blocks(adapter).Count(block => block.Text == "Typed before opening"));

            var guarded = new BlockGripCui.BlockGripContext(vm, "block-a");
            await guarded.DispatchAsync("ToggleActions", null);
            Assert.True(guarded.TryGetValue("DeleteRequest", out var stale));
            vm.EditText("t_block-a", "Changed with menu open");
            await guarded.DispatchAsync("ApplyBlockAction", stale);
            Assert.Contains(Blocks(adapter), block => block.Id == "block-a");
            Assert.Equal(4, Blocks(adapter).Count);
            await guarded.DispatchAsync("ToggleActions", null);
            Assert.True(guarded.TryGetValue("DeleteRequest", out var fresh));
            await guarded.DispatchAsync("ApplyBlockAction", fresh);
            Assert.DoesNotContain(Blocks(adapter), block => block.Id == "block-a");

            var oldPageGrip = new BlockGripCui.BlockGripContext(vm, "block-b");
            var section = adapter.Document.Sections.Single();
            var otherPage = new RichBoardPage { Id = "other-page",
                Blocks = [new RichBoardBlock { Id = "other-block", Text = "Other page" }] };
            section.Pages.Add(otherPage);
            vm.SelectPage(section.Id, otherPage.Id);
            await oldPageGrip.DispatchAsync("ToggleActions", null);
            Assert.True(oldPageGrip.TryGetValue("CanManage", out var canManage));
            Assert.Equal(false, canManage);
            Assert.True(oldPageGrip.TryGetValue("DeleteRequest", out var refused));
            Assert.Null(refused);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Canonical_commands_move_duplicate_delete_with_same_history_and_save_reopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-block-actions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "owner.9to1board");
            using var store = new JsonFileHavenBoardStore(root); await SeedAsync(store, path);
            string copiedId;
            var documentId = (await store.LoadDocumentAtPathAsync(path))!.DocumentId;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var vm = new BoardsViewModel(adapter); vm.FocusBlock("block-b");
                var original = adapter.Snapshot; Assert.False(adapter.CanUndo);
                Assert.True(Flag(vm, "CanMoveBlockUp")); Assert.True(Flag(vm, "CanMoveBlockDown"));
                await vm.DispatchAsync("BlockMoveUp", null);
                Assert.Equal(new[] { "block-b", "block-a", "block-c" }, Blocks(adapter).Select(block => block.Id));
                Assert.Equal("block-b", vm.SelectedBlockId); Assert.False(Flag(vm, "CanMoveBlockUp"));
                Assert.True(await adapter.UndoAsync()); Assert.Equal(original, adapter.Snapshot); Assert.False(adapter.CanUndo);
                vm.RefreshAfterEdit();
                await vm.DispatchAsync("BlockMoveDown", null);
                Assert.Equal(new[] { "block-a", "block-c", "block-b" }, Blocks(adapter).Select(block => block.Id));
                Assert.False(Flag(vm, "CanMoveBlockDown"));
                Assert.True(await adapter.UndoAsync()); Assert.Equal(original, adapter.Snapshot); Assert.False(adapter.CanUndo);
                vm.RefreshAfterEdit();
                await vm.DispatchAsync("BlockDuplicate", null);
                copiedId = vm.SelectedBlockId;
                Assert.NotEqual("block-b", copiedId);
                Assert.Equal(new[] { "block-a", "block-b", copiedId, "block-c" }, Blocks(adapter).Select(block => block.Id));
                Assert.Equal("AlphaBeta", Blocks(adapter)[2].Text);
                Assert.Equal(new[] { false, false, false, false, false, true, true, true, true },
                    Blocks(adapter)[2].Runs.SelectMany(run => Enumerable.Repeat(run.Bold, run.Text.Length)));
                Assert.True(await adapter.UndoAsync()); Assert.Equal(original, adapter.Snapshot); Assert.False(adapter.CanUndo);
                Assert.True(await adapter.RedoAsync());
                vm.RefreshAfterEdit(); vm.FocusBlock("block-a");
                await vm.DispatchAsync("BlockDelete", null);
                Assert.Equal(new[] { "block-b", copiedId, "block-c" }, Blocks(adapter).Select(block => block.Id));
                Assert.Equal("block-b", vm.SelectedBlockId);
                await vm.DispatchAsync("SaveBoard", null);
            }
            await using (var fresh = await ContractSessionAdapter.OpenAsync(store, path))
            {
                Assert.Equal(new[] { "block-b", copiedId, "block-c" }, Blocks(fresh).Select(block => block.Id));
                Assert.Equal("AlphaBeta", Blocks(fresh)[1].Text);
                Assert.Equal(HavenRichBaseline.Superscript, Blocks(fresh)[1].Runs[1].Baseline);
                Assert.Equal("Georgia", Blocks(fresh)[1].Runs[1].FontFamily);
                Assert.Equal(documentId, (await store.LoadDocumentAtPathAsync(path))!.DocumentId);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Duplication_preserves_full_contract_payload_and_refreshes_only_owned_nested_identities()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-block-payload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "payload.9to1board");
            await using (var owner = await RichBoardSession.CreateNewAsync(store, "Payload owner"))
            {
                await owner.MutateAsync(rich =>
                {
                    var page = rich.Sections.Single().Pages.Single(); page.Blocks.Clear();
                    var block = HavenRichBlock.Paragraph("original payload"); block.Id = "block-payload";
                    block.StyleId = "quote"; block.Alignment = HavenRichAlignment.Right; block.LineSpacing = 1.7;
                    block.SpaceBefore = 12; block.SpaceAfter = 9; block.IndentLevel = 2;
                    block.Runs = [new() { Text = block.PlainText, Bold = true, Italic = true, Underline = true,
                        StrikeThrough = true, Baseline = HavenRichBaseline.Subscript, FontFamily = "Georgia", FontSize = 19,
                        Foreground = "#FF112233", Background = "#FF445566" }];
                    // Optional contract fields are preserved even when the working
                    // projection for this block kind does not expose their UI.
                    block.Items = [new() { Id = "item-original", Text = "keep item", Checked = true, Level = 2, Underline = true }];
                    block.Table = HavenRichTable.Create(1, 1); block.Table.Rows[0].Cells[0].Text = "keep cell";
                    block.Table.Rows[0].Cells[0].Runs = [new() { Text = "keep cell", Bold = true }];
                    block.Table.Background = "#FF112233"; block.Table.ColumnWidths = [140]; block.Table.RowHeights = [42];
                    block.Image = new() { Id = "img-original", DisplayName = "original image", MediaType = "image/png",
                        DataBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII=",
                        LocalReference = "sidecar:abc123", Width = 320, AltText = "preserved alt" };
                    block.Graph = new() { Expressions = [new() { Id = "expr-original", Text = "x*x", Color = "#FF334455" }],
                        Points = [new() { X = 3, Y = 9, Label = "preserved point" }], XLabel = "original x" };
                    block.Attachment = new() { Id = "att-original", DisplayName = "original.bin", MediaType = "application/octet-stream",
                        DataBase64 = Convert.ToBase64String(Enumerable.Repeat((byte)42, 123).ToArray()),
                        LocalReference = "sidecar:def456", SizeBytes = 123 };
                    page.Blocks.Add(block);
                });
                var original = JsonSerializer.Serialize(owner.Rich.Sections.Single().Pages.Single().Blocks.Single());
                var pageId = owner.Rich.Sections.Single().Pages.Single().Id;
                string? copiedId = null;
                await owner.MutateAsync(rich => copiedId = HavenRichNotesOps.DuplicateBlock(rich, pageId, "block-payload").Id);
                var blocks = owner.Rich.Sections.Single().Pages.Single().Blocks;
                Assert.Equal(2, blocks.Count); var source = blocks[0]; var copy = blocks[1];
                Assert.Equal(original, JsonSerializer.Serialize(source)); Assert.NotEqual(source.Id, copiedId);
                Assert.NotEqual(source.Items[0].Id, copy.Items[0].Id);
                Assert.NotEqual(source.Table!.Rows[0].Cells[0].Id, copy.Table!.Rows[0].Cells[0].Id);
                Assert.NotEqual(source.Image!.Id, copy.Image!.Id);
                Assert.NotEqual(source.Graph!.GraphId, copy.Graph!.GraphId);
                Assert.NotEqual(source.Graph!.Expressions[0].Id, copy.Graph!.Expressions[0].Id);
                Assert.NotEqual(source.Attachment!.Id, copy.Attachment!.Id);
                Assert.Equal("quote", copy.StyleId); Assert.Equal("sidecar:abc123", copy.Image.LocalReference);
                Assert.Equal("sidecar:def456", copy.Attachment.LocalReference);
                // A detached comparison restores only identities/order; every
                // other serialized field must be exactly the original payload.
                var comparable = JsonSerializer.Deserialize<HavenRichBlock>(JsonSerializer.Serialize(copy))!;
                comparable.Id = source.Id; comparable.Order = source.Order;
                comparable.Items[0].Id = source.Items[0].Id;
                comparable.Table!.Rows[0].Cells[0].Id = source.Table.Rows[0].Cells[0].Id;
                comparable.Image!.Id = source.Image.Id;
                comparable.Graph!.GraphId = source.Graph.GraphId;
                comparable.Graph!.Expressions[0].Id = source.Graph.Expressions[0].Id;
                comparable.Attachment!.Id = source.Attachment.Id;
                Assert.Equal(original, JsonSerializer.Serialize(comparable));
                copy.Runs[0].Bold = false; copy.Table.Rows[0].Cells[0].Text = "copy only";
                Assert.True(source.Runs[0].Bold); Assert.Equal("keep cell", source.Table.Rows[0].Cells[0].Text);
                await owner.SaveAsAsync(path);
            }
            var saved = (await store.LoadDocumentAtPathAsync(path))!.RichNotes!;
            var reopened = saved.Sections.Single().Pages.Single().Blocks;
            Assert.Equal(2, reopened.Count); Assert.Equal("original payload", reopened[1].PlainText);
            Assert.Equal("original.bin", reopened[1].Attachment!.DisplayName);
            Assert.Equal("sidecar:def456", reopened[1].Attachment!.LocalReference);
            Assert.Equal("preserved point", reopened[1].Graph!.Points[0].Label);
            Assert.Equal(123, Convert.FromBase64String(reopened[1].Attachment!.DataBase64!).Length);
            Assert.All(Convert.FromBase64String(reopened[1].Attachment!.DataBase64!), value => Assert.Equal((byte)42, value));
            Assert.NotEmpty(Convert.FromBase64String(reopened[1].Image!.DataBase64!));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Boundary_denied_stale_and_composite_targets_create_no_action_history_or_disk_change()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-block-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "guard.9to1board"); using var store = new JsonFileHavenBoardStore(root);
            await SeedAsync(store, path); await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var page = adapter.Document.Sections.Single().Pages.Single(); var before = adapter.Snapshot;
            var disk = await File.ReadAllBytesAsync(path);
            var first = adapter.CaptureBlockAction(page.Id, "block-a")!;
            var last = adapter.CaptureBlockAction(page.Id, "block-c")!;
            Assert.Equal("AtBoundary", (await adapter.ApplyBlockActionAsync(first, BoardBlockAction.MoveUp, () => true)).Code);
            Assert.Equal("AtBoundary", (await adapter.ApplyBlockActionAsync(last, BoardBlockAction.MoveDown, () => true)).Code);
            var current = adapter.CaptureBlockAction(page.Id, "block-b")!;
            Assert.False((await adapter.ApplyBlockActionAsync(current, BoardBlockAction.Delete, () => false)).Applied);
            Assert.False((await adapter.ApplyBlockActionAsync(current, (BoardBlockAction)999, () => true)).Applied);
            Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo); Assert.False(adapter.CanRedo);
            Assert.Equal(disk, await File.ReadAllBytesAsync(path));
            Assert.Null(adapter.CaptureBlockAction(page.Id, "block-b:item-pretend"));
            current = adapter.CaptureBlockAction(page.Id, "block-b")!;
            Assert.True((await adapter.ApplyBlockActionAsync(current, BoardBlockAction.Duplicate, () => true)).Applied);
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot);
            var historyAfterUndo = adapter.CanUndo;
            Assert.False((await adapter.ApplyBlockActionAsync(current, BoardBlockAction.Delete, () => true)).Applied);
            Assert.Equal(historyAfterUndo, adapter.CanUndo); // equal-content undo does not revive tickets
            current = adapter.CaptureBlockAction(page.Id, "block-b")!;
            Blocks(adapter)[1].Text = "newer unsaved edit";
            var historyBeforeTextStaleAction = adapter.CanUndo;
            Assert.False((await adapter.ApplyBlockActionAsync(current, BoardBlockAction.Delete, () => true)).Applied);
            Assert.Equal(historyBeforeTextStaleAction, adapter.CanUndo);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Typed_block_move_uses_one_undo_step_and_cancelled_move_is_inert()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-block-pointer-move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "pointer.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await SeedAsync(store, path);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var pageId = adapter.Document.Sections.Single().Pages.Single().Id;
            var original = adapter.Snapshot;

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var cancellationTarget = adapter.CaptureBlockAction(pageId, "block-b")!;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await adapter.ApplyBlockMoveAsync(cancellationTarget, 2, () => true, cancelled.Token));
            Assert.Equal(original, adapter.Snapshot);
            Assert.False(adapter.CanUndo);

            var deniedTarget = adapter.CaptureBlockAction(pageId, "block-b")!;
            var denied = await adapter.ApplyBlockMoveAsync(deniedTarget, 2, () => false);
            Assert.False(denied.Applied);
            Assert.Equal(original, adapter.Snapshot);
            Assert.False(adapter.CanUndo);

            var target = adapter.CaptureBlockAction(pageId, "block-a")!;
            var moved = await adapter.ApplyBlockMoveAsync(target, 2, () => true);
            Assert.True(moved.Applied);
            Assert.Equal(new[] { "block-b", "block-c", "block-a" }, Blocks(adapter).Select(block => block.Id));
            Assert.True(adapter.CanUndo);
            Assert.True(await adapter.UndoAsync());
            Assert.Equal(original, adapter.Snapshot);
            Assert.False(adapter.CanUndo);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static bool Flag(BoardsViewModel model, string name) => model.TryGetValue(name, out var value) && value is true;
    private static List<RichBoardBlock> Blocks(ContractSessionAdapter adapter) => adapter.Document.Sections.Single().Pages.Single().Blocks;
    private static async Task SeedAsync(JsonFileHavenBoardStore store, string path, bool freePlacement = false,
        HavenRichBlockKind kind = HavenRichBlockKind.Paragraph)
    {
        await using var owner = await RichBoardSession.CreateNewAsync(store, "Same block owner");
        await owner.MutateAsync(rich =>
        {
            var page = rich.Sections.Single().Pages.Single(); page.Blocks.Clear();
            if (freePlacement) page.LayoutMode = HavenRichPageLayoutModes.Unlocked;
            page.Blocks.Add(new() { Id = "block-a", PlainText = "first", Order = 0 });
            page.Blocks.Add(new() { Id = "block-b", PlainText = "AlphaBeta", Order = 1, Runs =
                [new() { Text = "Alpha" }, new() { Text = "Beta", Bold = true, Baseline = HavenRichBaseline.Superscript, FontFamily = "Georgia" }] });
            if (kind != HavenRichBlockKind.Paragraph)
            {
                page.Blocks[1].Kind = kind;
                page.Blocks[1].Items = [new() { Id = "item-one", Text = "AlphaBeta", Checked = true },
                    new() { Id = "item-two", Text = "second item" }];
            }
            page.Blocks.Add(new() { Id = "block-c", PlainText = "last", Order = 2 });
        });
        await owner.SaveAsAsync(path);
    }
}
