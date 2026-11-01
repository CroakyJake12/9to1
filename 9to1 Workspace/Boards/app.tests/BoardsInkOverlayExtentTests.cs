// Regression coverage for the crash measured on the running standalone Boards app on 1 November 2026.
// Pressing the pointer to start an ink stroke aborted the process: the launcher recorded exit 134
// (SIGABRT) and run15.log carries
//   System.ArgumentException: NaN is not a valid value for 'MinWidth'.
//     at Avalonia.Layout.Layoutable.set_MinWidth(Double value)
//     at CakeOS.Apps.Boards.App.BoardInkOverlayRenderer.ApplyOverlayExtent(Panel, Double)  : line 69
//     at CakeOS.Apps.Boards.App.BoardInkInteractionController.RenderLiveStroke()          : line 202
//     at CakeOS.Apps.Boards.App.BoardInkInteractionController.OnPointerPressed(...)       : line 140
// The overlay extent code used double.NaN as its "no constraint" marker in two places. Avalonia accepts
// NaN for Height (auto) but rejects it for MinWidth, which validates a finite non-negative value, so the
// marker itself was the defect. "No width floor" is 0, which is the convention BlockRenderer already
// uses for the absolute block host (BlockRenderer.cs:149).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsInkOverlayExtentTests
{
    [Fact]
    public void GrowingTheInkLayerWithoutAnyMeasuredSiblingDoesNotWriteNaNintoMinWidth()
    {
        var observed = TestUiThread.Run(() =>
        {
            var host = new Canvas { Width = 700, Height = 500 };
            var overlay = new Canvas();
            var unmeasuredSibling = new Border();          // never arranged: Bounds stay empty
            host.Children.Add(unmeasuredSibling);
            host.Children.Add(overlay);
            var window = new Window { Content = host, Width = 700, Height = 500 };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            try
            {
                BoardInkOverlayRenderer.ApplyOverlayExtent(overlay, 400);
                return (Height: overlay.Height, MinWidth: overlay.MinWidth);
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        });
        Assert.Equal(400d, observed.Height, 0.5);
        Assert.Equal(0d, observed.MinWidth);               // was double.NaN -> Avalonia rejected it
    }

    [Fact]
    public void ShrinkingTheInkLayerBackDoesNotWriteNaNintoMinWidthEither()
    {
        var observed = TestUiThread.Run(() =>
        {
            var host = new Canvas { Width = 700, Height = 500 };
            var content = new Border { Width = 300, Height = 120 };
            var overlay = new Canvas();
            host.Children.Add(content);
            host.Children.Add(overlay);
            var window = new Window { Content = host, Width = 700, Height = 500 };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Assert.True(content.Bounds.Height > 0, "the sibling must be measured for this branch to be real");
                BoardInkOverlayRenderer.ApplyOverlayExtent(overlay, 420);   // grow
                // The shrink branch compares against the *arranged* height, so the grow must actually be
                // laid out before the second call, otherwise the second call takes the grow branch again.
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var grown = (Height: overlay.Height, Bounds: content.Bounds.Height, MinWidth: overlay.MinWidth);
                BoardInkOverlayRenderer.ApplyOverlayExtent(overlay, 0);     // shrink back below the grown height
                return (Grown: grown, FinalHeight: overlay.Height, FinalMinWidth: overlay.MinWidth);
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        });
        Assert.Equal(420d, observed.Grown.Height, 0.5);
        Assert.True(double.IsNaN(observed.FinalHeight), "the layer returns to auto height, which Height accepts");
        Assert.Equal(0d, observed.FinalMinWidth);
    }
}
