using Xunit;

namespace CakeOS.Canvas.App.Tests;

public sealed class CanvasViewportTests : IDisposable
{
    private readonly StubCanvasSession _session = new();
    private readonly CanvasController _controller;

    public CanvasViewportTests()
    {
        _controller = new CanvasController(() => _session);
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public void RewriteViewBoxMapsDocumentViewportToSvgSpace()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0.000 0.000 1000 500\"><rect/></svg>";
        var bounds = new CanvasDocumentBounds(10, 20, 1000, 500);
        var viewport = new CanvasViewportRect(110, 70, 500, 250);
        var rewritten = CanvasController.RewriteViewBox(svg, bounds, viewport);
        Assert.Contains("viewBox=\"100 50 500 250\"", rewritten);
        Assert.Contains("<rect/>", rewritten);
    }

    [Fact]
    public void RewriteViewBoxKeepsSvgWithoutViewBox()
    {
        var svg = "<svg><rect/></svg>";
        var rewritten = CanvasController.RewriteViewBox(
            svg, new CanvasDocumentBounds(0, 0, 100, 100), new CanvasViewportRect(0, 0, 50, 50));
        Assert.Equal(svg, rewritten);
    }

    [Fact]
    public void BlankDocumentMapsOneToOneWithInsideCheck()
    {
        var inside = _controller.SurfaceToDocument(30, 40, 800, 600, bounds: null, requireInside: true);
        Assert.Equal((30, 40), inside);
        Assert.Null(_controller.SurfaceToDocument(900, 40, 800, 600, bounds: null, requireInside: true));
        Assert.Equal((900, 40), _controller.SurfaceToDocument(900, 40, 800, 600, bounds: null, requireInside: false));
    }

    [Fact]
    public void TouchNeverStartsStrokesButStartsPans()
    {
        var bounds = new CanvasDocumentBounds(0, 0, 1000, 1000);
        Assert.False(_controller.BeginStrokeAt(CanvasPointerKind.Touch, 100, 100, 0.5, 800, 600, bounds, insideViewport: true));
        Assert.False(_controller.StrokeActive);
        Assert.True(_controller.BeginPanAt(100, 100));
        Assert.True(_controller.IsPanning);
        _controller.UpdatePanTo(120, 110, 800, 600, bounds);
        _controller.EndPan(800, 600, bounds);
        Assert.False(_controller.IsPanning);
    }

    [Fact]
    public void StrokeOutsideViewportIsRejected()
    {
        var bounds = new CanvasDocumentBounds(0, 0, 100, 100);
        Assert.False(_controller.BeginStrokeAt(CanvasPointerKind.Mouse, 500, 500, 0.5, 200, 200, bounds, insideViewport: false));
    }

    [Fact]
    public void CommittedStrokeMarksDirtyAndRefreshesFrame()
    {
        var before = _controller.FrameVersion;
        Assert.True(_controller.BeginStrokeAt(CanvasPointerKind.Pen, 10, 10, 0.7, 200, 200, null, insideViewport: true));
        _controller.UpdateStrokeAt(20, 20, 0.7, 200, 200, null);
        Assert.True(_controller.EndStrokeAt(CanvasPointerKind.Pen, 30, 30, 0.7, 200, 200, null));
        Assert.True(_controller.IsDirty);
        Assert.True(_controller.FrameVersion > before);
        Assert.NotNull(_controller.CurrentFrameSvg);
    }

    [Fact]
    public void EmptyDocumentClearsFrame()
    {
        _session.ReturnEmptyFrame = true;
        _controller.RefreshFrame(800, 600);
        Assert.Null(_controller.CurrentFrameSvg);
        Assert.Null(_controller.LastBounds);
    }

    [Fact]
    public void ZoomAtPointStaysClampedAndKeepsAnchor()
    {
        var bounds = new CanvasDocumentBounds(0, 0, 1000, 1000);
        _controller.ZoomAtPoint(400, 300, 100, 800, 600, bounds);
        Assert.Equal(CanvasController.MaxZoom, _controller.Zoom);
        _controller.ZoomAtPoint(400, 300, -100, 800, 600, bounds);
        Assert.Equal(CanvasController.MinZoom, _controller.Zoom);
    }

    [Fact]
    public void FitToContentCentersAndFits()
    {
        var bounds = new CanvasDocumentBounds(0, 0, 2000, 1000);
        _controller.FitToContent(800, 600, bounds);
        Assert.Equal(0.4, _controller.Zoom, precision: 6);
    }

    [Fact]
    public void ViewportSizePreservesAspect()
    {
        // Wider-than-target documents are height-driven, preserving aspect.
        var bounds = new CanvasDocumentBounds(0, 0, 1600, 900);
        var (w, h) = CanvasController.ViewportSize(800, 600, bounds, zoom: 1);
        Assert.Equal(1200, w, precision: 6);
        Assert.Equal(900, h, precision: 6);
        Assert.Equal(800d / 600d, w / h, precision: 6);
    }

    [Fact]
    public void ResetViewportClearsGestureAndFrame()
    {
        _controller.BeginPanAt(5, 5);
        _controller.ResetViewport();
        Assert.False(_controller.IsPanning);
        Assert.False(_controller.StrokeActive);
        Assert.Null(_controller.CurrentFrameSvg);
        Assert.Null(_controller.LastBounds);
    }
}
