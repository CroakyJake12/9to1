using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Chat;
using Haven.UI;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

/// <summary>
/// Lane-10 native-mount regression for the generic escape floating portal. The shared CUI composer is
/// mounted through the genuine <see cref="ChatHavenScene"/> + <see cref="HavenSceneControl"/> native
/// registrar path (the real product surface). The Aa floating toolbar's resolved rect escapes the
/// clipped composer leaf, so the portal must paint/hit-test it in the owning window viewport, outside
/// the leaf, with all six commands reachable and input/draft/selection preserved. Command dispatch is
/// asserted by observing the real scene-side shared-input transformation (the native scene source does
/// not surface a last-command probe; the authored format action mutates the live selection). Pointer
/// events are sent to the ACTUAL owning top level at the translated live bounds — never synthesize
/// command invocation or RaiseEvent. Execution is slot-gated / NOT RUN here.
/// </summary>
public sealed class NativeComposerFloatingPortalTests
{
    private sealed class RecordingRegistrar : IHavenNativeControlRegistrar
    {
        private readonly ChatGenUiNativeControlResolver _mapping = new();
        public bool TryCreate(HavenElement element, out Control? control) => _mapping.TryCreate(element, out control);
        public void Register(HavenElement element, Control control) => _mapping.Register(element, control);
        public void Unregister(HavenElement element, Control control) => _mapping.Unregister(element, control);
        public bool IsRegistered(HavenElement element) => _mapping.TryCreate(element, out _);
    }

