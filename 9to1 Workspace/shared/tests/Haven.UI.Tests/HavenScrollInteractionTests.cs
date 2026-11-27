using System.Reflection;
using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenScrollInteractionTests
{
    [Fact]
    public void Wheel_residual_chains_from_inner_boundary_to_outer_scroll_container()
    {
        var (root, outer, inner) = NestedScrollers();
        SetMetrics(outer, new HavenSize(140, 140), new HavenSize(140, 320));
        SetMetrics(inner, new HavenSize(100, 100), new HavenSize(100, 160));
        inner.ScrollY = 50;

        var router = new HavenInputRouter(root);
        Assert.True(router.Scroll(new HavenPoint(20, 20), 0, 30));

        Assert.Equal(60d, inner.ScrollY, 6);
        Assert.Equal(20d, outer.ScrollY, 6);
    }

    [Fact]
    public void Touch_drag_chains_nested_scroll_and_does_not_activate_pressed_surface()
    {
        var (root, outer, inner) = NestedScrollers();
        SetMetrics(outer, new HavenSize(140, 140), new HavenSize(140, 320));
        SetMetrics(inner, new HavenSize(100, 100), new HavenSize(100, 160));
        inner.ScrollY = 50;
        var invoked = 0;
        inner.Invoked += (_, _) => invoked++;
        var router = new HavenInputRouter(root);

        router.PointerPressed(new HavenPoint(50, 80), HavenPointerKind.Touch);
        router.PointerMoved(new HavenPoint(50, 40), HavenPointerKind.Touch);
        Assert.True(router.PointerReleased(new HavenPoint(50, 40)));

        Assert.Equal(60d, inner.ScrollY, 6);
        Assert.Equal(30d, outer.ScrollY, 6);
        Assert.Equal(0, invoked);
        Assert.Null(router.Pressed);
    }

    [Fact]
    public void Touch_jitter_below_threshold_does_not_scroll()
    {
        var (root, outer, inner) = NestedScrollers();
        SetMetrics(outer, new HavenSize(140, 140), new HavenSize(140, 320));
        SetMetrics(inner, new HavenSize(100, 100), new HavenSize(100, 160));
        inner.ScrollY = 20;
        var router = new HavenInputRouter(root);

        router.PointerPressed(new HavenPoint(50, 80), HavenPointerKind.Touch);
        router.PointerMoved(new HavenPoint(52, 77), HavenPointerKind.Touch);
        router.PointerReleased(new HavenPoint(52, 77));

        Assert.Equal(20d, inner.ScrollY, 6);
        Assert.Equal(0d, outer.ScrollY, 6);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scroll_offset_change_only_invalidates_layout_and_paint(bool horizontal)
    {
        var container = new Container();
        SetMetrics(container, new HavenSize(100, 100), new HavenSize(300, 400));
        var invalidations = new List<HavenInvalidationKinds>();
        container.Invalidated += (_, _) => invalidations.Add(container.LastInvalidationKinds);

        if (horizontal) container.ScrollX = 35;
        else container.ScrollY = 40;

        Assert.Equal(HavenInvalidationKinds.Layout | HavenInvalidationKinds.Paint, Assert.Single(invalidations));
        Assert.Equal(horizontal ? 35d : 0d, container.ScrollX);
        Assert.Equal(horizontal ? 0d : 40d, container.ScrollY);
    }

    [Fact]
    public void Scroll_layout_request_translates_existing_content()
    {
        var viewport = new Container();
        viewport.SetValue(HavenProperties.Width, HavenLength.Px(100));
        viewport.SetValue(HavenProperties.Height, HavenLength.Px(100));
        viewport.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        var content = new Text("A reply taller than the viewport");
        content.SetValue(HavenProperties.Width, HavenLength.Px(100));
        content.SetValue(HavenProperties.Height, HavenLength.Px(300));
        viewport.Add(content);
        var layout = new HavenLayoutEngine();
        var measure = new FixedMeasure();
        layout.Layout(viewport, new HavenSize(100, 100), HavenPlatform.Windows, measure);
        var originalBounds = content.Bounds;
        var pending = HavenInvalidationKinds.None;
        viewport.Invalidated += (_, _) => pending |= viewport.LastInvalidationKinds;

        Assert.True(viewport.ScrollBy(0, 40));
        Assert.True(pending.HasFlag(HavenInvalidationKinds.Layout));
        layout.Layout(viewport, new HavenSize(100, 100), HavenPlatform.Windows, measure);

        Assert.Same(content, Assert.Single(viewport.Children));
        Assert.Equal(new HavenRect(originalBounds.X, originalBounds.Y - 40,
            originalBounds.Width, originalBounds.Height), content.Bounds);
        Assert.Equal(200d, viewport.MaxScrollY);
        Assert.Equal(40d, viewport.ScrollY);
    }

    [Fact]
    public void Clamped_scroll_no_op_does_not_invalidate()
    {
        var container = new Container();
        SetMetrics(container, new HavenSize(100, 100), new HavenSize(300, 400));
        var invalidations = 0;
        container.Invalidated += (_, _) => invalidations++;

        Assert.False(container.ScrollBy(-10, -20));
        Assert.Equal(0, invalidations);
        Assert.True(container.ScrollBy(500, 600));
        Assert.Equal(1, invalidations);
        Assert.Equal(200d, container.ScrollX);
        Assert.Equal(300d, container.ScrollY);

        container.ScrollX = 500;
        container.ScrollY = 600;
        Assert.False(container.ScrollBy(10, 20));
        Assert.Equal(1, invalidations);
        Assert.Equal(200d, container.ScrollX);
        Assert.Equal(300d, container.ScrollY);
    }

    private static (Container Root, Container Outer, Container Inner) NestedScrollers()
    {
        var root = new Container { Layout = HavenLayout.Canvas };
        root.SetValue(HavenProperties.Width, HavenLength.Px(200));
        root.SetValue(HavenProperties.Height, HavenLength.Px(200));

        var outer = new Container { Layout = HavenLayout.Canvas };
        outer.SetValue(HavenProperties.Width, HavenLength.Px(140));
        outer.SetValue(HavenProperties.Height, HavenLength.Px(140));
        outer.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);

        var inner = new Container { Layout = HavenLayout.Canvas };
        inner.SetValue(HavenProperties.Width, HavenLength.Px(100));
        inner.SetValue(HavenProperties.Height, HavenLength.Px(100));
        inner.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);

        outer.Add(inner);
        root.Add(outer);
        new HavenLayoutEngine().Layout(root, new HavenSize(200, 200), HavenPlatform.Windows, new FixedMeasure());
        return (root, outer, inner);
    }

    private static void SetMetrics(Container container, HavenSize viewport, HavenSize extent)
    {
        var method = typeof(Container).GetMethod("UpdateScrollMetrics", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(container, [viewport, extent]);
    }

    private sealed class FixedMeasure : IHavenMeasureContext
    {
        public HavenSize MeasureLeaf(HavenElement element, HavenSize available) => new(available.Width, 44);
    }
}
