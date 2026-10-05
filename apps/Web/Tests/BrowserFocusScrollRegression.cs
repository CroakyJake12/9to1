using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using NineToOne.Web;

await using var session = HeadlessUnitTestSession.StartNew(typeof(FocusScrollApplication));
var failed = await session.Dispatch(() =>
{
    var results = new List<(string Name, bool Passed)>();
    void Check(string name, bool passed)
    {
        results.Add((name, passed));
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}");
    }
    void WithFixture(Action<Window, ScrollViewer, Button, Button, BrowserAccessibilityBridge, string> test)
    {
        var previous = new Button { Content = "Prior native focus", Height = 32 };
        var target = new Button { Content = "Actual offscreen focus target", Height = 32 };
        AutomationProperties.SetAutomationId(target, "focus-target");
        var panel = new StackPanel();
        panel.Children.Add(previous);
        panel.Children.Add(new FocusLayoutBoundary { Height = 568 });
        panel.Children.Add(target);
        panel.Children.Add(new Border { Height = 600 });
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var window = new Window { Content = scroll, Width = 390, Height = 200 };
        var bridge = new BrowserAccessibilityBridge();
        window.Show();
        try
        {
            Settle(window);
            previous.Focus();
            scroll.Offset = default;
            Settle(window);
            bridge.Bind(scroll);
            using var snapshot = JsonDocument.Parse(bridge.ReadSnapshot());
            var id = snapshot.RootElement.GetProperty("elements").EnumerateArray()
                .Single(e => e.GetProperty("automationId").GetString() == "focus-target").GetProperty("id").GetString()!;
            test(window, scroll, previous, target, bridge, id);
        }
        finally { bridge.Clear(); window.Content = null; window.Close(); }
    }
    void Sample(string name, Window window, ScrollViewer scroll, Button target, bool returned, int requests)
    {
        var bounds = ControlAutomationPeer.CreatePeerForElement(target).GetBoundingRectangle();
        Console.WriteLine(FormattableString.Invariant(
            $"NATIVE_FOCUS: {name}; return={returned}; focused={target.IsFocused}; offset={scroll.Offset}; viewport={scroll.Viewport}; target={bounds}; client={window.ClientSize}; requests={requests}"));
    }
    bool Fits(Window window, ScrollViewer scroll, Button target)
    {
        var bounds = ControlAutomationPeer.CreatePeerForElement(target).GetBoundingRectangle();
        var viewport = ControlAutomationPeer.CreatePeerForElement(scroll.GetVisualDescendants()
            .OfType<ScrollContentPresenter>().Single()).GetBoundingRectangle();
        return target.IsFocused && double.IsFinite(bounds.Y) && bounds.Height > 0
            && scroll.Viewport.Height > 0 && viewport.Height > 0
            && bounds.Top >= viewport.Top - 0.1 && bounds.Bottom <= viewport.Bottom + 0.1
            && bounds.Top >= -0.1 && bounds.Bottom <= window.ClientSize.Height + 0.1;
    }

    WithFixture((window, scroll, previous, target, bridge, id) =>
    {
        var before = ControlAutomationPeer.CreatePeerForElement(target).GetBoundingRectangle();
        Check("Real native scroll fixture starts offscreen with nonempty extent", before.Top > window.ClientSize.Height
            && before.Height == 32 && scroll.Extent.Height > scroll.Viewport.Height && scroll.Offset.Y == 0);
        var requests = 0;
        EventHandler<RequestBringIntoViewEventArgs> handler = (_, e) => { if (ReferenceEquals(e.TargetObject, target)) ++requests; };
        scroll.AddHandler(Control.RequestBringIntoViewEvent, handler, RoutingStrategies.Tunnel, true);
        try
        {
            var returned = bridge.Perform(id, "focus", null);
            Settle(window);
            Sample("new-focus", window, scroll, target, returned, requests);
            Check("New native focus settles automatic and explicit bring within viewport", returned && Fits(window, scroll, target) && requests >= 2);
        }
        finally { scroll.RemoveHandler(Control.RequestBringIntoViewEvent, handler); }
    });
    WithFixture((window, scroll, previous, target, bridge, id) =>
    {
        target.Focus();
        Settle(window);
        scroll.Offset = default;
        Settle(window);
        var returned = bridge.Perform(id, "focus", null);
        Settle(window);
        Sample("already-focused", window, scroll, target, returned, -1);
        Check("Already focused native peer explicitly brings its offscreen target into view", returned && Fits(window, scroll, target));
    });
    WithFixture((window, scroll, previous, target, bridge, id) =>
    {
        scroll.BringIntoViewOnFocusChange = false;
        var returned = bridge.Perform(id, "focus", null);
        Settle(window);
        Sample("automatic-bring-disabled", window, scroll, target, returned, -1);
        Check("Disabled automatic focus bring preserves explicit native bring", returned && Fits(window, scroll, target));
    });
    WithFixture((window, scroll, previous, target, bridge, id) =>
    {
        var callbacks = 0;
        target.GotFocus += (_, _) => { ++callbacks; bridge.Bind(new StackPanel()); };
        var returned = bridge.Perform(id, "focus", null);
        Check("GotFocus render generation change rejects continuation", callbacks == 1 && !returned);
    });
    WithFixture((window, scroll, previous, target, bridge, id) =>
    {
        var callbacks = 0;
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            ++callbacks;
            window.LayoutUpdated -= handler;
            bridge.Bind(new StackPanel());
        };
        window.LayoutUpdated += handler;
        try
        {
            var returned = bridge.Perform(id, "focus", null);
            // This is the first layout invalidated by focus scrolling, not a fabricated callback.
            Settle(window);
            Check("Real layout callback changing render generation rejects continuation", callbacks == 1 && !returned);
        }
        finally { window.LayoutUpdated -= handler; }
    });
    WithFixture((window, scroll, previous, target, bridge, id) =>
    {
        var callbacks = 0;
        target.GotFocus += (_, _) =>
        {
            ++callbacks;
            ((StackPanel)scroll.Content!).Children.Remove(target);
        };
        var returned = bridge.Perform(id, "focus", null);
        Check("Native focus callback detaching current control rejects continuation", callbacks == 1 && !returned && TopLevel.GetTopLevel(target) is null);
    });
    WithFixture((window, scroll, previous, target, bridge, id) =>
    {
        var callbacks = 0;
        target.GotFocus += (_, _) => { ++callbacks; previous.Focus(); };
        var returned = bridge.Perform(id, "focus", null);
        Check("Native focus callback claiming another control rejects continuation", callbacks == 1 && !returned && previous.IsFocused && !target.IsFocused);
    });
    WithFixture((window, scroll, previous, target, bridge, id) =>
    {
        var spacer = (FocusLayoutBoundary)((StackPanel)scroll.Content!).Children[1];
        var callbacks = 0;
        var returned = true;
        var invalidAtCall = false;
        spacer.Callback = () =>
        {
            ++callbacks;
            spacer.Callback = null;
            // A real ArrangeOverride callback invalidates its native scrolling
            // ancestor while the same maintained layout pass is still running.
            scroll.InvalidateArrange();
            invalidAtCall = !scroll.IsArrangeValid;
            returned = bridge.Perform(id, "focus", null);
        };
        spacer.Height = 569;
        Settle(window);
        Check("Reentrant real arrangement with invalid ancestor rejects unsettled bring", callbacks == 1 && invalidAtCall && !returned);
    });
    Console.WriteLine($"{results.Count} real native focus/scroll checks; {results.Count(r => r.Passed)} passed; {results.Count(r => !r.Passed)} failed. Controlled callback boundary regression, not actual browser case acceptance.");
    return results.Any(r => !r.Passed);
}, CancellationToken.None);
return failed ? 1 : 0;

static void Settle(Window window)
{
    window.UpdateLayout();
    using var frame = window.CaptureRenderedFrame();
}

public sealed class FocusScrollApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<FocusScrollApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
}

public sealed class FocusLayoutBoundary : Border
{
    public Action? Callback { get; set; }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        Callback?.Invoke();
        return arranged;
    }
}
