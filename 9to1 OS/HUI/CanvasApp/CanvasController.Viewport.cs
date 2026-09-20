using System.Globalization;

namespace CakeOS.Canvas.App;

/// <summary>Pointer device for viewport gesture arbitration.</summary>
public enum CanvasPointerKind
{
    Mouse,
    Pen,
    Touch,
}

/// <summary>Document-space viewport rectangle.</summary>
public readonly record struct CanvasViewportRect(double X, double Y, double Width, double Height);

/// <summary>
/// Viewport half of <see cref="CanvasController"/>: document transforms,
/// stroke/pan/zoom gestures and viewport-cropped SVG frames. All state stays
/// in document coordinates; the HUI tree only displays the produced frame.
/// Fully covered by controller unit tests (stub session).
/// </summary>
public sealed partial class CanvasController
{
    private double _viewportCenterX;
    private double _viewportCenterY;
    private bool _viewportInitialized;
    private bool _strokeActive;
    private (double X, double Y)? _panLast;

    /// <summary>Latest viewport-cropped frame, or null while the document is blank.</summary>
    public string? CurrentFrameSvg { get; private set; }

    /// <summary>Bounds of the last rendered frame (viewport input mapping).</summary>
    public CanvasDocumentBounds? LastBounds { get; private set; }

    /// <summary>True while a pan gesture is in progress.</summary>
    public bool IsPanning => _panLast is not null;

    /// <summary>Bumps on every frame change so hosts can refresh image caches.</summary>
    public long FrameVersion { get; private set; }

    public bool StrokeActive => _strokeActive;

    public void ResetViewport()
    {
        _viewportInitialized = false;
        _strokeActive = false;
        _panLast = null;
        CurrentFrameSvg = null;
        LastBounds = null;
        FrameVersion++;
        RaiseChanged();
    }

    public static CanvasViewportRect ViewportFor(
        double targetWidth, double targetHeight, CanvasDocumentBounds bounds, double zoom,
        ref double centerX, ref double centerY, ref bool initialized)
    {
        var size = ViewportSize(targetWidth, targetHeight, bounds, zoom);
        if (!initialized)
        {
            centerX = bounds.X;
            centerY = bounds.Y;
            initialized = true;
        }
        var minCenterX = bounds.X + size.Width / 2d;
        var maxCenterX = bounds.X + bounds.Width - size.Width / 2d;
        var minCenterY = bounds.Y + size.Height / 2d;
        var maxCenterY = bounds.Y + bounds.Height - size.Height / 2d;
        // A viewport larger than the document cannot clamp: center it instead.
        centerX = size.Width >= bounds.Width
            ? bounds.X + bounds.Width / 2d
            : Math.Clamp(centerX, minCenterX, maxCenterX);
        centerY = size.Height >= bounds.Height
            ? bounds.Y + bounds.Height / 2d
            : Math.Clamp(centerY, minCenterY, maxCenterY);
        return new CanvasViewportRect(
            centerX - size.Width / 2d,
            centerY - size.Height / 2d,
            size.Width,
            size.Height);
    }

