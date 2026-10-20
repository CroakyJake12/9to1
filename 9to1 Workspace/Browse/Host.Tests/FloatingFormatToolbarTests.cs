using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Prefabs;
using Haven.Desktop.Views.Pages.Chat;
using System.Threading;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

/// <summary>
/// Lane-3 regression coverage for the floating, out-of-flow formatting toolbar in the single shared
/// CUI composer. The Aa toggle must reveal a same-scene floating layer (never a stack row, never a
/// native Popup) positioned adjacent to the editable input — flipping and clamping so it never covers
/// the first editable line, the chip actions or the controls row — while preserving input/draft/
/// selection identity across the toggle. These tests exercise the REAL authored ChatComposerCuiView
/// and use real headless pointer input; they require the recompiled packaged CUI bundle for the new
/// overlay composition (execution is slot-gated / NOT RUN here).
/// </summary>
public sealed class FloatingFormatToolbarTests
{
    private sealed class Source : IChatComposerSource
    {
        public string Draft { get; set; } = string.Empty;
        public string Placeholder => "Ask Haven anything";
        public bool IsSending { get; set; }
        public bool IsSafetyLocked { get; set; }
        public bool HasAdd { get; set; } = true;
        public bool CanSend { get; set; } = true;
        public bool IsEditing { get; set; }
        public IReadOnlyList<ChatComposerChip> Chips { get; set; } = [];
        public int FormatCount;
        public string? LastFormatCommand;

        public ValueTask Send() => ValueTask.CompletedTask;
        public ValueTask Stop() => ValueTask.CompletedTask;
        public ValueTask Add() => ValueTask.CompletedTask;
        public ValueTask OpenSettings() => ValueTask.CompletedTask;
        public ValueTask CloseSettings() => ValueTask.CompletedTask;
        public ValueTask SetActionMode(string mode) => ValueTask.CompletedTask;
        public ValueTask SetResponseMode(string mode) => ValueTask.CompletedTask;
        public ValueTask InvokeChip(string id) => ValueTask.CompletedTask;
        public ValueTask RemoveChip(string id) => ValueTask.CompletedTask;
        public ValueTask FormatSelection(string command) { LastFormatCommand = command; FormatCount++; return ValueTask.CompletedTask; }
        public ValueTask InsertEmoji(string emoji) => ValueTask.CompletedTask;
        public ValueTask CancelEdit() => ValueTask.CompletedTask;
    }

