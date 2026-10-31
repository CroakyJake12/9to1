using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Reflection;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiPopupAnchorTrackingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Open_menu_tracks_ancestor_layout_and_scroll_without_changing_local_target_bounds(bool overlay)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup("""
                <Container Type="Menu"><Button Type="Menu Item" Content="File">
                  <Button Type="Menu Item" Content="Open"/>
                </Button></Container>
                """);
            Assert.Empty(loaded.Diagnostics);
            var ancestor = new Border { Width = 220, Height = 48, Child = loaded.Root };
            // Keep the initial popup (including CUI's shadow margin/offset) clear of
            // viewport edges, so constraint clamping cannot change its movement delta.
            Canvas.SetLeft(ancestor, 300);
            Canvas.SetTop(ancestor, 160);
            var canvas = new Canvas { Width = 1200, Height = 900, Children = { ancestor } };
            var scroll = new ScrollViewer
            {
                Content = canvas,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            };
            var window = NewWindow(scroll, 800, 600);
            try
            {
                window.Show();
                Pump(window);
                var owner = Assert.Single(loaded.Root!.GetVisualDescendants().OfType<CuiMenuItem>());
                var popup = Assert.Single(owner.GetVisualDescendants().OfType<Popup>());
                popup.ShouldUseOverlayLayer = overlay;
                owner.Focus();
                owner.IsSubMenuOpen = true;
                Pump(window);
                var child = Assert.IsAssignableFrom<Control>(popup.Child);
                var host = overlay
                    ? (Control)Assert.Single(child.GetVisualAncestors().OfType<OverlayPopupHost>())
                    : Assert.Single(child.GetVisualAncestors().OfType<PopupRoot>());
                var localBounds = owner.Bounds;
                var targetBefore = owner.TranslatePoint(default, window)!.Value;
                var popupBefore = HostPosition(host, window);
                Assert.InRange(popupBefore.X, 1, window.ClientSize.Width - host.Bounds.Width - 1);
                Assert.InRange(popupBefore.Y, 1, window.ClientSize.Height - host.Bounds.Height - 1);

                // The target stays at the same coordinates inside its immediate parent.
                // Only an ancestor's arrangement changes, as when a toolbar is reflowed.
                Canvas.SetLeft(ancestor, 380);
                Canvas.SetTop(ancestor, 210);
                Pump(window);
                Assert.Equal(localBounds, owner.Bounds);
                Assert.Equal(new Vector(80, 50), owner.TranslatePoint(default, window)!.Value - targetBefore);
                Assert.Equal(new Vector(80, 50), HostPosition(host, window) - popupBefore);
                Assert.True(owner.IsSubMenuOpen);

                targetBefore = owner.TranslatePoint(default, window)!.Value;
                popupBefore = HostPosition(host, window);
                scroll.Offset = new Vector(40, 30);
                Pump(window);
                Assert.Equal(localBounds, owner.Bounds);
                Assert.Equal(new Vector(-40, -30), owner.TranslatePoint(default, window)!.Value - targetBefore);
                Assert.Equal(new Vector(-40, -30), HostPosition(host, window) - popupBefore);
                Assert.True(owner.IsSubMenuOpen);

                // Exercise the menu's normal keyboard dismissal after both repositionings.
                var keyboard = AvaloniaLocator.Current.GetRequiredService<IKeyboardDevice>();
                var inputRoot = ((ForwardingPlatformProxy)(object)window.PlatformImpl!).InputRoot!;
                window.PlatformImpl!.Input!.Invoke(new RawKeyEventArgs(keyboard, 1, inputRoot,
                    RawKeyEventType.KeyDown, Key.Escape, RawInputModifiers.None, PhysicalKey.None, null));
                window.PlatformImpl.Input.Invoke(new RawKeyEventArgs(keyboard, 2, inputRoot,
                    RawKeyEventType.KeyUp, Key.Escape, RawInputModifiers.None, PhysicalKey.None, null));
                Pump(window);
                Assert.False(owner.IsSubMenuOpen);
                Assert.False(popup.IsOpen);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, 40)]
    [InlineData(false, 300)]
    [InlineData(true, 40)]
    [InlineData(true, 300)]
    public async Task Top_level_menu_frame_aligns_with_target_left_at_interior_and_near_edge(bool overlay, double left)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup("""
                <Container Type="Menu"><Button Type="Menu Item" Content="File">
                  <Button Type="Menu Item" Content="More available actions">
                    <Button Type="Menu Item" Content="Open"/>
                  </Button>
                </Button></Container>
                """);
            Assert.Empty(loaded.Diagnostics);
            var ancestor = new Border { Width = 240, Height = 48, Child = loaded.Root };
            Canvas.SetLeft(ancestor, left);
            Canvas.SetTop(ancestor, 100);
            var window = NewWindow(new Canvas { Children = { ancestor } }, 800, 600);
            try
            {
                window.Show();
                Pump(window);
                var owner = Assert.Single(loaded.Root!.GetVisualDescendants().OfType<CuiMenuItem>());
                var popup = Assert.Single(owner.GetVisualDescendants().OfType<Popup>());
                popup.ShouldUseOverlayLayer = overlay;
                owner.Focus();
                owner.IsSubMenuOpen = true;
                Pump(window);

                var frame = Assert.IsAssignableFrom<Border>(popup.Child);
                var host = overlay
                    ? (Control)Assert.Single(frame.GetVisualAncestors().OfType<OverlayPopupHost>())
                    : Assert.Single(frame.GetVisualAncestors().OfType<PopupRoot>());
                var hostPosition = HostPosition(host, window);
                var frameInHost = frame.TranslatePoint(default, host)!.Value;
                var target = owner.TranslatePoint(default, window)!.Value;
                // Compare the visible frame, not its outer native window: CUI reserves
                // shadow space and compensates for it with the shared horizontal offset.
                Assert.True(hostPosition.X > 0);
                Assert.Equal(-frame.Margin.Left, popup.HorizontalOffset);
                Assert.Equal(target.X, hostPosition.X + frameInHost.X);
                Assert.True(owner.IsSubMenuOpen);
                var nestedOwner = Assert.Single(owner.Items.OfType<CuiMenuItem>());
                var nestedPopup = Assert.Single(nestedOwner.GetVisualDescendants().OfType<Popup>());
                Assert.Equal(PlacementMode.RightEdgeAlignedTop, nestedPopup.Placement);

                var keyboard = AvaloniaLocator.Current.GetRequiredService<IKeyboardDevice>();
                var inputRoot = ((ForwardingPlatformProxy)(object)window.PlatformImpl!).InputRoot!;
                window.PlatformImpl!.Input!.Invoke(new RawKeyEventArgs(keyboard, 1, inputRoot,
                    RawKeyEventType.KeyDown, Key.Escape, RawInputModifiers.None, PhysicalKey.None, null));
                window.PlatformImpl.Input.Invoke(new RawKeyEventArgs(keyboard, 2, inputRoot,
                    RawKeyEventType.KeyUp, Key.Escape, RawInputModifiers.None, PhysicalKey.None, null));
                Pump(window);
                Assert.False(owner.IsSubMenuOpen);
                Assert.False(popup.IsOpen);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Unrelated_layout_does_not_reconfigure_an_unchanged_native_popup_anchor()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var target = new Border { Width = 80, Height = 40 };
            var ancestor = new Border { Width = 100, Height = 60, Child = target };
            var unrelated = new Border { Width = 20, Height = 20 };
            var canvas = new Canvas { Children = { ancestor, unrelated } };
            var window = NewWindow(canvas, 600, 400);
            var placements = 0;
            var popup = new Popup
            {
                PlacementTarget = target,
                ShouldUseOverlayLayer = false,
                Child = new TextBlock { Text = "Popup" },
                Placement = PlacementMode.Custom,
                CustomPopupPlacementCallback = placement =>
                {
                    placements++;
                    placement.Anchor = PopupAnchor.BottomLeft;
                    placement.Gravity = PopupGravity.BottomRight;
                },
            };
            try
            {
                window.Show();
                Pump(window);
                popup.Open();
                Pump(window);
                var settled = placements;
                Assert.True(settled > 0);

                unrelated.Width = 30;
                Pump(window);
                Assert.Equal(settled, placements);

                Canvas.SetLeft(ancestor, 40);
                Pump(window);
                Assert.True(placements > settled);
                settled = placements;
                unrelated.Width = 40;
                Pump(window);
                Assert.Equal(settled, placements);
            }
            finally { popup.Close(); window.Close(); }
        }, CancellationToken.None);
    }

    private static Point HostPosition(Control host, Window window) => host is PopupRoot native
        ? native.PlatformImpl!.Position.ToPoint(window.RenderScaling)
        : host.TranslatePoint(default, window)!.Value;

    private static Window NewWindow(Control content, double width, double height)
    {
        var inner = PlatformManager.CreateWindow();
        var platform = DispatchProxy.Create<IWindowImpl, ForwardingPlatformProxy>();
        ((ForwardingPlatformProxy)(object)platform).Initialize(inner, (method, _) =>
        {
            if (method.Name != nameof(IWindowImpl.CreatePopup)) return (false, null);
            var popupWindow = PlatformManager.CreateWindow();
            var popupInner = Assert.IsAssignableFrom<IPopupImpl>(popupWindow);
            // Headless IPopupImpl.PopupPositioner is null. Supply the production managed
            // positioner, retaining headless rendering/input and real PopupRoot hosting.
            var positioner = new ManagedPopupPositioner(new ManagedPopupPositionerPopupImplHelper(
                platform, (position, size, _) =>
                {
                    popupWindow.Move(position);
                    popupWindow.Resize(size);
                }));
            var popup = DispatchProxy.Create<IPopupImpl, ForwardingPlatformProxy>();
            ((ForwardingPlatformProxy)(object)popup).Initialize(popupInner, (member, _) =>
                member.Name == "get_PopupPositioner" ? (true, positioner) : (false, null));
            return (true, popup);
        });
        return new Window(platform) { Content = content, Width = width, Height = height };
    }

    public class ForwardingPlatformProxy : DispatchProxy
    {
        private object _inner = null!;
        private Func<MethodInfo, object?[]?, (bool Handled, object? Value)> _override = null!;
        internal IInputRoot? InputRoot { get; private set; }

        internal void Initialize(object inner,
            Func<MethodInfo, object?[]?, (bool Handled, object? Value)> dispatchOverride)
        {
            _inner = inner;
            _override = dispatchOverride;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod ?? throw new InvalidOperationException("Missing platform method.");
            if (method.Name == nameof(ITopLevelImpl.SetInputRoot)) InputRoot = (IInputRoot)args![0]!;
            var result = _override(method, args);
            return result.Handled ? result.Value : method.Invoke(_inner, args);
        }
    }

    private static void Pump(Window window)
    {
        // Same dispatcher/render settling as the headless extensions, without their
        // IHeadlessWindow cast: this window uses the delegating platform adapter above.
        for (var i = 0; i < 10; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (!Dispatcher.UIThread.HasJobsWithPriority(DispatcherPriority.SystemIdle)) return;
        }
        Dispatcher.UIThread.RunJobs();
    }
}
