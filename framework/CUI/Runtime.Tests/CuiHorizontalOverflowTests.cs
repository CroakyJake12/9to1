using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiHorizontalOverflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Temporary_input_denial_restores_only_its_original_focus_after_all_gates_release(bool authoredFirst)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup("<Button Content='Original action'/>");
            Assert.Empty(loaded.Diagnostics);
            var button = Assert.IsType<Button>(loaded.Root);
            var window = new Window { Content = button, Width = 240, Height = 100 };
            try
            {
                window.Show(); Pump(window); Assert.True(button.Focus());
                if (authoredFirst) Interactive(false);
                using var first = CuiLifecycleProperties.RestrictInteraction(button);
                using var second = CuiLifecycleProperties.RestrictInteraction(button);
                if (!authoredFirst) Interactive(false);
                Assert.False(button.IsEnabled); Assert.False(button.Focusable);
                Assert.False(button.IsFocused); Assert.Null(window.FocusManager!.GetFocusedElement());
                first.Dispose(); Interactive(true); Pump(window);
                Assert.False(button.IsEnabled); Assert.False(button.IsFocused); // Final capability still denies.
                second.Dispose(); Pump(window);
                Assert.True(button.IsEnabled); Assert.True(button.Focusable); Assert.True(button.IsFocused);
                first.Dispose(); second.Dispose(); // Idempotent releases must not issue a second focus action.
                Interactive(false); Assert.False(button.IsEnabled); Assert.False(button.IsFocused);
                Interactive(true); Assert.True(button.IsEnabled); Assert.True(button.IsFocused);
                using (CuiLifecycleProperties.RestrictInteraction(button))
                { Assert.False(button.IsEnabled); Assert.False(button.IsFocused); }
                Assert.True(button.IsEnabled); Assert.True(button.IsFocused);
            }
            finally { window.Close(); }
            void Interactive(bool value) => Assert.True(CuiLifecycleProperties.SetLiteral(button, "Interactive", value.ToString(),
                CuiPropertyLevel.Instance, "fixture authored interaction"));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("competing-focus")]
    [InlineData("competing-focus-then-clear")]
    [InlineData("another-window-then-clear")]
    [InlineData("detach-reattach")]
    [InlineData("dispose")]
    [InlineData("hidden-then-visible")]
    [InlineData("ancestor-hidden-then-visible")]
    [InlineData("authored-disabled")]
    [InlineData("never-focused")]
    public async Task Releasing_input_denial_does_not_resurrect_cancelled_or_unowned_focus(string cancellation)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup("<Button Content='Original action'/>");
            Assert.Empty(loaded.Diagnostics);
            var button = Assert.IsType<Button>(loaded.Root);
            var other = new Button { Content = "Competing focus" };
            var panel = new StackPanel { Children = { button, other } };
            var window = new Window { Content = panel, Width = 240, Height = 140 };
            Window? anotherWindow = null;
            try
            {
                window.Show(); Pump(window);
                Assert.True(cancellation == "never-focused" ? other.Focus() : button.Focus());
                using var denial = CuiLifecycleProperties.RestrictInteraction(button);
                Assert.False(button.IsFocused); Assert.False(button.IsEnabled);
                switch (cancellation)
                {
                    case "competing-focus": Assert.True(other.Focus()); break;
                    case "competing-focus-then-clear":
                        Assert.True(other.Focus()); window.FocusManager!.Focus(null); break;
                    case "another-window-then-clear":
                        var remote = new Button { Content = "Another root" };
                        anotherWindow = new Window { Content = remote, Width = 200, Height = 100 };
                        anotherWindow.Show(); Pump(anotherWindow); Assert.True(remote.Focus());
                        anotherWindow.FocusManager!.Focus(null); break;
                    case "detach-reattach": panel.Children.Remove(button); panel.Children.Insert(0, button); break;
                    case "dispose": CuiLifecycleProperties.Detach(button); break;
                    case "hidden-then-visible":
                        Assert.True(CuiLifecycleProperties.SetLiteral(button, "Hidden", "True", CuiPropertyLevel.Instance, "fixture"));
                        Assert.True(CuiLifecycleProperties.SetLiteral(button, "Hidden", "False", CuiPropertyLevel.Instance, "fixture")); break;
                    case "ancestor-hidden-then-visible": panel.IsVisible = false; panel.IsVisible = true; break;
                    case "authored-disabled":
                        Assert.True(CuiPropertyPresentation.Set(button, "IsEnabled", false, CuiPropertyLevel.Instance, "fixture authored disabled")); break;
                }
                denial.Dispose(); Pump(window);
                Assert.False(button.IsFocused);
                if (cancellation is "competing-focus" or "never-focused") Assert.True(other.IsFocused);
                if (cancellation == "authored-disabled")
                {
                    Assert.False(button.IsEnabled);
                    CuiPropertyPresentation.Remove(button, "IsEnabled", CuiPropertyLevel.Instance, "fixture authored disabled");
                    Pump(window); Assert.True(button.IsEnabled); Assert.False(button.IsFocused);
                }
                if (cancellation == "dispose") Assert.False(button.IsEnabled); // No stale release effects after retirement.
            }
            finally { anotherWindow?.Close(); window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Authored_reveal_waits_for_layout_then_reveals_once_without_focus_or_manual_scroll_snapback()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup(RevealMarkup);
            Assert.Empty(loaded.Diagnostics);
            var container = Assert.IsType<CuiContainerControl>(loaded.Root);
            var target = Assert.IsType<CuiContainerControl>(container.AuthoredChildren.Last());
            var focus = new Button { Content = "Keep focus", Height = 30 };
            var window = new Window { Content = new StackPanel { Children = { focus, container } }, Width = 240, Height = 140 };
            try
            {
                window.Show(); focus.Focus(); Pump(window);
                var scroll = Assert.Single(container.GetVisualDescendants().OfType<ScrollViewer>());
                var presenter = Assert.Single(scroll.GetVisualDescendants().OfType<ScrollContentPresenter>());
                AssertVisible(); Assert.True(focus.IsFocused);
                scroll.Offset = default; Pump(window);
                Assert.Equal(0, scroll.Offset.X); // An unchanged true is not a permanent scroll lock.
                Set(false); Set(true); Pump(window);
                AssertVisible(); Assert.True(focus.IsFocused);
                scroll.Offset = default; Set(true); Pump(window);
                Assert.Equal(0, scroll.Offset.X);
                Set(false); Set(true); Set(false); Pump(window);
                Assert.Equal(0, scroll.Offset.X); // Stale pending state was withdrawn before layout.

                void Set(bool value) => Assert.True(CuiLayoutProperties.SetLiteral(target, "BringIntoView", value.ToString(),
                    CuiPropertyLevel.Overlay, "fixture current selection"));
                void AssertVisible()
                {
                    Assert.True(scroll.Offset.X > 0);
                    var origin = target.TranslatePoint(default, presenter)!.Value;
                    Assert.InRange(origin.X, -.5, presenter.Bounds.Width - target.Bounds.Width + .5);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Pending_reveal_is_withdrawn_on_detach_or_disposal_before_layout()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup(RevealMarkup);
            Assert.Empty(loaded.Diagnostics);
            var container = Assert.IsType<CuiContainerControl>(loaded.Root);
            var target = Assert.IsType<CuiContainerControl>(container.AuthoredChildren.Last());
            var window = new Window { Content = container, Width = 240, Height = 90 };
            try
            {
                window.Show(); Pump(window);
                var scroll = Assert.Single(container.GetVisualDescendants().OfType<ScrollViewer>());
                Set(false); scroll.Offset = default; Pump(window);
                Set(true); Assert.True(container.RemoveChild(target)); Pump(window);
                Assert.Equal(0, scroll.Offset.X);
                Set(false); container.AddChild(target); Pump(window);
                Assert.Equal(0, scroll.Offset.X);
                Set(true); CuiLayoutProperties.Detach(target); Pump(window);
                Assert.Equal(0, scroll.Offset.X);
                Assert.Same(scroll, Assert.Single(container.GetVisualDescendants().OfType<ScrollViewer>()));

                void Set(bool value) => Assert.True(CuiLayoutProperties.SetLiteral(target, "BringIntoView", value.ToString(),
                    CuiPropertyLevel.Overlay, "fixture current selection"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Arrows_follow_actual_direction_extent_and_resize_without_replacing_the_viewport()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup(Markup);
            Assert.Empty(loaded.Diagnostics);
            var container = Assert.IsType<CuiContainerControl>(loaded.Root);
            var window = new Window { Content = container, Width = 240, Height = 90 };
            try
            {
                window.Show(); Pump(window);
                var scroll = Assert.Single(container.GetVisualDescendants().OfType<ScrollViewer>());
                var left = Arrow(container, "CuiOverflowLeft"); var right = Arrow(container, "CuiOverflowRight");
                Assert.Same(container.Layout, scroll.Content);
                Assert.Equal(ScrollBarVisibility.Hidden, scroll.HorizontalScrollBarVisibility);
                Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
                Assert.False(left.IsEffectivelyVisible); Assert.True(right.IsEffectivelyVisible);
                var rows = container.AuthoredChildren.Select(child => child.TranslatePoint(default, container)!.Value.Y).ToArray();
                Assert.All(rows, y => Assert.Equal(rows[0], y, 2));
                var point = right.TranslatePoint(new Point(right.Bounds.Width / 2, right.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump(window);
                Assert.True(scroll.Offset.X > 0); Assert.True(left.IsEffectivelyVisible);
                scroll.Offset = new Vector(scroll.Extent.Width, 0); Pump(window);
                Assert.False(right.IsEffectivelyVisible); Assert.True(left.IsEffectivelyVisible);
                window.Width = 1000; Pump(window);
                Assert.False(left.IsEffectivelyVisible); Assert.False(right.IsEffectivelyVisible);
                window.Width = 240; Pump(window);
                Assert.True(right.IsEffectivelyVisible);
                container.SetHorizontalOverflowArrows(false); Pump(window);
                Assert.Same(scroll, Assert.Single(container.GetVisualDescendants().OfType<ScrollViewer>()));
                Assert.Equal(ScrollBarVisibility.Auto, scroll.HorizontalScrollBarVisibility);
                Assert.False(left.IsEffectivelyVisible); Assert.False(right.IsEffectivelyVisible);
                container.SetHorizontalOverflowArrows(true); Pump(window);
                Assert.Same(scroll, Assert.Single(container.GetVisualDescendants().OfType<ScrollViewer>()));
                Assert.Equal(ScrollBarVisibility.Hidden, scroll.HorizontalScrollBarVisibility);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Native_wheel_and_keyboard_work_and_scoped_accessibility_controls_the_backdrops()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Boards", "app.boards");
            var loaded = loader.LoadMarkup("<Theme Name='Professional'>" + Markup + "</Theme>");
            Assert.Empty(loaded.Diagnostics);
            var container = Assert.IsType<CuiContainerControl>(loaded.Root);
            var window = new Window { Content = container, Width = 240, Height = 90 };
            try
            {
                window.Show(); Pump(window);
                var scroll = Assert.Single(container.GetVisualDescendants().OfType<ScrollViewer>());
                var left = Arrow(container, "CuiOverflowLeft"); var right = Arrow(container, "CuiOverflowRight");
                window.MouseWheel(new Point(80, 20), new Vector(0, -1)); Pump(window);
                Assert.True(scroll.Offset.X > 0);
                var previous = scroll.Offset.X;
                window.MouseWheel(new Point(80, 20), new Vector(-1, 0)); Pump(window);
                Assert.True(scroll.Offset.X > previous);
                right.Focus(); Press(window, Key.Right); Pump(window);
                Assert.True(scroll.Offset.X > previous);
                Press(window, Key.End); Pump(window);
                Assert.False(right.IsEffectivelyVisible);
                left.Focus(); Press(window, Key.Home); Pump(window);
                Assert.Equal(0, scroll.Offset.X); Assert.False(left.IsEffectivelyVisible);
                container.Resources["CuiPanel2Brush"] = Brushes.Green;
                container.Resources["CuiTextBrush"] = Brushes.Yellow;
                container.Resources["CuiReduceMotion"] = true;
                var backdrop = Assert.IsType<Border>(right.Parent);
                Assert.Same(Brushes.Green, backdrop.Background);
                Assert.Same(Brushes.Yellow, right.Foreground);
                Assert.Null(right.Effect); Assert.Null(right.Transitions);
                container.Resources["CuiHighContrast"] = true;
                Assert.Equal(0, backdrop.BoxShadow.Count);
                Assert.True(backdrop.BorderThickness.Left > 0);
                var offset = scroll.Offset;
                container.Dispose();
                right.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(offset, scroll.Offset);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Scoped_thin_scrollbar_keeps_native_hit_area_drag_and_contrast_fallback()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup("<Container Type='Vertical Stack' VerticalScrolling='True' ScrollBarThickness='6'>" +
                "<Text Text='Document' Height='1000'/></Container>");
            Assert.Empty(loaded.Diagnostics);
            var container = Assert.IsType<CuiContainerControl>(loaded.Root);
            var window = new Window { Content = container, Width = 300, Height = 180 };
            try
            {
                window.Show(); Pump(window);
                var scroll = Assert.IsType<ScrollViewer>(container.Child);
                var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(value => value.Orientation == Avalonia.Layout.Orientation.Vertical);
                var thumb = Assert.Single(bar.GetVisualDescendants().OfType<Track>()).Thumb!;
                var face = thumb.GetVisualDescendants().OfType<Border>().Single(value => value.Name == "CuiScrollThumbFace");
                Assert.Equal(6, face.Bounds.Width, 2);
                Assert.True(thumb.Bounds.Width >= 12); Assert.True(bar.Bounds.Width >= 12);
                var point = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(0, 40));
                window.MouseUp(point + new Vector(0, 40), MouseButton.Left); Pump(window);
                Assert.True(scroll.Offset.Y > 0);
                container.SetScrollBarThickness(8); Pump(window); Assert.Equal(8, face.Bounds.Width, 2);
                container.Resources["CuiHighContrast"] = true; Pump(window); Assert.Equal(12, face.Bounds.Width, 2);
                container.Resources["CuiHighContrast"] = false;
                container.SetScrollBarThickness(0); Pump(window); Assert.Equal(12, face.Bounds.Width, 2);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public void Ordinary_horizontal_scrollers_keep_their_existing_rail_by_default()
    {
        using var loader = new CuiControlLoader();
        var loaded = loader.LoadMarkup("<Container Type='Horizontal Stack' HorizontalScrolling='True'><Text Text='Item'/></Container>");
        Assert.Empty(loaded.Diagnostics);
        var container = Assert.IsType<CuiContainerControl>(loaded.Root);
        Assert.Equal(ScrollBarVisibility.Auto, Assert.IsType<ScrollViewer>(container.Child).HorizontalScrollBarVisibility);
    }

    private const string RevealMarkup = "<Container Type='Horizontal Stack' HorizontalScrolling='True' HorizontalOverflowArrows='True' Height='60'>" +
        "<Button Text='First' Width='250'/><Container Type='Overlay' Width='80' Height='40' bring-into-view='True'><Button Text='Selected'/></Container></Container>";
    private static readonly string Markup = "<Container Type='Horizontal Stack' HorizontalScrolling='True' " +
        "VerticalScrolling='False' horizontal-overflow-arrows='True'>" +
        string.Concat(Enumerable.Range(0, 6).Select(index => $"<Button Content='Item {index}' Width='100' Height='40'/>")) + "</Container>";
    private static Button Arrow(Control root, string name) => root.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);
    private static void Pump(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
    }
}