    [AvaloniaFact]
    public async Task Aa_toggles_same_scene_floating_toolbar_out_of_flow_without_covering_input_or_controls()
    {
        var source = new Source { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            var box = FindInput(view);
            Assert.NotNull(box);
            view.Input.Text = "draft";
            view.Input.SetSelection(0, 2);
            box!.Focus(); Pump();

            // The composer surface is an out-of-flow overlay; the body is a vertical stack inside it.
            var surface = FindContainer(view, "composer-surface");
            var body = FindContainer(view, "composer-body");
            Assert.NotNull(surface);
            Assert.NotNull(body);
            Assert.IsType<CuiContainerControl>(surface);
            Assert.Equal("Overlay Float", ((CuiContainerControl)surface!).LayoutType);
            Assert.Contains(box, body!.GetVisualDescendants());

            // Record baseline in WINDOW space (local bounds miss ancestor shifts).
            var inputBefore = WindowBounds(box, window);
            var sendBefore = WindowBounds(FindButton(view, "send-button")!, window);
            var surfaceDesiredBefore = surface!.DesiredSize;

            // Closed toolbar renders nothing (cell removed from the visual tree).
            Assert.Null(FindContainer(view, "format-toolbar"));

            // Open via a real headless click on the Aa control.
            Click(window, FindButton(view, "format-button")!);
            await view.CaptureOriginalActionSettlement(); PumpLayout(window);

            var toolbar = FindContainer(view, "format-toolbar");
            Assert.NotNull(toolbar);
            Assert.True(toolbar!.IsVisible, "Aa must open the floating toolbar");
            Assert.True(toolbar.Bounds.Width > 0 && toolbar.Bounds.Height > 0);

            // Out-of-flow: the toolbar lives in the floating layer, NOT the body flow.
            var layer = FindContainer(view, "composer-floating-layer");
            Assert.NotNull(layer);
            Assert.False(toolbar.GetVisualAncestors().Contains(body), "floating toolbar must not live in the body flow");
            Assert.Contains(layer, toolbar.GetVisualAncestors());

            // Out-of-flow measure: the surface's desired size is NOT inflated by the toolbar.
            Assert.Equal(surfaceDesiredBefore, surface.DesiredSize);

            // It floats adjacent to the input but never covers editable/action content, and stays
            // inside the window viewport (edge fit).
            var toolbarWin = WindowBounds(toolbar, window);
            var windowClient = new Rect(0, 0, window.Width, window.Height);
            Assert.True(windowClient.Contains(toolbarWin), "floating toolbar must fit within the window viewport");
            Assert.False(toolbarWin.Intersects(inputBefore), "floating toolbar must not cover the editable input");
            Assert.False(toolbarWin.Intersects(sendBefore), "floating toolbar must not cover the send control");

            // Toggling must not shift the input or controls baseline in window space.
            Assert.Equal(inputBefore, WindowBounds(box, window));
            Assert.Equal(sendBefore, WindowBounds(FindButton(view, "send-button")!, window));

            // B then I via real pointer clicks dispatch genuine format actions and keep the toolbar open.
            Click(window, ToolbarButton(toolbar, 0)!);
            await view.CaptureOriginalActionSettlement();
            Assert.Equal("Bold", source.LastFormatCommand);
            Assert.True(toolbar.IsVisible);
            Click(window, ToolbarButton(toolbar, 1)!);
            await view.CaptureOriginalActionSettlement();
            Assert.Equal("Italic", source.LastFormatCommand);
            Assert.Equal(2, source.FormatCount);
            Assert.True(toolbar.IsVisible);

            // Input/draft/selection identity is preserved through the open round trip.
            Assert.Equal("draft", box.Text);
            Assert.Same(box, FindInput(view));
            Assert.Equal(2, view.Input.SelectionLength);
            Assert.Equal("dr", view.Input.SelectedText);

            // Aa closes the toolbar; the SAME input/draft/selection survive.
            Click(window, FindButton(view, "format-button")!);
            PumpLayout(window);
            Assert.False(toolbar.IsVisible);
            Assert.Null(FindContainer(view, "format-toolbar"));
            Assert.Equal("draft", box.Text);
            Assert.Same(box, FindInput(view));
            Assert.Equal(2, view.Input.SelectionLength);
            Assert.Equal("dr", view.Input.SelectedText);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
    }

    [AvaloniaFact]
    public async Task Pointer_click_on_first_line_input_lands_in_editable_text_not_the_toolbar()
    {
        var source = new Source { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            var box = FindInput(view);
            Assert.NotNull(box);
            view.Input.Text = "draft";
            PumpLayout(window);

            // Open the floating toolbar over the input's top edge.
            Click(window, FindButton(view, "format-button")!);
            await view.CaptureOriginalActionSettlement(); PumpLayout(window);
            var toolbar = FindContainer(view, "format-toolbar");
            Assert.NotNull(toolbar);
            var inputRect = WindowBounds(box, window);

            // Real click near the LEFT edge of the first editable line.
            var nearLeft = new Point(inputRect.Left + 4, inputRect.Top + inputRect.Height / 2);
            window.MouseMove(nearLeft); window.MouseDown(nearLeft, MouseButton.Left);
            window.MouseUp(nearLeft, MouseButton.Left); PumpLayout(window);

            // The click reached the editable text (caret placed near the start), not the toolbar:
            // the toolbar must not be covering the input at that point.
            Assert.True(view.Input.CaretIndex <= 1, $"expected caret near start, got {view.Input.CaretIndex}");
            Assert.Equal(0, view.Input.SelectionLength);
            Assert.True(box.IsKeyboardFocusWithin);
            Assert.True(toolbar.IsVisible);
            Assert.False(WindowBounds(toolbar, window).Intersects(inputRect));
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
    }

    [AvaloniaFact]
    public async Task Floating_toolbar_does_not_shift_input_across_narrow_and_multiline_resizes()
    {
        var source = new Source { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 420, Height = 620, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            var box = FindInput(view);
            Assert.NotNull(box);
            box!.Focus(); Pump();
            Click(window, FindButton(view, "format-button")!);
            await view.CaptureOriginalActionSettlement(); PumpLayout(window);

            var toolbar = FindContainer(view, "format-toolbar");
            Assert.NotNull(toolbar);
            var inputBefore = WindowBounds(box, window);

            // Real narrow resize: shrink the window and re-layout.
            window.Width = 300;
            PumpLayout(window);
            Assert.True(toolbar.IsVisible);
            Assert.True(toolbar.Bounds.Width > 0 && toolbar.Bounds.Height > 0);
            var windowClient = new Rect(0, 0, window.Width, window.Height);
            Assert.True(windowClient.Contains(WindowBounds(toolbar, window)), "toolbar must stay inside the narrowed viewport");
            Assert.False(WindowBounds(toolbar, window).Intersects(WindowBounds(box, window)));
            Assert.False(toolbar.GetVisualAncestors().Contains(FindContainer(view, "composer-body")));

            // Out-of-flow measure holds at the narrowed width: closing then reopening the toolbar
            // must not change the surface's desired size.
            var surface = FindContainer(view, "composer-surface")!;
            var desiredClosed = surface.DesiredSize;
            Click(window, FindButton(view, "format-button")!);
            PumpLayout(window);
            Assert.False(FindContainer(view, "format-toolbar") is { IsVisible: true });
            Assert.False(FindContainer(view, "format-toolbar") is not null);
            Assert.Equal(desiredClosed, surface.DesiredSize);
            Click(window, FindButton(view, "format-button")!);
            await view.CaptureOriginalActionSettlement(); PumpLayout(window);
            Assert.Equal(desiredClosed, surface.DesiredSize);

            // Multiline draft grows the input; the floating toolbar must not change it.
            view.Input.Text = string.Join("\n", Enumerable.Repeat("a line", 8));
            PumpLayout(window);
            Assert.True(WindowBounds(box, window).Height > inputBefore.Height);
            Assert.True(toolbar.IsVisible);
            Assert.False(WindowBounds(toolbar, window).Intersects(WindowBounds(box, window)));
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
    }

    [AvaloniaFact]
    public async Task Floating_toolbar_does_not_cover_wrapped_chips_or_controls()
    {
        var source = new Source
        {
            CanSend = true,
            Chips = [new ChatComposerChip("c1", "Documents", false), new ChatComposerChip("c2", "Design", false)]
        };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            var box = FindInput(view);
            Assert.NotNull(box);
            box!.Focus(); Pump();
            Click(window, FindButton(view, "format-button")!);
            await view.CaptureOriginalActionSettlement(); PumpLayout(window);

            var toolbar = FindContainer(view, "format-toolbar");
            Assert.NotNull(toolbar);
            var toolbarWin = WindowBounds(toolbar!, window);
            var windowClient = new Rect(0, 0, window.Width, window.Height);
            Assert.True(windowClient.Contains(toolbarWin), "toolbar must fit inside the window viewport");
            // The bar must never cover the editable input, the send control, or the wrapped chip row.
            Assert.False(toolbarWin.Intersects(WindowBounds(box, window)), "toolbar must not cover the editable input");
            Assert.False(toolbarWin.Intersects(WindowBounds(FindButton(view, "send-button")!, window)), "toolbar must not cover send");
            Assert.False(toolbarWin.Intersects(WindowBounds(FindContainer(view, "chips-row")!, window)), "toolbar must not cover chips");
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
    }

    [AvaloniaFact]
    public async Task Floating_toolbar_all_commands_stay_reachable_at_narrow_width()
    {
        var source = new Source { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 300, Height = 620, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            var box = FindInput(view);
            Assert.NotNull(box);
            box!.Focus(); Pump();
            Click(window, FindButton(view, "format-button")!);
            await view.CaptureOriginalActionSettlement(); PumpLayout(window);

            var toolbar = FindContainer(view, "format-toolbar");
            Assert.NotNull(toolbar);
            var windowClient = new Rect(0, 0, window.Width, window.Height);
            Assert.True(windowClient.Contains(WindowBounds(toolbar!, window)), "toolbar must stay inside the narrowed viewport");
            // Every formatting command must itself be visible and hit-testable, not merely the toolbar
            // outer rectangle: the authored Wrap keeps all six reachable at the narrowed width.
            var commandButtons = toolbar!.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Equal(6, commandButtons.Length);
            foreach (var button in commandButtons)
            {
                var b = WindowBounds(button, window);
                Assert.True(b.Width > 0 && b.Height > 0, "each formatting command must remain visible");
                Assert.True(windowClient.Contains(b), "each formatting command must stay inside the narrowed viewport");
            }
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
    }

    [AvaloniaFact]
    public async Task Wrapped_chips_narrow_toolbar_all_six_commands_hit_test_and_dispatch()
    {
        // A real wrapped-chip case: enough chips to force the row to wrap at the narrow width, plus a
        // narrow window so the floating toolbar itself wraps to multiple lines.
        var source = new Source
        {
            CanSend = true,
            Chips = Enumerable.Range(0, 10)
                .Select(i => new ChatComposerChip($"c{i}", $"Chip {i} label", false)).ToArray()
        };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 300, Height = 620, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            var box = FindInput(view);
            Assert.NotNull(box);
            box!.Focus(); Pump();
            Click(window, FindButton(view, "format-button")!);
            await view.CaptureOriginalActionSettlement(); PumpLayout(window);

            var toolbar = FindContainer(view, "format-toolbar");
            Assert.NotNull(toolbar);
            var windowClient = new Rect(0, 0, window.Width, window.Height);
            var toolbarWin = WindowBounds(toolbar!, window);
            Assert.True(windowClient.Contains(toolbarWin), "toolbar must fit inside the narrowed viewport");
            // No protected body/input/action overlap.
            Assert.False(toolbarWin.Intersects(WindowBounds(box, window)), "toolbar must not cover input");
            Assert.False(toolbarWin.Intersects(WindowBounds(FindButton(view, "send-button")!, window)), "toolbar must not cover send");
            var chipsRow = FindContainer(view, "chips-row")!;
            Assert.True(chipsRow.Bounds.Height > 0, "chips row must be present and wrapping");
            Assert.False(toolbarWin.Intersects(WindowBounds(chipsRow, window)), "toolbar must not cover wrapped chips");

            // Real pointer-click ALL SIX commands after narrow wrapping: each original format action
            // dispatches/settles, is preserved across successive use, and selection is retained.
            var commandButtons = toolbar!.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Equal(6, commandButtons.Length);
            var expected = new[] { "Bold", "Italic", "Code", "Link", "Quote", "Bullet" };
            for (var i = 0; i < commandButtons.Length; i++)
            {
                var b = WindowBounds(commandButtons[i], window);
                Assert.True(b.Width > 0 && b.Height > 0, $"format command {i} must remain visible");
                Assert.True(windowClient.Contains(b), $"format command {i} must stay inside the narrowed viewport");
                Click(window, commandButtons[i]);
                await view.CaptureOriginalActionSettlement();
                Assert.Equal(expected[i], source.LastFormatCommand);
                Assert.Equal(i + 1, source.FormatCount);
                Assert.True(toolbar.IsVisible);
            }

            // Retained selection/identity and focus through the six dispatch clicks.
            Assert.Same(box, FindInput(view));
            Assert.True(box.IsKeyboardFocusWithin);
            Assert.Equal(0, view.Input.SelectionLength);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
    }

    [AvaloniaFact]
    public async Task Live_target_retarget_reanchors_swaps_observers_and_matches_ids_case_sensitively()
    {
        var source = new Source { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            var box = FindInput(view);
            Assert.NotNull(box);
            box!.Focus(); Pump();
            Click(window, FindButton(view, "format-button")!);
            await view.CaptureOriginalActionSettlement(); PumpLayout(window);
            var toolbar = FindContainer(view, "format-toolbar");
            Assert.NotNull(toolbar);

            var layer = FindContainer(view, "composer-floating-layer")!;
            var container = Assert.IsType<CuiContainerControl>(layer);
            Assert.Equal("composer-body", container.AuthoredTargetId);
            var body = FindContainer(view, "composer-body")!;
            var inputColumn = FindContainer(view, "input-column")!;
            Assert.NotNull(body);
            Assert.NotNull(inputColumn);

            var before = WindowBounds(toolbar!, window);
            var windowClient = new Rect(0, 0, window.Width, window.Height);
            Assert.True(windowClient.Contains(before));

            // Live retarget A→B while A remains attached/valid: placement must change immediately and
            // the old anchor subscription retired (no stale anchor retained).
            container.SetAuthoredTarget("input-column");
            PumpLayout(window);
            Assert.Equal("input-column", container.AuthoredTargetId);
            var after = WindowBounds(toolbar!, window);
            Assert.NotEqual(before, after);
            Assert.True(windowClient.Contains(after));
            Assert.False(after.Intersects(WindowBounds(inputColumn, window)));
            Assert.False(after.Intersects(WindowBounds(FindButton(view, "send-button")!, window)));

            // Retarget back to the whole body re-anchors to the original placement.
            container.SetAuthoredTarget("composer-body");
            PumpLayout(window);
            Assert.Equal(before, WindowBounds(toolbar!, window));

            // Canonical CUI Target IDs are ordinal/case-sensitive: a differently-cased id must NOT
            // resolve to the body, so the bar fails closed (hidden) rather than anchoring to the
            // stale body.
            container.SetAuthoredTarget("Composer-Body");
            PumpLayout(window);
            Assert.Equal("Composer-Body", container.AuthoredTargetId);
            var failed = WindowBounds(toolbar!, window);
            Assert.True(failed.Width <= 0 || failed.Height <= 0, "wrong-case target must fail closed (hidden)");
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
    }

    [AvaloniaFact]
    public async Task Short_viewport_degrades_to_truly_hidden_and_recovers_when_space_returns()
    {
        var source = new Source { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            var box = FindInput(view);
            Assert.NotNull(box);
            box!.Focus(); Pump();

            Click(window, FindButton(view, "format-button")!);
            await view.CaptureOriginalActionSettlement(); PumpLayout(window);

            var toolbar = FindContainer(view, "format-toolbar");
            Assert.NotNull(toolbar);
            var tb = toolbar!;
            var layer = FindContainer(view, "composer-floating-layer")!;
            var cell = Assert.IsType<Border>(layer.GetVisualParent());
            Assert.True(tb.IsVisible, "with enough space the toolbar must be visible");
            Assert.True(cell.IsVisible, "the floating cell must be present when placed");

            // Shrink the usable height until no side offers genuinely safe space. The toolbar must be
            // ACTUALLY removed from paint/hit/focus (the panel-private cell wrapper hidden), NOT merely
            // arranged at a zero rect that still lets positive-sized buttons paint near the origin.
            window.Height = 100;
            PumpLayout(window);

            // Genuine suppression: the panel-private wrapper is hidden, so the whole subtree loses
            // paint/hit/focus/tab participation. The authored FormattingOpen state is preserved (the
            // authored toolbar is still "open") — this is NOT a hidden-as-success conversion.
            Assert.False(cell.IsVisible, "no-safe-space must actually remove the floating cell's subtree");
            Assert.True(FindContainer(view, "format-toolbar")!.IsVisible);
            var suppressedButton = ToolbarButton(tb, 0);
            Assert.NotNull(suppressedButton);
            Assert.False(suppressedButton!.IsEffectivelyVisible, "suppressed toolbar buttons must not be effectively visible (no paint/hit/tab/focus)");

            // Recovery: when safe space returns, the SAME retained authored toolbar is placed again.
            window.Height = 700;
            PumpLayout(window);
            Assert.True(cell.IsVisible, "toolbar must recover once safe space returns");
            Assert.True(tb.Bounds.Width > 0 && tb.Bounds.Height > 0);
            Assert.Same(tb, FindContainer(view, "format-toolbar"), "recovery must restore the identical authored toolbar");
            var windowClient = new Rect(0, 0, window.Width, window.Height);
            Assert.True(windowClient.Contains(WindowBounds(tb, window)), "recovered toolbar must stay in the viewport");

            // The recovered toolbar's REAL button dispatches after expansion (not just re-shown).
            Click(window, ToolbarButton(tb, 0)!);
            await view.CaptureOriginalActionSettlement();
            Assert.Equal("Bold", source.LastFormatCommand);
            Assert.Equal(1, source.FormatCount);
            Assert.True(tb.IsVisible);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
    }

    [AvaloniaFact]
    public async Task Production_style_footer_mount_places_within_remaining_space_without_covering_footer()
    {
        var source = new Source { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var footer = new Border
        {
            Height = 48,
            Background = Brushes.Gray,
            Child = new TextBlock { Text = "status bar", VerticalAlignment = VerticalAlignment.Center }
        };
        var grid = new Grid { RowDefinitions = new("*,Auto") };
        grid.Children.Add(view);
        grid.Children.Add(footer);
        Grid.SetRow(footer, 1);
        var window = new Window { Width = 900, Height = 700, Content = grid };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            var box = FindInput(view);
            Assert.NotNull(box);
            box!.Focus(); Pump();

            Click(window, FindButton(view, "format-button")!);
            await view.CaptureOriginalActionSettlement(); PumpLayout(window);

            var toolbar = FindContainer(view, "format-toolbar");
            Assert.NotNull(toolbar);
            Assert.True(toolbar!.IsVisible);
            var toolbarWin = WindowBounds(toolbar, window);
            var windowClient = new Rect(0, 0, window.Width, window.Height);
            var footerWin = WindowBounds(footer, window);
            Assert.True(windowClient.Contains(toolbarWin), "toolbar must fit inside the owner viewport");
            Assert.False(toolbarWin.Intersects(footerWin), "toolbar must not cover the mounted footer");
            Assert.False(toolbarWin.Intersects(WindowBounds(box, window)), "toolbar must not cover the editable input");
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
    }

    private static void Click(TopLevel top, Control target)
    {
        var center = Center(target, top);
        top.MouseMove(center);
        top.MouseDown(center, MouseButton.Left);
        top.MouseUp(center, MouseButton.Left);
        Pump();
    }

    private static Rect WindowBounds(Control control, TopLevel top)
    {
        var origin = control.TranslatePoint(default, top) ?? default;
        return new Rect(origin.X, origin.Y, control.Bounds.Width, control.Bounds.Height);
    }

    private static Point Center(Control control, TopLevel top)
    {
        var rect = WindowBounds(control, top);
        return new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    private static Control? FindContainer(ContentControl root, string automationId) =>
        root.GetVisualDescendants().OfType<Control>().SingleOrDefault(control =>
            AutomationProperties.GetAutomationId(control) == automationId);

    private static Button? FindButton(ContentControl root, string automationId) =>
        root.GetVisualDescendants().OfType<Button>().SingleOrDefault(b =>
            AutomationProperties.GetAutomationId(b) == automationId);

    private static Button? ToolbarButton(Control toolbar, int index) =>
        toolbar.GetVisualDescendants().OfType<Button>().ToArray().ElementAtOrDefault(index);

    private static TextBox? FindInput(ContentControl root) =>
        root.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(box =>
            AutomationProperties.GetAutomationId(box) == "composer-input");

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static void PumpLayout(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
        }
    }

    private static async Task FinishComposerTestAsync(Window window, ChatComposerCuiView view, Exception? testFailure)
    {
        try { await view.CloseAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { window.Close(); }
        if (testFailure is not null) throw testFailure;
    }
}
