using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Lane-2 coverage for the generic escape floating portal. When an authored floating cell's resolved
/// rect must escape the owning overlay panel's own physical bounds (the native composer leaf clip
/// case), the panel engages a top-level popup portal that re-parents the SAME authored host, positions
/// it against the real owning window viewport, restores it on no-safe-space/recovery, and closes
/// through the canonical authored action/ledger route (Escape joins the loader's action pipeline).
/// Execution is slot-gated / NOT RUN here.
/// </summary>
public sealed class CuiFloatingPortalTests
{
    private const string Markup = """
        <Cui>
          <Actions><Action name="toggle-fmt"><Toggle Target="FormattingOpen" /></Action></Actions>
          <Variable Type="bool" ID="FormattingOpen">True</Variable>
          <Container Type="Overlay Float" id="surface" padding="30">
            <Container id="body" Type="Vertical Stack"/>
            <Container id="float-layer" Type="Floating" Target="body" Placement="above" action="toggle-fmt">
              <Container id="inner-bar" Type="Wrap" CellGap="6" IsVisible="{Binding FormattingOpen}">
                <Button id="f0" content="B" MinWidth="36" MinHeight="36"/>
              </Container>
            </Container>
          </Container>
        </Cui>
        """;

    private const string ClosedInnerMarkup = """
        <Cui>
          <Actions><Action name="toggle-fmt"><Toggle Target="FormattingOpen" /></Action></Actions>
          <Variable Type="bool" ID="FormattingOpen">False</Variable>
          <Container Type="Overlay Float" id="surface" padding="30">
            <Container id="body" Type="Vertical Stack"/>
            <Container id="float-layer" Type="Floating" Target="body" Placement="above" action="toggle-fmt">
              <Container id="inner-bar" Type="Wrap" CellGap="6" IsVisible="{Binding FormattingOpen}">
                <Button id="f0" content="B" MinWidth="36" MinHeight="36"/>
              </Container>
            </Container>
          </Container>
        </Cui>
        """;

    private const string FailureMarkup = """
        <Cui>
          <Actions><Action name="toggle-bad"><Toggle Target="UndefinedVar" /></Action></Actions>
          <Container Type="Overlay Float" id="surface" padding="30">
            <Container id="body" Type="Vertical Stack"/>
            <Container id="float-layer" Type="Floating" Target="body" Placement="above" action="toggle-bad">
              <Button id="f0" content="B" MinWidth="36" MinHeight="36"/>
            </Container>
          </Container>
        </Cui>
        """;

    private const string ConditionalMountedMarkup = """
        <Cui>
          <Actions><Action name="toggle-fmt"><Toggle Target="FormattingOpen" /></Action></Actions>
          <Variable Type="bool" ID="FormattingOpen">True</Variable>
          <If condition="{Binding Visible}">
            <Container Type="Overlay Float" id="surface" padding="30">
              <Container id="body" Type="Vertical Stack"/>
              <Container id="float-layer" Type="Floating" Target="body" Placement="above" action="toggle-fmt">
                <Button id="f0" content="B" MinWidth="36" MinHeight="36"/>
              </Container>
            </Container>
          </If>
        </Cui>
        """;

    private static CuiOverlayPanel Mount(CuiControlLoader loader, string markup,
        out CuiContainerControl surface, out CuiContainerControl authoredHost, out Window window)
    {
        var loaded = loader.LoadMarkup(markup);
        Assert.Empty(loaded.Diagnostics);
        surface = (CuiContainerControl)loaded.Root!;
        loader.WireBindings(surface);
        authoredHost = surface.GetVisualDescendants().OfType<CuiContainerControl>()
            .Single(c => c.AuthoredTargetId is not null);

        // Mount the compact composer surface inside a larger window so the resolved floating rect can
        // legitimately sit inside the window viewport while escaping the composer leaf itself. The
        // authored target (body) fills the surface, so the "Above" float resolves to a rect that must
        // leave the panel's top edge but still lies within the window viewport: a deterministic escape.
        var wrapper = new Border { Width = 200, Height = 120, Child = surface };
        var canvas = new Canvas();
        canvas.Children.Add(wrapper);
        Canvas.SetLeft(wrapper, 20);
        Canvas.SetTop(wrapper, 130);
        window = CuiNativePopupFixture.New(canvas, 400, 300);
        window.Show();
        PumpLayout(window);
        return (CuiOverlayPanel)surface.Layout;
    }