    [AvaloniaFact]
    public async Task Native_mount_floating_toolbar_renders_outside_composer_leaf_all_commands_reachable_draft_selection_preserved()
    {
        var registrar = new RecordingRegistrar();
        var scene = new ChatHavenScene(registrar);
        var window = new Window
        {
            Width = 1000,
            Height = 450,
            Content = new HavenSceneControl(new HavenAvaloniaImageResolver(), registrar) { Root = scene.Root }
        };
        Exception? testFailure = null;
        window.Show();
        try
        {
            await scene.ComposerInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            var view = scene.ComposerView;
            var box = FindInput(view);
            Assert.NotNull(box);
            // Pointer focus can move the caret and expand the composer. Establish it BEFORE setting
            // the deliberate selection and measuring the baseline used by the open-toolbar assertions.
            Click(box!);
            await view.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            Assert.True(box!.IsFocused, "the actual native input must own focus before setup");
            view.Input.Text = "draft";
            view.Input.SetSelection(0, 2);
            Assert.Equal("draft", box!.Text);
            Assert.Equal(2, view.Input.SelectionLength);

            // Closed toolbar renders nothing (no portal, no bar anywhere).
            Assert.Null(FindToolbarAnywhere(window, view));

            var surface = FindContainer(view, "composer-surface");
            Assert.NotNull(surface);
            var surfaceDesiredBefore = surface!.DesiredSize;

            // Open via a real pointer click on the visible Aa control in the composer leaf.
            var aa = FindButton(view, "format-button");
            Assert.NotNull(aa);
            Assert.True(aa!.IsEffectivelyVisible, "Aa must be effectively visible after real input focus");
            Click(aa);
            await view.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            // The toolbar is discoverable through the portal (window/overlay path) and effectively
            // visible with positive bounds inside the owning viewport; input/draft/selection survived the
            // open round-trip intact before any command dispatch.
            var toolbar = FindToolbarAnywhere(window, view);
            Assert.NotNull(toolbar);
            Assert.True(toolbar!.IsEffectivelyVisible, "the native Aa floating toolbar must be effectively visible");
            Assert.True(toolbar.Bounds.Width > 0 && toolbar.Bounds.Height > 0, "the floating toolbar must have positive bounds");
            var windowClient = new Rect(0, 0, window.Width, window.Height);
            var toolbarWin = WindowBounds(toolbar, window);
            Assert.True(windowClient.Contains(toolbarWin), "floating toolbar must stay inside the owning viewport");
            Assert.Equal("draft", box.Text);
            Assert.Same(box, FindInput(view));
            Assert.Equal("dr", view.Input.SelectedText);
            Assert.Equal(2, view.Input.SelectionLength);

            // It is NOT a visual descendant of the composer leaf: it escaped the clipped native host.
            Assert.False(surface.IsVisualAncestorOf(toolbar), "floating toolbar must render outside the composer leaf");

            // Out-of-flow: opening the toolbar must not inflate the composer surface desired size.
            Assert.Equal(surfaceDesiredBefore, surface.DesiredSize);

            // Exclusion from the protected composer input/chips/actions/footer regions.
            Assert.False(toolbarWin.Intersects(WindowBounds(box, window)), "floating toolbar must not cover the composer input");
            var chipsRow = FindContainer(view, "chips-row");
            if (chipsRow is not null)
                Assert.False(toolbarWin.Intersects(WindowBounds(chipsRow, window)), "floating toolbar must not cover the chips row");
            Assert.False(toolbarWin.Intersects(WindowBounds(FindButton(view, "send-button")!, window)), "floating toolbar must not cover the send action");

            // All six formatting commands are present, visible and dispatch the genuine authored actions.
            // Each format wraps the current selection, so we observe the real shared-input transformation
            // (the native scene source exposes no last-command probe).
            var commandButtons = toolbar.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Equal(6, commandButtons.Length);
            var expected = new (string Command, string Wrapped)[]
            {
                ("Bold", "**x**"),
                ("Italic", "_x_"),
                ("Code", "`x`"),
                ("Link", "[x](url)"),
                ("Quote", "> x"),
                ("Bullet", "- x"),
            };
            for (var i = 0; i < commandButtons.Length; i++)
            {
                Assert.True(commandButtons[i].IsEffectivelyVisible, $"command {i} must remain visible");
                var buttonWin = WindowBounds(commandButtons[i], window);
                Assert.True(windowClient.Contains(buttonWin), $"command {i} must stay inside the owning viewport");
                view.Input.Text = "x";
                view.Input.SetSelection(0, 1);
                Assert.Equal("x", view.Input.SelectedText);
                // Real pointer click on the ACTUAL owning top level at live translated bounds.
                Click(commandButtons[i]);
                await view.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(expected[i].Wrapped, view.Input.Text);
            }

            // The composer input control identity persists through the native open + command journey.
            Assert.Same(box, FindInput(view));
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            try { await scene.JoinComposerCloseAndReleaseAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { window.Close(); }
            if (testFailure is not null) throw testFailure;
        }
    }

    /// <summary>Send a real pointer move/down/up to the ACTUAL owning top level of the target (the
    /// window for in-tree content, the portal's popup root for portaled content) at the translated live
    /// bounds. No RaiseEvent / direct service invocation / synthetic action routing.</summary>
    private static void Click(Control target)
    {
        var tl = TopLevel.GetTopLevel(target);
        Assert.NotNull(tl);
        var origin = target.TranslatePoint(default, tl!) ?? default;
        var center = new Point(origin.X + target.Bounds.Width / 2, origin.Y + target.Bounds.Height / 2);
        tl!.MouseMove(center);
        tl.MouseDown(center, MouseButton.Left);
        tl.MouseUp(center, MouseButton.Left);
        Pump();
    }

    /// <summary>Translate a control's bounds into the owning window's client coordinate space. Portaled
    /// content lives in the portal's popup top level; the actual popup-root/control position is mapped
    /// back through the owning window's client transform (PointToScreen/PointToClient), which respects
    /// scaling and crosses top-level roots. Assert the translation succeeds — never fall back to derived
    /// origins.</summary>
    private static Rect WindowBounds(Control control, Window window)
    {
        var tl = TopLevel.GetTopLevel(control);
        Assert.NotNull(tl);
        var tlPoint = control.TranslatePoint(default, tl!)
            ?? throw new InvalidOperationException("WindowBounds: control translation to its top level failed");
        var screen = tl.PointToScreen(tlPoint);
        Assert.True(double.IsFinite(screen.X) && double.IsFinite(screen.Y), "WindowBounds: non-finite screen origin");
        var clientOrigin = window.PointToClient(screen);
        return new Rect(clientOrigin, new Size(control.Bounds.Width, control.Bounds.Height));
    }

    private static Control? FindToolbarAnywhere(Window window, ContentControl view)
    {
        const string id = "format-toolbar";
        foreach (var c in window.GetVisualDescendants().OfType<Control>())
            if (AutomationProperties.GetAutomationId(c) == id) return c;
        foreach (var node in view.GetLogicalDescendants())
            if (node is Control c && AutomationProperties.GetAutomationId(c) == id) return c;
        foreach (var c in view.GetVisualDescendants().OfType<Control>())
            if (AutomationProperties.GetAutomationId(c) == id) return c;
        return null;
    }

    private static Control? FindContainer(ContentControl root, string automationId) =>
        root.GetVisualDescendants().OfType<Control>().SingleOrDefault(control =>
            AutomationProperties.GetAutomationId(control) == automationId);

    private static Button? FindButton(ContentControl root, string automationId) =>
        root.GetVisualDescendants().OfType<Button>().SingleOrDefault(b =>
            AutomationProperties.GetAutomationId(b) == automationId);

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
}
