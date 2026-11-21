using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class WorkspaceWindowPersistenceTests
{
    [AvaloniaFact]
    public void ApplyWindowBounds_restores_valid_geometry_and_clamps_to_window_minimums()
    {
        var window = new Window { MinWidth = 420, MinHeight = 320, Width = 640, Height = 480 };

        WorkspaceWindowService.ApplyWindowBounds(window, """{"X":120,"Y":80,"Width":300,"Height":200}""");

        Assert.Equal(420, window.Width);
        Assert.Equal(320, window.Height);
        Assert.Equal(120, window.Position.X);
        Assert.Equal(80, window.Position.Y);
    }

    [AvaloniaFact]
    public void ApplyWindowBounds_ignores_corrupt_or_invalid_geometry()
    {
        var window = new Window { MinWidth = 420, MinHeight = 320, Width = 640, Height = 480 };

        WorkspaceWindowService.ApplyWindowBounds(window, "not-json");
        WorkspaceWindowService.ApplyWindowBounds(window, """{"X":0,"Y":0,"Width":-10,"Height":0}""");

        Assert.Equal(640, window.Width);
        Assert.Equal(480, window.Height);
    }

    [AvaloniaFact]
    public void ApplyWindowBounds_clamps_a_position_stranded_off_every_screen_back_into_reach()
    {
        // The 20 November native defect: a frame saved above the top edge (client root origin
        // negative) came back with the whole tab strip unreachable.
        var workingArea = new PixelRect(0, 0, 1920, 1080);
        var window = new Window { MinWidth = 420, MinHeight = 320, Width = 640, Height = 480 };

        WorkspaceWindowService.ApplyWindowBounds(window, """{"X":-2884,"Y":-1340,"Width":640,"Height":480}""", workingArea);

        Assert.Equal(0, window.Position.X);
        Assert.Equal(0, window.Position.Y);

        var past = new Window { MinWidth = 420, MinHeight = 320, Width = 640, Height = 480 };
        WorkspaceWindowService.ApplyWindowBounds(past, """{"X":5000,"Y":5000,"Width":640,"Height":480}""", workingArea);
        Assert.Equal(1720, past.Position.X); // 1920 - 200 title-strip reach
        Assert.Equal(1016, past.Position.Y); // 1080 - 64 title reach
    }

    [AvaloniaFact]
    public void ApplyWindowBounds_keeps_a_reachable_saved_position_and_its_fallback()
    {
        var window = new Window { MinWidth = 420, MinHeight = 320, Width = 640, Height = 480 };
        WorkspaceWindowService.ApplyWindowBounds(window, """{"X":120,"Y":140,"Width":640,"Height":480}""",
            new PixelRect(0, 0, 1920, 1080));
        Assert.Equal(120, window.Position.X);
        Assert.Equal(140, window.Position.Y);

        // Multi-monitor teardown race: with no readable working area the saved value is still honoured.
        var blind = new Window { MinWidth = 420, MinHeight = 320, Width = 640, Height = 480 };
        WorkspaceWindowService.ApplyWindowBounds(blind, """{"X":-500,"Y":-200,"Width":640,"Height":480}""", null);
        Assert.Equal(-500, blind.Position.X);
        Assert.Equal(-200, blind.Position.Y);
    }

    [AvaloniaFact]
    public void ResolveReachablePosition_fixes_the_observed_fractional_scale_startup_strand()
    {
        // 21 Nov native evidence: a 2850x1820 physical frame at exactly minus the centring delta,
        // tab strip above the display; single working area 4384x2466.
        var areas = new[] { new PixelRect(0, 0, 4384, 2466) };
        var fixedTo = WorkspaceWindowService.ResolveReachablePosition(new PixelSize(2850, 1820), new PixelPoint(-767, -348), areas);
        Assert.NotNull(fixedTo);
        Assert.Equal(new PixelPoint(767, 323), fixedTo!.Value); // centred; strip reachable and draggable

        // A normal reachable window is left alone.
        Assert.Null(WorkspaceWindowService.ResolveReachablePosition(new PixelSize(1600, 1000), new PixelPoint(200, 120), areas));

        // Stranded past the right edge returns to the nearest area.
        var east = WorkspaceWindowService.ResolveReachablePosition(new PixelSize(1600, 1000), new PixelPoint(4200, 100), areas);
        Assert.Equal(new PixelPoint(1392, 733), east!.Value);

        // No screens: fail open, change nothing.
        Assert.Null(WorkspaceWindowService.ResolveReachablePosition(new PixelSize(1600, 1000), new PixelPoint(-767, -348), Array.Empty<PixelRect>()));
    }
}