    private static CuiOverlayPanel LoadSurface(CuiControlLoader loader,
        out CuiContainerControl surface, out CuiContainerControl authoredHost, out Window window) =>
        Mount(loader, Markup, out surface, out authoredHost, out window);

    /// <summary>Mount a compact composer surface and drive ONLY the real layout manager/dispatcher
    /// (window.Show + Dispatcher.RunJobs + UpdateLayout) — never manual Measure/Arrange — so the portal's
    /// per-pass wrapper LayoutUpdated behavior is exercised through the genuine render/dispatcher cycle.</summary>
    private static CuiOverlayPanel MountReal(CuiControlLoader loader, string markup,
        out CuiContainerControl surface, out CuiContainerControl authoredHost, out Window window)
    {
        var loaded = loader.LoadMarkup(markup);
        Assert.Empty(loaded.Diagnostics);
        surface = (CuiContainerControl)loaded.Root!;
        loader.WireBindings(surface);
        authoredHost = surface.GetVisualDescendants().OfType<CuiContainerControl>()
            .Single(c => c.AuthoredTargetId is not null);

        var wrapper = new Border { Width = 200, Height = 120, Child = surface };
        var canvas = new Canvas();
        canvas.Children.Add(wrapper);
        Canvas.SetLeft(wrapper, 20);
        Canvas.SetTop(wrapper, 130);
        window = CuiNativePopupFixture.New(canvas, 400, 300);
        window.Show();
        for (var i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
        return (CuiOverlayPanel)surface.Layout;
    }

    private static Button? FindButton(Control root, string id) =>
        root.GetVisualDescendants().OfType<Button>().SingleOrDefault(b =>
            AutomationProperties.GetAutomationId(b) == id);

    private static Control? FindContainer(Control root, string id) =>
        root.GetVisualDescendants().OfType<Control>().SingleOrDefault(c =>
            AutomationProperties.GetAutomationId(c) == id);

    private static void SendEscapeToPortaledContent(CuiFloatingPortal portal)
    {
        var btn = portal.Host.GetVisualDescendants().OfType<Button>().First();
        btn.Focus();
        var tl = TopLevel.GetTopLevel(btn);
        Assert.NotNull(tl);
        tl!.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
    }

    [Fact]
    public async Task Escaping_floating_cell_engages_portal_with_the_same_retained_host()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            try
            {
                var panel = LoadSurface(loader, out var surface, out var authoredHost, out window);

                // The rect escapes the composer leaf: the portal must be engaged and open.
                var portal = Assert.Single(panel.ActivePortals);
                Assert.True(portal.IsOpen);
                // The SAME authored floating host lives in the portal (never a copy).
                Assert.Same(authoredHost, portal.Host);
                // It is no longer a visual descendant of the surface (the in-tree cell is out of flow).
                Assert.DoesNotContain(authoredHost, surface.GetVisualDescendants());
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task No_safe_space_dismisses_portal_and_safe_space_recovers_the_same_retained_host()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            try
            {
                var panel = LoadSurface(loader, out var surface, out var authoredHost, out window);
                var portal = Assert.Single(panel.ActivePortals);
                Assert.Same(authoredHost, portal.Host);

                window.Width = 40;
                window.Height = 40;
                PumpLayout(window);
                Assert.Empty(panel.ActivePortals);
                Assert.Contains(authoredHost, surface.GetVisualDescendants());

                window.Width = 400;
                window.Height = 300;
                PumpLayout(window);
                var recovered = Assert.Single(panel.ActivePortals);
                Assert.True(recovered.IsOpen);
                Assert.Same(authoredHost, recovered.Host);
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Off_state_retires_portal_and_reopen_reengages_the_same_retained_host()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            try
            {
                var panel = LoadSurface(loader, out var surface, out var authoredHost, out window);
                var first = Assert.Single(panel.ActivePortals);
                Assert.Same(authoredHost, first.Host);

                authoredHost.IsVisible = false;
                PumpLayout(window);
                Assert.Empty(panel.ActivePortals);
                Assert.Contains(authoredHost, surface.GetVisualDescendants());

                authoredHost.IsVisible = true;
                PumpLayout(window);
                var reopened = Assert.Single(panel.ActivePortals);
                Assert.True(reopened.IsOpen);
                Assert.Same(authoredHost, reopened.Host);
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Inner_visible_content_off_retires_portal_and_on_recovers_same_host()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            try
            {
                var panel = LoadSurface(loader, out var surface, out var authoredHost, out window);
                var f0 = FindButton(authoredHost, "f0");
                Assert.NotNull(f0);

                f0!.IsVisible = false;
                PumpLayout(window);
                Assert.Empty(panel.ActivePortals);
                Assert.Contains(authoredHost, surface.GetVisualDescendants());

                f0.IsVisible = true;
                PumpLayout(window);
                var reopened = Assert.Single(panel.ActivePortals);
                Assert.True(reopened.IsOpen);
                Assert.Same(authoredHost, reopened.Host);
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Closed_inner_content_under_hidden_wrapper_recovers_host_measure_when_opened()
    {
        // Regression for the cached-measure gate: opening the bound inner authored content must make the
        // always-visible floating host re-measure, even though the host sits under a suppressed/hidden
        // wrapper (unplaced/portaled) where the normal layout-manager descendant remeasurement is blocked.
        // Authored FormattingOpen begins False so the closed precondition is the real authored zero-size
        // state, and the open transition goes through the loader's canonical toggle-fmt close-gesture
        // action path (FloatingCloseRequested -> ObserveActionDispatch -> local toggle) with its real ledger.
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            try
            {
                var panel = Mount(loader, ClosedInnerMarkup, out var surface, out var authoredHost, out window);

                // Closed inner content (FormattingOpen=False): inner-bar is hidden, so the floating host
                // measures zero and Place resolves to the genuinely-hidden unplaced state (no portal).
                Assert.Empty(panel.ActivePortals);
                Assert.Contains(authoredHost, surface.GetVisualDescendants());
                Assert.True(authoredHost.DesiredSize.Width <= 0 && authoredHost.DesiredSize.Height <= 0,
                    $"closed floating host must measure zero (DesiredSize={authoredHost.DesiredSize})");

                // Open the bound inner-bar through the canonical authored toggle action and join its ledger.
                Assert.NotNull(authoredHost.FloatingCloseRequested);
                authoredHost.FloatingCloseRequested!.Invoke();
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
                PumpLayout(window);

                // The host must re-measure the now-visible content even under the hidden wrapper; otherwise
                // the cached 0,0 DesiredSize leaves the toolbar unplaced/invisible forever.
                Assert.True(authoredHost.DesiredSize.Width > 0 && authoredHost.DesiredSize.Height > 0,
                    $"floating host must re-measure opened content (DesiredSize={authoredHost.DesiredSize})");
                var reopened = Assert.Single(panel.ActivePortals);
                Assert.True(reopened.IsOpen);
                Assert.Same(authoredHost, reopened.Host);
                var f0 = FindButton(authoredHost, "f0");
                Assert.NotNull(f0);
                Assert.True(f0!.IsEffectivelyVisible, "the reopened command must be effectively visible through the portal");
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Real_dispatcher_cycles_settle_unchanged_portal_geometry_without_rescheduling()
    {
        // Regression for the unconditional wrapper.LayoutUpdated invalidation loop. Open the authored
        // composer through the real action path, then drive ONLY the real layout manager/dispatcher cycle
        // (never manual Measure/Arrange) and assert the wrapper layout-update rate settles instead of
        // growing without bound when the portal geometry is unchanged.
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            try
            {
                var panel = MountReal(loader, ClosedInnerMarkup, out var surface, out var authoredHost, out window);

                // Closed inner content: no portal yet.
                Assert.Empty(panel.ActivePortals);

                // Open via the canonical authored toggle action and join its real ledger.
                Assert.NotNull(authoredHost.FloatingCloseRequested);
                authoredHost.FloatingCloseRequested!.Invoke();
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
                for (var i = 0; i < 10; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                }

                var portal = Assert.Single(panel.ActivePortals);
                Assert.True(portal.IsOpen);
                Assert.Same(authoredHost, portal.Host);

                var wrapper = portal.Popup.Child as Border;
                Assert.NotNull(wrapper);
                var layoutUpdates = 0;
                EventHandler counter = (_, _) => layoutUpdates++;
                wrapper!.LayoutUpdated += counter;
                try
                {
                    var previous = -1;
                    var settled = false;
                    for (var cycle = 0; cycle < 80; cycle++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        window.UpdateLayout();
                        if (layoutUpdates == previous)
                        {
                            settled = true;
                            break;
                        }
                        previous = layoutUpdates;
                    }
                    Assert.True(settled,
                        $"unchanged portal geometry kept rescheduling layout ({layoutUpdates} wrapper layout updates)");
                    Assert.Equal(previous, layoutUpdates);
                    // The portal still owns the SAME authored host, still open, after settling.
                    Assert.True(portal.IsOpen);
                    Assert.Same(authoredHost, portal.Host);
                    Assert.True(authoredHost.IsAttachedToVisualTree(), "the retained authored host must stay mounted after settling");
                }
                finally { wrapper!.LayoutUpdated -= counter; }
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Bound_inner_child_transition_schedules_owner_and_places_open_toolbar()
    {
        // Gate-1 regression: opening the authored inner toolbar changes neither the always-visible outer
        // floating host nor the target, so the owning overlay must observe the retained authored child
        // graph and schedule itself — otherwise Place/MeasureHost never re-run and the opened bar stays
        // 0,0 / hidden forever. Drive only the real layout manager/dispatcher (no forced
        // InvalidateMeasure, no manual Measure/Arrange) and assert the opened toolbar is genuinely placed.
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            try
            {
                var panel = MountReal(loader, ClosedInnerMarkup, out var surface, out var authoredHost, out window);

                // Closed bound inner content: no portal, host measures zero.
                Assert.Empty(panel.ActivePortals);
                Assert.True(authoredHost.DesiredSize.Width <= 0 && authoredHost.DesiredSize.Height <= 0,
                    $"closed floating host must measure zero (DesiredSize={authoredHost.DesiredSize})");

                // Open through the canonical authored toggle action and its real ledger.
                Assert.NotNull(authoredHost.FloatingCloseRequested);
                authoredHost.FloatingCloseRequested!.Invoke();
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));

                // Normal dispatcher/render progression (real layout manager; never manual Measure/Arrange).
                for (var i = 0; i < 20; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                }

                // The opened toolbar must now be genuinely placed: host re-measures live content, the
                // portal engages with the SAME retained host, and the bound command is effectively visible.
                Assert.True(authoredHost.DesiredSize.Width > 0 && authoredHost.DesiredSize.Height > 0,
                    $"floating host must re-measure opened content (DesiredSize={authoredHost.DesiredSize})");
                var portal = Assert.Single(panel.ActivePortals);
                Assert.True(portal.IsOpen);
                Assert.Same(authoredHost, portal.Host);
                var f0 = FindButton(authoredHost, "f0");
                Assert.NotNull(f0);
                Assert.True(f0!.IsEffectivelyVisible, "the reopened command must be effectively visible through the portal");
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Escape_on_hosted_root_closes_via_authored_action_ledger()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            try
            {
                var panel = LoadSurface(loader, out var surface, out var authoredHost, out window);
                var portal = Assert.Single(panel.ActivePortals);
                Assert.Same(authoredHost, portal.Host);

                // Wire is purely authored: the floating layer's referenced action is the canonical
                // composer.toggle-formatting-style local toggle joined to the loader's action ledger.
                // Escape must route through that SAME pipeline (pending-action task + RefreshLocalVariable)
                // — no direct visibility/lambda substitute.
                SendEscapeToPortaledContent(portal);
                PumpLayout(window);
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));

                // Retired, restored, and stays closed: authored FormattingOpen flipped off via the action,
                // so the inner format bar is hidden and no later layout pass reopens it.
                Assert.Empty(panel.ActivePortals);
                Assert.Contains(authoredHost, surface.GetVisualDescendants());
                var innerBar = FindContainer(authoredHost, "inner-bar");
                Assert.NotNull(innerBar);
                Assert.False(innerBar!.IsEffectivelyVisible);
                PumpLayout(window);
                Assert.Empty(panel.ActivePortals);
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Authored_close_failure_is_acknowledged_through_original_ledger()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            var failed = false;
            loader.ActionFailed += (_, _) => failed = true;
            Window window = null!;
            try
            {
                var panel = Mount(loader, FailureMarkup, out var surface, out var authoredHost, out window);
                var portal = Assert.Single(panel.ActivePortals);
                Assert.Same(authoredHost, portal.Host);

                // The authored toggle references an undeclared variable: the close still retires the
                // portal, and the original loader ledger surfaces the failure acknowledgement.
                SendEscapeToPortaledContent(portal);
                PumpLayout(window);
                try { await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (AggregateException) { }

                Assert.True(failed, "the authored close failure must be acknowledged through the original ledger");
                Assert.Empty(panel.ActivePortals);
                Assert.Contains(authoredHost, surface.GetVisualDescendants());
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Portaled_host_keeps_stable_authored_identity_traversal_reorder()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            try
            {
                var panel = LoadSurface(loader, out var surface, out var authoredHost, out window);
                Assert.Single(panel.ActivePortals);

                // Even while physically portaled, the authored host and its body sibling both remain
                // visible authored children: Loader's EnumerateControls/AuthoredChildren snapshot must
                // see them so retired portaled callbacks are withdrawn before original-ledger retirement.
                var all = surface.AuthoredChildren.ToArray();
                Assert.Equal(2, all.Length);
                Assert.Contains(authoredHost, all);
                Assert.Contains(all, c => AutomationProperties.GetAutomationId(c) == "body");

                // Generic reorder names every existing authored child exactly once (incl. the portaled
                // host) and succeeds; authored identity is preserved independently of physical cell.Child.
                surface.ReorderChildren(new[] { all[1], all[0] });
                Assert.Equal(2, surface.AuthoredChildren.Count);
                Assert.Contains(authoredHost, surface.AuthoredChildren);
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Reentrant_focus_driven_retirement_keeps_authored_custody_stable()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            Exception? reentrantFailure = null;
            try
            {
                var panel = LoadSurface(loader, out var surface, out var authoredHost, out window);
                surface.EnableCompoundFocus();
                var scope = surface.CompoundFocusScope;
                Assert.NotNull(scope);
                var portal = Assert.Single(panel.ActivePortals);

                // Focus a real portaled control so the owning scope reports focused; retirement then
                // focuses focus and publishes synchronous FocusChanged callbacks while custody is being
                // reconciled. Those callbacks MUST still see the authored host (stable identity) via the
                // normal cell.Child path, never through a transient active-portal-only recovery.
                var btn = portal.Host.GetVisualDescendants().OfType<Button>().First();
                btn.Focus();
                scope!.FocusChanged += (_, focused) =>
                {
                    if (!focused)
                    {
                        try
                        {
                            if (!surface.AuthoredChildren.Contains(authoredHost))
                                throw new InvalidOperationException("authored custody was dropped during retirement callback");
                        }
                        catch (Exception error) { reentrantFailure = error; }
                    }
                };
                panel.ForceRetirePortaledHost(authoredHost);
                PumpLayout(window);

                Assert.Null(reentrantFailure);
                Assert.Empty(panel.ActivePortals);
                Assert.Contains(authoredHost, surface.AuthoredChildren);
                Assert.Contains(authoredHost, surface.GetVisualDescendants());
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Portal_close_escape_path_retires_and_cleanup_restores_same_host()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            try
            {
                var panel = LoadSurface(loader, out var surface, out var authoredHost, out window);
                var portal = Assert.Single(panel.ActivePortals);

                // The direct-close route is exactly `_popup.Close()`; exercising it must retire the portal
                // (drop focus/relationship tracking) and restore the SAME authored host.
                portal.Popup.Close();
                PumpLayout(window);
                Assert.Empty(panel.ActivePortals);
                Assert.Contains(authoredHost, surface.GetVisualDescendants());
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Direct_dismiss_and_dispose_retire_portal_and_restore_same_host()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            try
            {
                var panel = LoadSurface(loader, out var surface, out var authoredHost, out window);
                Assert.Single(panel.ActivePortals);

                panel.ForceRetirePortaledHost(authoredHost);
                PumpLayout(window);
                // Direct retire closes the native popup; its once-only receipt joins the original ledger.
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Empty(panel.ActivePortals);
                Assert.Contains(authoredHost, surface.GetVisualDescendants());
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Host_removal_while_portaled_retires_portal_and_removes_host()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            try
            {
                var panel = LoadSurface(loader, out var surface, out var authoredHost, out window);
                Assert.Single(panel.ActivePortals);

                Assert.True(surface.RemoveChild(authoredHost));
                PumpLayout(window);
                Assert.Empty(panel.ActivePortals);
                Assert.DoesNotContain(authoredHost, surface.GetVisualDescendants());
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    private const string ConditionalMarkup = """
        <Cui>
          <Actions><Action name="toggle-fmt"><Toggle Target="FormattingOpen" /></Action></Actions>
          <Variable Type="bool" ID="FormattingOpen">False</Variable>
          <Container Type="Overlay Float" id="surface" padding="30">
            <Container id="body" Type="Vertical Stack"/>
            <If condition="{Binding Visible}">
              <Container id="float-layer" Type="Floating" Target="body" Placement="above" action="toggle-fmt">
                <Button id="f0" content="B" MinWidth="36" MinHeight="36"/>
              </Container>
            </If>
          </Container>
        </Cui>
        """;

    [Fact]
    public async Task Conditional_retirement_withdraws_exact_close_callback_before_presentation_disposal()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var state = new ConditionalState();
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(state);
            var loaded = loader.LoadMarkup(ConditionalMarkup);
            Assert.Empty(loaded.Diagnostics);
            var root = (CuiContainerControl)loaded.Root!;
            loader.WireBindings(root);

            CuiContainerControl OldHost() => root.GetLogicalDescendants().OfType<CuiContainerControl>()
                .Single(c => c.AuthoredTargetId is not null);
            var oldHost = OldHost();
            Assert.NotNull(oldHost.FloatingCloseRequested);
            Assert.NotNull(oldHost.FloatingCloseLedgerPublisher);

            // Retire the branch through the real loader (DetachControlTree). The exact loader-owned close
            // callback and ledger publisher must be withdrawn BEFORE presentation/disposal callbacks, and
            // the original action/ack ledger still settles.
            state.Visible = false;
            state.NotifyAll();
            await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(oldHost.FloatingCloseRequested);
            Assert.Null(oldHost.FloatingCloseLedgerPublisher);

            // A newer same-ID replacement is wired fresh (unchanged), never inheriting the retired handler.
            state.Visible = true;
            state.NotifyAll();
            var replacement = OldHost();
            Assert.NotSame(oldHost, replacement);
            Assert.NotNull(replacement.FloatingCloseRequested);
            Assert.NotNull(replacement.FloatingCloseLedgerPublisher);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Native_popup_close_is_acknowledged_once_in_owner_ledger_and_repeated_close_never_replays()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            try
            {
                var panel = LoadSurface(loader, out var surface, out var authoredHost, out window);
                var portal = Assert.Single(panel.ActivePortals);
                var closed = 0;
                portal.Popup.Closed += (_, _) => closed++;

                // Real Escape: authored action AND native close both join the SAME original ledger. The
                // native-close receipt is published before Close and settles only after the real close.
                SendEscapeToPortaledContent(portal);
                PumpLayout(window);
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));

                Assert.Equal(1, closed);
                Assert.Empty(panel.ActivePortals);
                Assert.Contains(authoredHost, surface.GetVisualDescendants());

                // Repeated close of the now-closed popup never replays a native close and never double-
                // admits; the SAME original receipt is the single acknowledgement and the ledger still
                // completes cleanly.
                portal.Popup.Close();
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(1, closed);
                Assert.Empty(panel.ActivePortals);
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Ancestor_retirement_rejects_captured_descendant_gesture_and_preserves_replacement()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var state = new ConditionalState();
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(state);
            var loaded = loader.LoadMarkup(ConditionalMountedMarkup);
            Assert.Empty(loaded.Diagnostics);
            var ifRoot = (CuiContainerControl)loaded.Root!;
            loader.WireBindings(ifRoot);
            Window window = null!;
            try
            {
                var surface = ifRoot.GetVisualDescendants().OfType<CuiContainerControl>()
                    .Single(c => c.Layout is CuiOverlayPanel);
                var authoredHost = surface.GetVisualDescendants().OfType<CuiContainerControl>()
                    .Single(c => c.AuthoredTargetId is not null);
                var panel = (CuiOverlayPanel)surface.Layout;
                window = MountSurfaceInWindow(surface);
                PumpLayout(window);
                var portal = Assert.Single(panel.ActivePortals);
                Assert.Same(authoredHost, portal.Host);

                // Capture the loader-owned descendant close gesture BEFORE ancestor retirement.
                var gesture = authoredHost.FloatingCloseRequested;
                Assert.NotNull(gesture);

                // Retire the ANCESTOR (the <If> branch) through the real loader: DetachControlTree
                // revokes the whole subtree (incl. the open portaled descendant) before presentation/
                // disposal callbacks run.
                state.Visible = false;
                state.NotifyAll();
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));

                Assert.Null(authoredHost.FloatingCloseRequested);
                Assert.Null(authoredHost.FloatingCloseLedgerPublisher);
                Assert.False(portal.IsOpen);

                // A previously captured descendant gesture must now be REJECTED: no new admission, no
                // authored-close dispatch, no failure.
                var beforePend = loader.PendingActionCount;
                gesture!.Invoke();
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(beforePend, loader.PendingActionCount);

                // Newer same-ID replacement is unaffected and freshly wired.
                state.Visible = true;
                state.NotifyAll();
                var surface2 = ifRoot.GetVisualDescendants().OfType<CuiContainerControl>()
                    .Single(c => c.Layout is CuiOverlayPanel);
                var replacement = surface2.GetVisualDescendants().OfType<CuiContainerControl>()
                    .Single(c => c.AuthoredTargetId is not null);
                Assert.NotSame(authoredHost, replacement);
                Assert.NotNull(replacement.FloatingCloseRequested);
                Assert.NotNull(replacement.FloatingCloseLedgerPublisher);
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Physical_close_failure_is_retained_once_across_disposal_and_quiesces_late_receipt()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            CuiFloatingPortal portal = null!;
            var closeAttempts = 0;
            EventHandler<CancelEventArgs>? cancelHandler = null;
            var closing = typeof(Avalonia.Controls.Primitives.Popup).GetEvent("Closing",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.NotNull(closing); // Real approved-fork cancellable event is internal, not absent.
            try
            {
                var panel = LoadSurface(loader, out var surface, out var authoredHost, out window);
                portal = Assert.Single(panel.ActivePortals);
                // Force a REAL physical close failure: cancel the native Closing so Close() cannot close.
                cancelHandler = (_, e) => { closeAttempts++; e.Cancel = true; };
                closing.GetAddMethod(nonPublic: true)!.Invoke(portal.Popup, [cancelHandler]);

                // Disposing the loader tears down surface -> overlay panel -> portal, performing the REAL
                // native close. Because Closing is cancelled, the physical close FAILS: the once-only
                // receipt is published and left incomplete, and custody is retained (popup stays open).
                Exception? caught = null;
                try { loader.Dispose(); }
                catch (Exception error) { caught = error; }
                Assert.NotNull(caught);
                Assert.Contains("did not close", Flatten(caught!), StringComparison.OrdinalIgnoreCase);
                Assert.Equal(1, closeAttempts);
                Assert.True(portal.Popup.IsOpen, "custody must be retained until the physical close is acknowledged");

                // After disposal, the SAME original failure surfaces from the quiesced ledger.
                var ex = await Assert.ThrowsAnyAsync<Exception>(
                    () => loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Contains("did not close", Flatten(ex), StringComparison.OrdinalIgnoreCase);

                // Repeat close joins the SAME original failure without replaying the native Close.
                var again = Assert.ThrowsAny<Exception>(() => portal.Dismiss());
                Assert.Contains("did not close", Flatten(again), StringComparison.OrdinalIgnoreCase);
                Assert.Equal(1, closeAttempts);

                // Let teardown proceed normally (un-cancel) so the window can close cleanly.
                closing.GetRemoveMethod(nonPublic: true)!.Invoke(portal.Popup, [cancelHandler]);
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Escape_late_Closed_failure_remains_original_in_close_ledger()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            using var loader = new CuiControlLoader();
            Window window = null!;
            CuiFloatingPortal? portal = null;
            EventHandler<EventArgs>? laterClosed = null;
            try
            {
                // Observe the ORIGINAL physical-close task without replacing its owner/ledger.
                // Install before portal construction, then restore the exact owned seam after Mount:
                // the portal's existing publisher lease retains only this forwarding observer.
                var loaded = loader.LoadMarkup(Markup);
                Assert.Empty(loaded.Diagnostics);
                var surface = (CuiContainerControl)loaded.Root!;
                loader.WireBindings(surface);
                var authoredHost = surface.GetVisualDescendants().OfType<CuiContainerControl>()
                    .Single(c => c.AuthoredTargetId is not null);
                var originalPublisher = authoredHost.FloatingCloseLedgerPublisher;
                Assert.NotNull(originalPublisher);
                Task? physicalCloseReceipt = null;
                string? nativeCloseAdmissionStack = null;
                var closeAdmissions = 0;
                authoredHost.FloatingCloseLedgerPublisher = task =>
                {
                    closeAdmissions++;
                    physicalCloseReceipt = task;
                    nativeCloseAdmissionStack = Environment.StackTrace;
                    originalPublisher!(task);
                };
                try { window = MountSurfaceInWindow(surface); }
                finally { authoredHost.FloatingCloseLedgerPublisher = originalPublisher; }
                var panel = (CuiOverlayPanel)surface.Layout;
                var activePortal = Assert.Single(panel.ActivePortals);
                portal = activePortal;
                Assert.True(activePortal.IsOpen);
                Assert.Null(activePortal.Cell.Child);
                Assert.Null(physicalCloseReceipt);
                bool? physicalReceiptCompletedInsideClosed = null;
                string? laterClosedStack = null;
                var original = new InvalidOperationException("later native Closed callback failed");
                Task? idleObservedInsideClosed = null;
                bool? ledgerCompletedInsideClosed = null;
                bool? hostRestoredInsideClosed = null;
                var closedCalls = 0;

                // Added after the portal's own OnClosed: this actual native callback runs later.
                laterClosed = (_, _) =>
                {
                    closedCalls++;
                    laterClosedStack = Environment.StackTrace;
                    physicalReceiptCompletedInsideClosed = physicalCloseReceipt?.IsCompleted;
                    hostRestoredInsideClosed = ReferenceEquals(activePortal.Cell.Child, authoredHost);
                    idleObservedInsideClosed = loader.WhenActionsIdleAsync();
                    ledgerCompletedInsideClosed = idleObservedInsideClosed.IsCompleted;
                    throw original;
                };
                activePortal.Popup.Closed += laterClosed;

                // Real PopupRoot Escape enters NativeClose without Dismiss's _dismissing guard.
                var inputError = Record.Exception(() => SendEscapeToPortaledContent(activePortal));
                Assert.Same(original, inputError);
                Assert.Equal(1, closedCalls);
                Assert.Equal(1, closeAdmissions);
                Assert.NotNull(physicalCloseReceipt);
                // Verify the intended internal NativeClose path rather than inferring it from host
                // restoration; reentrant authored-state retirement can also restore that same host.
                Assert.DoesNotContain("CuiFloatingPortal.Dismiss", nativeCloseAdmissionStack ?? "");
                Assert.DoesNotContain("CuiFloatingPortal.Dismiss", laterClosedStack ?? "");
                Assert.False(physicalReceiptCompletedInsideClosed ?? true,
                    "the specific ORIGINAL physical-close receipt completed inside a later Closed handler; "
                    + "admission=" + nativeCloseAdmissionStack + "; closed=" + laterClosedStack);
                Assert.True(hostRestoredInsideClosed is true,
                    "the portal OnClosed must run before the later handler; Dismiss would bypass it");
                Assert.False(ledgerCompletedInsideClosed ?? true,
                    "the original receipt cannot complete while a later native Closed handler is running");
                Assert.NotNull(idleObservedInsideClosed);

                var physicalError = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => physicalCloseReceipt!.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Same(original, physicalError);
                var capturedError = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => idleObservedInsideClosed!.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Same(original, capturedError);
                var idleError = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Same(original, idleError);
                Assert.False(activePortal.IsOpen);
            }
            finally
            {
                if (portal is not null && laterClosed is not null)
                    portal.Popup.Closed -= laterClosed;
                window?.Close();
            }
        }, CancellationToken.None);
    }

    private sealed class ConditionalState : ICuiBindingContext, INotifyPropertyChanged
    {
        public bool Visible = true;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch { "Visible" => Visible, _ => null };
            return value is not null;
        }
    }

    private static Window MountSurfaceInWindow(CuiContainerControl surface)
    {
        var wrapper = new Border { Width = 200, Height = 120, Child = surface };
        var canvas = new Canvas();
        canvas.Children.Add(wrapper);
        Canvas.SetLeft(wrapper, 20);
        Canvas.SetTop(wrapper, 130);
        var window = CuiNativePopupFixture.New(canvas, 400, 300);
        window.Show();
        PumpLayout(window);
        return window;
    }

    private static string Flatten(Exception error) =>
        error is AggregateException agg
            ? string.Join(" | ", agg.InnerExceptions.Select(e => e.Message))
            : error.Message;

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
