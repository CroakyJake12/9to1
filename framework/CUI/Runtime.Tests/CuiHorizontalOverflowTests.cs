using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
