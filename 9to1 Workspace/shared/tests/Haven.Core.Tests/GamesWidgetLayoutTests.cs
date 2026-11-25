using Haven.Core.Games;
using Xunit;

namespace Haven.Core.Tests;

public class GamesWidgetLayoutTests
{
    [Fact]
    public void Centered_remains_centered_at_16_9_and_ultrawide()
    {
        var layout = GamesWidgetLayout.Centered(200, 100);
        var wide = layout.Resolve(1920, 1080);
        var ultra = layout.Resolve(2560, 1080);
        Assert.Equal(960, wide.X + wide.Width / 2);
        Assert.Equal(540, wide.Y + wide.Height / 2);
        Assert.Equal(1280, ultra.X + ultra.Width / 2);
        Assert.Equal(540, ultra.Y + ultra.Height / 2);
    }

    [Fact]
    public void EdgeStretched_adapts_to_viewport()
    {
        var layout = GamesWidgetLayout.EdgeStretched(0, 0);
        var bounds = layout.Resolve(1280, 720);
        Assert.Equal(0, bounds.X);
        Assert.Equal(0, bounds.Y);
        Assert.Equal(1280, bounds.Width);
        Assert.Equal(720, bounds.Height);
    }

    [Fact]
    public void Invalid_geometry_is_rejected()
    {
        var invalidAnchor = new GamesWidgetLayout(1.5, 0.5, 0, 0, 100, 100, 0, 0, GamesWidgetAlignment.Center, false);
        Assert.Throws<ArgumentException>(() => invalidAnchor.Resolve(1000, 1000));
        var invalidMin = new GamesWidgetLayout(0.5, 0.5, 0, 0, 100, 100, 200, 0, GamesWidgetAlignment.Center, false);
        Assert.Throws<ArgumentException>(() => invalidMin.Resolve(1000, 1000));
        var invalidViewport = GamesWidgetLayout.Centered(100, 100);
        Assert.Throws<ArgumentException>(() => invalidViewport.Resolve(0, 1000));
    }

    [Fact]
    public void Snap_and_minimum_size_are_applied()
    {
        var layout = GamesWidgetLayout.Centered(123, 45, 60, 30, true);
        var bounds = layout.Resolve(1000, 1000, 50);
        Assert.Equal(150, bounds.Width);
        Assert.Equal(50, bounds.Height);
        Assert.True(bounds.Width >= 60);
        Assert.True(bounds.Height >= 30);
    }

    [Fact]
    public void Unknown_alignment_and_overflowing_snap_do_not_admit_nonfinite_geometry()
    {
        var layout = GamesWidgetLayout.Centered(100, 100, snapToGrid: true);
        var unsupported = layout with { Alignment = (GamesWidgetAlignment)99 };
        Assert.Throws<ArgumentException>(() => unsupported.Resolve(1000, 800));
        var bounds = (layout with { OffsetX = double.MaxValue, AnchorX = 1 })
            .Resolve(double.MaxValue, 800, double.Epsilon);
        Assert.True(double.IsFinite(bounds.X));
        Assert.True(double.IsFinite(bounds.Y));
        Assert.True(double.IsFinite(bounds.Width));
        Assert.True(double.IsFinite(bounds.Height));
    }

    [Fact]
    public void Deterministic_results_are_stable()
    {
        var layout = GamesWidgetLayout.Centered(200, 100, 0, 0, true);
        var first = layout.Resolve(1920, 1080, 10);
        var second = layout.Resolve(1920, 1080, 10);
        Assert.Equal(first, second);
    }
}