    public static (double Width, double Height) ViewportSize(
        double targetWidth, double targetHeight, CanvasDocumentBounds bounds, double zoom)
    {
        if (targetWidth <= 0 || targetHeight <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return (0, 0);
        var targetAspect = targetWidth / targetHeight;
        var documentAspect = bounds.Width / bounds.Height;
        double baseWidth;
        double baseHeight;
        if (documentAspect > targetAspect)
        {
            baseHeight = bounds.Height;
            baseWidth = baseHeight * targetAspect;
        }
        else
        {
            baseWidth = bounds.Width;
            baseHeight = baseWidth / targetAspect;
        }
        var clampedZoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        return (Math.Max(1d, baseWidth / clampedZoom), Math.Max(1d, baseHeight / clampedZoom));
    }

    /// <summary>Map a surface point to document coordinates (blank doc maps 1:1).</summary>
    public (double X, double Y)? SurfaceToDocument(
        double surfaceX, double surfaceY, double targetWidth, double targetHeight,
        CanvasDocumentBounds? bounds, bool requireInside)
    {
        if (bounds is null)
        {
            if (requireInside && (surfaceX < 0 || surfaceY < 0 || surfaceX > targetWidth || surfaceY > targetHeight))
                return null;
            return (surfaceX, surfaceY);
        }
        if (targetWidth <= 0 || targetHeight <= 0)
            return null;
        if (requireInside && (surfaceX < 0 || surfaceY < 0 || surfaceX > targetWidth || surfaceY > targetHeight))
            return null;
        var viewport = ViewportFor(targetWidth, targetHeight, bounds.Value, Zoom,
            ref _viewportCenterX, ref _viewportCenterY, ref _viewportInitialized);
        var x = Math.Clamp(surfaceX / targetWidth, 0d, 1d);
        var y = Math.Clamp(surfaceY / targetHeight, 0d, 1d);
        return (viewport.X + x * viewport.Width, viewport.Y + y * viewport.Height);
    }

    public bool BeginStrokeAt(
        CanvasPointerKind kind, double x, double y, double pressure,
        double targetWidth, double targetHeight, CanvasDocumentBounds? bounds,
        bool insideViewport, double tiltX = 0, double tiltY = 0)
    {
        ThrowIfDisposed();
        if (_strokeActive || _panLast is not null)
            return false;
        if (kind == CanvasPointerKind.Touch)
            return false;
        if (!insideViewport)
            return false;
        var document = SurfaceToDocument(x, y, targetWidth, targetHeight, bounds, requireInside: true);
        if (document is null)
            return false;
        Session.BeginStroke(document.Value.X, document.Value.Y, pressure, tiltX, tiltY);
        _strokeActive = true;
        return true;
    }

    public void UpdateStrokeAt(
        double x, double y, double pressure,
        double targetWidth, double targetHeight, CanvasDocumentBounds? bounds,
        double tiltX = 0, double tiltY = 0)
    {
        ThrowIfDisposed();
        if (!_strokeActive)
            return;
        var document = SurfaceToDocument(x, y, targetWidth, targetHeight, bounds, requireInside: false);
        if (document is null)
            return;
        Session.UpdateStroke(document.Value.X, document.Value.Y, pressure, tiltX, tiltY);
    }

    /// <summary>Returns true when a stroke committed (frame refreshed).</summary>
    public bool EndStrokeAt(
        CanvasPointerKind kind, double x, double y, double pressure,
        double targetWidth, double targetHeight, CanvasDocumentBounds? bounds,
        double tiltX = 0, double tiltY = 0)
    {
        ThrowIfDisposed();
        if (!_strokeActive)
            return false;
        var document = SurfaceToDocument(x, y, targetWidth, targetHeight, bounds, requireInside: false);
        if (document is null)
        {
            _strokeActive = false;
            return false;
        }
        Session.EndStroke(document.Value.X, document.Value.Y, pressure, tiltX, tiltY);
        _strokeActive = false;
        RefreshFrame(targetWidth, targetHeight);
        MarkDirty();
        SetStatus($"{Tool} stroke committed; {Zoom:0.#}x");
        Console.WriteLine(
            $"CANVAS_RNOTE_POINTER_STROKE_COMMITTED pointer={kind} tool={Tool} pressure={pressure:0.###} zoom={Zoom:0.###}");
        return true;
    }

    public bool BeginPanAt(double x, double y)
    {
        ThrowIfDisposed();
        if (_strokeActive || _panLast is not null)
            return false;
        _panLast = (x, y);
        return true;
    }

    public void UpdatePanTo(
        double x, double y, double targetWidth, double targetHeight, CanvasDocumentBounds? bounds)
    {
        ThrowIfDisposed();
        if (_panLast is null || bounds is null || targetWidth <= 0 || targetHeight <= 0)
            return;
        var viewport = ViewportFor(targetWidth, targetHeight, bounds.Value, Zoom,
            ref _viewportCenterX, ref _viewportCenterY, ref _viewportInitialized);
        var (lastX, lastY) = _panLast.Value;
        _viewportCenterX -= (x - lastX) / targetWidth * viewport.Width;
        _viewportCenterY -= (y - lastY) / targetHeight * viewport.Height;
        _panLast = (x, y);
        _ = ViewportFor(targetWidth, targetHeight, bounds.Value, Zoom,
            ref _viewportCenterX, ref _viewportCenterY, ref _viewportInitialized);
        SetStatus($"Pan / {Zoom:0.#}x");
        RefreshFrame(targetWidth, targetHeight);
    }

    public void EndPan(double targetWidth, double targetHeight, CanvasDocumentBounds? bounds)
    {
        ThrowIfDisposed();
        _panLast = null;
        if (bounds is null)
            return;
        var viewport = ViewportFor(targetWidth, targetHeight, bounds.Value, Zoom,
            ref _viewportCenterX, ref _viewportCenterY, ref _viewportInitialized);
        Console.WriteLine(
            $"CANVAS_RNOTE_VIEWPORT_PAN_COMMITTED zoom={Zoom:0.###} x={viewport.X:0.###} y={viewport.Y:0.###} width={viewport.Width:0.###} height={viewport.Height:0.###}");
        RaiseChanged();
    }

    public void ZoomAtPoint(
        double surfaceX, double surfaceY, double wheelDelta,
        double targetWidth, double targetHeight, CanvasDocumentBounds? bounds)
    {
        ThrowIfDisposed();
        if (bounds is null || targetWidth <= 0 || targetHeight <= 0 || Math.Abs(wheelDelta) < 0.0001d)
            return;
        var oldViewport = ViewportFor(targetWidth, targetHeight, bounds.Value, Zoom,
            ref _viewportCenterX, ref _viewportCenterY, ref _viewportInitialized);
        var u = Math.Clamp(surfaceX / targetWidth, 0d, 1d);
        var v = Math.Clamp(surfaceY / targetHeight, 0d, 1d);
        var documentX = oldViewport.X + u * oldViewport.Width;
        var documentY = oldViewport.Y + v * oldViewport.Height;
        var nextZoom = Math.Clamp(Zoom * Math.Pow(1.25d, wheelDelta), MinZoom, MaxZoom);
        if (Math.Abs(nextZoom - Zoom) < 0.0001d)
            return;
        Zoom = nextZoom;
        var (nextWidth, nextHeight) = ViewportSize(targetWidth, targetHeight, bounds.Value, nextZoom);
        _viewportCenterX = documentX - (u - 0.5d) * nextWidth;
        _viewportCenterY = documentY - (v - 0.5d) * nextHeight;
        var viewport = ViewportFor(targetWidth, targetHeight, bounds.Value, nextZoom,
            ref _viewportCenterX, ref _viewportCenterY, ref _viewportInitialized);
        SetStatus($"Zoom {Zoom:0.#}x");
        Console.WriteLine(
            $"CANVAS_RNOTE_VIEWPORT_ZOOM zoom={Zoom:0.###} x={viewport.X:0.###} y={viewport.Y:0.###} width={viewport.Width:0.###} height={viewport.Height:0.###}");
        RefreshFrame(targetWidth, targetHeight);
    }

    public void FitToContent(double targetWidth, double targetHeight, CanvasDocumentBounds? bounds)
    {
        ThrowIfDisposed();
        if (bounds is null || targetWidth <= 0 || targetHeight <= 0)
            return;
        var fitZoom = Math.Min(
            targetWidth / Math.Max(1d, bounds.Value.Width),
            targetHeight / Math.Max(1d, bounds.Value.Height));
        Zoom = Math.Clamp(fitZoom, MinZoom, MaxZoom);
        _viewportCenterX = bounds.Value.X + bounds.Value.Width / 2d;
        _viewportCenterY = bounds.Value.Y + bounds.Value.Height / 2d;
        _viewportInitialized = true;
        RefreshFrame(targetWidth, targetHeight);
    }

    /// <summary>
    /// Re-render the frame for the given surface size, rewriting the SVG
    /// viewBox to the current viewport so hosts display the cropped region.
    /// Blank documents clear the frame (paper placeholder).
    /// </summary>
    public void RefreshFrame(double targetWidth, double targetHeight)
    {
        ThrowIfDisposed();
        try
        {
            var frame = Session.RenderSvg();
            if (frame.Bounds.Width <= 0 || frame.Bounds.Height <= 0)
                throw new InvalidOperationException("Canvas native frame returned invalid document bounds.");
            LastBounds = frame.Bounds;
            var viewport = ViewportFor(targetWidth, targetHeight, frame.Bounds, Zoom,
                ref _viewportCenterX, ref _viewportCenterY, ref _viewportInitialized);
            CurrentFrameSvg = RewriteViewBox(frame.Svg, frame.Bounds, viewport);
            FrameVersion++;
            Console.WriteLine(
                $"CANVAS_RNOTE_HUI_RENDER_READY abi=3 format=svg coordinate=document width={frame.Bounds.Width:0.###} height={frame.Bounds.Height:0.###}");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("empty", StringComparison.OrdinalIgnoreCase))
        {
            CurrentFrameSvg = null;
            LastBounds = null;
            FrameVersion++;
        }
        RaiseChanged();
    }

    public static string RewriteViewBox(string svg, CanvasDocumentBounds bounds, CanvasViewportRect viewport)
    {
        const string key = "viewBox=\"";
        var start = svg.IndexOf(key, StringComparison.Ordinal);
        if (start < 0)
            return svg;
        var end = svg.IndexOf('"', start + key.Length);
        if (end < 0)
            return svg;
        var replacement = string.Create(CultureInfo.InvariantCulture,
            $"{viewport.X - bounds.X:0.###} {viewport.Y - bounds.Y:0.###} {viewport.Width:0.###} {viewport.Height:0.###}");
        return svg.Substring(0, start + key.Length) + replacement + svg.Substring(end);
    }
}
