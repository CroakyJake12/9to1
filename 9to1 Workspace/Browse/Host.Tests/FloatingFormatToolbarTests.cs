using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
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
