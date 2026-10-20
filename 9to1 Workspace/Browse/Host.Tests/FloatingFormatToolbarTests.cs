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
/// CUI composer. The Aa toggle must reveal a same-scene overlay sibling (never a stack row, never a
/// native Popup), anchored within the composer surface, without moving or resizing the input or the
/// controls-row baseline, and must preserve input/draft/selection identity across the toggle.
/// These tests exercise the REAL authored ChatComposerCuiView, so they require the recompiled
/// packaged CUI bundle for the new overlay composition (execution is slot-gated / NOT RUN here).
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
    public async Task Aa_toggles_same_scene_overlay_toolbar_out_of_flow_without_shifting_input_or_controls()
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

            // The composer surface is an overlay; the body is a vertical stack inside it.
            var surface = FindContainer(view, "composer-surface");
            var body = FindContainer(view, "composer-body");
            Assert.NotNull(surface);
            Assert.NotNull(body);
            Assert.IsType<CuiContainerControl>(surface);
            Assert.Equal("Overlay", ((CuiContainerControl)surface!).LayoutType);
            // The input lives in the normal-flow body.
            Assert.Contains(box, body!.GetVisualDescendants());

            var inputBefore = box.Bounds;
            var sendBefore = FindButton(view, "send-button")!.Bounds;

            // Closed toolbar renders nothing (cell removed from the visual tree).
            Assert.Null(FindContainer(view, "format-toolbar"));

            var format = FindButton(view, "format-button");
            Assert.NotNull(format);
            format!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.CaptureOriginalActionSettlement(); Pump();

            var toolbar = FindContainer(view, "format-toolbar");
            Assert.NotNull(toolbar);
            Assert.True(toolbar.IsVisible, "Aa must open the floating toolbar");
            Assert.True(toolbar.Bounds.Width > 0 && toolbar.Bounds.Height > 0);

            // Out-of-flow: the toolbar is a DIRECT sibling overlay child of composer-surface, NOT a
            // descendant of composer-body, and it is anchored inside the surface (aligned top/left).
            Assert.False(toolbar.GetVisualAncestors().Contains(body), "floating toolbar must not live in the body flow");
            Assert.Contains(surface, toolbar.GetVisualAncestors());
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, toolbar.HorizontalAlignment);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, toolbar.VerticalAlignment);

            // It overlaps the body region (it floats above content) rather than occupying a row.
            var bodyBounds = body.Bounds;
            Assert.True(toolbar.Bounds.Intersects(new Rect(bodyBounds.X, bodyBounds.Y, bodyBounds.Width, bodyBounds.Height)),
                "floating toolbar must overlay the body region");

            // Opening the toolbar must NOT move/resize the input or the controls-row baseline.
            Assert.Equal(inputBefore, box.Bounds);
            Assert.Equal(sendBefore, FindButton(view, "send-button")!.Bounds);

            // B then I dispatch genuine format actions and keep the toolbar open and unchanged.
            var bold = ToolbarButton(toolbar, 0);
            var italic = ToolbarButton(toolbar, 1);
            Assert.NotNull(bold);
            Assert.NotNull(italic);
            bold!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.CaptureOriginalActionSettlement();
            Assert.Equal("Bold", source.LastFormatCommand);
            Assert.True(toolbar.IsVisible);
            italic!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.CaptureOriginalActionSettlement();
            Assert.Equal("Italic", source.LastFormatCommand);
            Assert.True(toolbar.IsVisible);
            Assert.Equal(2, source.FormatCount);

            // Input/draft/selection identity is preserved through the open round trip.
            Assert.Equal("draft", box.Text);
            Assert.Same(box, FindInput(view));
            Assert.Equal(2, view.Input.SelectionLength);
            Assert.Equal("dr", view.Input.SelectedText);

            // Aa closes the toolbar; the SAME input/draft/selection survive.
            format.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
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
            var format = FindButton(view, "format-button");
            Assert.NotNull(format);
            format!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.CaptureOriginalActionSettlement(); Pump();

            var toolbar = FindContainer(view, "format-toolbar");
            Assert.NotNull(toolbar);
            var inputBefore = box.Bounds;

            // Multiline draft grows the input; the floating toolbar must not change it.
            view.Input.Text = string.Join("\n", Enumerable.Repeat("a line", 8));
            PumpLayout(window);
            Assert.True(box.Bounds.Height > inputBefore.Height);
            Assert.True(toolbar.IsVisible);
            Assert.True(toolbar.Bounds.Width > 0 && toolbar.Bounds.Height > 0);
            Assert.False(toolbar.GetVisualAncestors().Contains(FindContainer(view, "composer-body")));
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
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
