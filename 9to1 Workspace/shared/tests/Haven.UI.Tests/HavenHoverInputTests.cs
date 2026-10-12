using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenHoverInputTests
{
    [Fact]
    public void Uncaptured_hover_and_exit_do_not_begin_or_move_a_pointer_gesture()
    {
        var root = new Container(); var surface = new HoverSurface();
        surface.SetValue(HavenProperties.Width, HavenLength.Px(100));
        surface.SetValue(HavenProperties.Height, HavenLength.Px(100)); root.Add(surface);
        new HavenLayoutEngine().Layout(root, new HavenSize(200, 200), HavenPlatform.Windows, new Measure());
        var router = new HavenInputRouter(root);
        router.PointerMoved(new(20, 30));
        Assert.Equal(new HavenPoint(20, 30), surface.Hover);
        Assert.Equal(0, surface.Moves);
        router.PointerPressed(new(20, 30)); router.PointerMoved(new(40, 50));
        Assert.Equal(1, surface.Moves);
        router.PointerExited(); Assert.Null(surface.Hover);
        Assert.True(router.CancelPointer()); Assert.Equal(1, surface.Cancellations);
        Assert.False(router.PointerReleased(new(40, 50)));
    }

    private sealed class HoverSurface : HavenElement, IHavenHoverInputTarget, IHavenPointerInputTarget
    {
        public HavenPoint? Hover { get; private set; }
        public int Moves { get; private set; }
        public int Cancellations { get; private set; }
        public void PointerHovered(HavenPoint? point) => Hover = point;
        public bool PointerPressed(HavenPointerInput input) => true;
        public bool PointerMoved(HavenPointerInput input) { Moves++; return true; }
        public bool PointerReleased(HavenPointerInput input) => true;
        public bool PointerCancelled(HavenPointerInput input) { Cancellations++; return true; }
    }
    private sealed class Measure : IHavenMeasureContext
    { public HavenSize MeasureLeaf(HavenElement element, HavenSize available) => new(100, 100); }
}
