using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Haven.Application.Canvas;
using CakeOS.Cui.Runtime;
using System.Runtime.InteropServices;

namespace HavenOS.Apps.Canvas;

/// <summary>A retained native viewport over the owning structured Canvas document.
/// The route owns the document lifetime; this control owns only its operation raster.</summary>
public class CanvasNativeViewport : UserControl, IDisposable
{
    private readonly CanvasRnoteDocument _document;
    private readonly ICuiSceneReadiness _readiness;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly SelectionFeedbackControl _selectionFeedback;
    private readonly IDisposable _selectionBrushBinding;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CanvasOriginalWorkOwner _originalWork = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WriteableBitmap? _bitmap;
    private RnoteRenderFrame? _frame;
    private bool _disposed;
    private double _zoom = 1;
    private Vector _pan;
    private IPointer? _panPointer;
    private Point _panStart;
    private bool _panWithPrimaryButton;
    public bool PanWithPrimaryButton
    {
        get => _panWithPrimaryButton;
        set { Dispatcher.UIThread.VerifyAccess(); if (_panWithPrimaryButton == value) return; ReleasePan(); _panWithPrimaryButton = value; }
    }
    public event EventHandler? ViewChanged;
    public double ViewZoom => _zoom;
    public Vector ViewPan => _pan;

    public CanvasNativeViewport(CanvasRnoteDocument document, ICuiSceneReadiness readiness)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _selectionFeedback = new SelectionFeedbackControl(this) { IsHitTestVisible = false, ClipToBounds = true };
        _selectionBrushBinding = _selectionFeedback.Bind(SelectionFeedbackControl.OutlineBrushProperty,
            _selectionFeedback.GetResourceObservable("CuiAccentBrush"));
        ClipToBounds = true;
        _image.RenderTransformOrigin = RelativePoint.TopLeft;
        Content = new Grid { Children = { _image, _status, _selectionFeedback } };
        PointerWheelChanged += Wheel;
        PointerPressed += BeginPan;
        PointerMoved += ContinuePan;
        PointerReleased += EndPan;
        PointerCaptureLost += LostPan;
        DetachedFromVisualTree += (_, _) => ReleasePan();
    }

    public Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
        => _originalWork.RunOriginalAsync(RefreshOriginalAsync, cancellationToken);
    private async Task<bool> RefreshOriginalAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _gate.WaitAsync(operation.Token);
        try
        {
            var before = await _originalWork.ObserveOriginalAsync(_readiness.CheckAsync(operation.Token).AsTask());
            if (before.State != CuiSceneAvailabilityState.Ready) { Clear(before.Message); return false; }
            var snapshot = _document.Identity;
            var output = await _originalWork.ObserveOriginalAsync(Task.Run(() =>
            {
                var frame = _document.Render();
                if (!double.IsFinite(frame.X) || !double.IsFinite(frame.Y) || !double.IsFinite(frame.Width) || !double.IsFinite(frame.Height) || frame.Width <= 0 || frame.Height <= 0)
                    throw new InvalidDataException("Canvas returned invalid document bounds.");
                var scale = 1600 / Math.Max(frame.Width, frame.Height);
                var width = Math.Clamp((int)Math.Round(frame.Width * scale), 1, 1600);
                var height = Math.Clamp((int)Math.Round(frame.Height * scale), 1, 1600);
                return (Frame: frame, Raster: CanvasSvgRasterizer.Render(frame.Svg, width, height));
            }, operation.Token));
            operation.Token.ThrowIfCancellationRequested();
            var current = _document.Identity;
            var after = await _originalWork.ObserveOriginalAsync(_readiness.CheckAsync(operation.Token).AsTask());
            operation.Token.ThrowIfCancellationRequested();
            if (after.State != CuiSceneAvailabilityState.Ready) { Clear(after.Message); return false; }
            if (current.ArtifactId != snapshot.ArtifactId || current.RevisionId != snapshot.RevisionId)
            {
                Clear("Canvas changed while rendering. Refresh the current document.");
                return false;
            }
            var raster = output.Raster;
            var bitmap = new WriteableBitmap(new(raster.Width, raster.Height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            try
            {
                using (var buffer = bitmap.Lock())
                    for (var row = 0; row < raster.Height; row++)
                        Marshal.Copy(raster.PremultipliedBgra, row * raster.Stride,
                            IntPtr.Add(buffer.Address, row * buffer.RowBytes), raster.Width * 4);
                var old = _bitmap;
                _bitmap = bitmap;
                _frame = output.Frame;
                _image.Source = bitmap;
                _status.IsVisible = false;
                old?.Dispose();
                _selectionFeedback.Reproject();
                return true;
            }
            catch { bitmap.Dispose(); throw; }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or DllNotFoundException or EntryPointNotFoundException or UnauthorizedAccessException or InvalidOperationException)
        {
            Clear(error.Message);
            return false;
        }
        finally { _gate.Release(); }
    }

    public Point? ToDocumentPoint(Point viewportPosition)
    {
        if (_disposed || _bitmap is null || _frame is null || Bounds.Width <= 0 || Bounds.Height <= 0) return null;
        if (!double.IsFinite(viewportPosition.X) || !double.IsFinite(viewportPosition.Y) ||
            viewportPosition.X < 0 || viewportPosition.Y < 0 || viewportPosition.X > Bounds.Width || viewportPosition.Y > Bounds.Height) return null;
        viewportPosition = new Point((viewportPosition.X - _pan.X) / _zoom, (viewportPosition.Y - _pan.Y) / _zoom);
        var imageScale = Math.Min(Bounds.Width / _bitmap.PixelSize.Width, Bounds.Height / _bitmap.PixelSize.Height);
        var width = _bitmap.PixelSize.Width * imageScale;
        var height = _bitmap.PixelSize.Height * imageScale;
        var left = (Bounds.Width - width) / 2;
        var top = (Bounds.Height - height) / 2;
        if (viewportPosition.X < left || viewportPosition.X > left + width || viewportPosition.Y < top || viewportPosition.Y > top + height) return null;
        return new(_frame.X + (viewportPosition.X - left) / width * _frame.Width,
            _frame.Y + (viewportPosition.Y - top) / height * _frame.Height);
    }

    /// <summary>Changes only this native view, never the donor document or durable Files revision.</summary>
    public bool ZoomAt(double factor, Point anchor)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || _bitmap is null || !double.IsFinite(factor) || factor <= 0 ||
            !double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y)) return false;
        var zoom = Math.Clamp(_zoom * factor, 0.25, 8);
        var ratio = zoom / _zoom;
        var pan = new Vector(anchor.X - (anchor.X - _pan.X) * ratio,
            anchor.Y - (anchor.Y - _pan.Y) * ratio);
        if (!ValidPan(pan)) return false;
        _zoom = zoom; _pan = pan; ApplyView(); return true;
    }

    public bool PanBy(Vector delta)
    {
        Dispatcher.UIThread.VerifyAccess();
        var pan = _pan + delta;
        if (_disposed || _bitmap is null || !ValidPan(pan)) return false;
        _pan = pan; ApplyView(); return true;
    }

    public void ResetView()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        _zoom = 1; _pan = default; ApplyView();
    }

    /// <summary>Transient accelerated-surface feedback from the authoritative donor path.
    /// No guessed paint bounds, artifact mutation or resource authority is supplied here.</summary>
    public bool SetSelectionFeedback(CanvasSelectionPath? path, double deltaX = 0, double deltaY = 0, bool visible = true)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return false;
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY)) throw new ArgumentException("Selection offsets must be finite.");
        if (path is not null && (path.ArtifactId == Guid.Empty || path.RevisionId == Guid.Empty || path.StrokeId == Guid.Empty ||
            path.Geometry is null || path.Geometry.Segments is null)) throw new ArgumentException("Selection feedback requires its exact retained path identity.");
        return _selectionFeedback.Set(path, new(deltaX, deltaY), visible);
    }

    // Read-only geometry observation used by focused native projection checks. These are
    // contour bounds, not claimed bounds of a pressure-varying painted stroke.
    internal bool TryGetSelectionFeedbackBounds(out Rect original, out Rect preview)
        => _selectionFeedback.TryGetBounds(out original, out preview);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty && _selectionFeedback is not null)
        {
            _selectionFeedback.Reproject();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool ValidPan(Vector pan) => double.IsFinite(pan.X) && double.IsFinite(pan.Y) &&
        Math.Abs(pan.X) <= 1_000_000 && Math.Abs(pan.Y) <= 1_000_000;
    private void ApplyView()
    {
        _image.RenderTransform = new MatrixTransform(new Matrix(_zoom, 0, 0, _zoom, _pan.X, _pan.Y));
        _selectionFeedback.Reproject();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Wheel(object? sender, PointerWheelEventArgs args)
    {
        if (args.Delta.Y != 0 && ZoomAt(Math.Pow(1.2, Math.Clamp(args.Delta.Y, -8, 8)), args.GetPosition(this))) args.Handled = true;
    }
    private void BeginPan(object? sender, PointerPressedEventArgs args)
    {
        if (_disposed || _bitmap is null || _panPointer is not null) return;
        var point = args.GetCurrentPoint(this);
        if (!(PanWithPrimaryButton && point.Properties.IsLeftButtonPressed) &&
            !(args.Pointer.Type == PointerType.Mouse && point.Properties.IsMiddleButtonPressed)) return;
        _panPointer = args.Pointer; _panStart = point.Position;
        args.Pointer.Capture(this); args.PreventGestureRecognition(); args.Handled = true;
    }
    private void ContinuePan(object? sender, PointerEventArgs args)
    {
        if (_panPointer != args.Pointer) return;
        var position = args.GetPosition(this);
        PanBy(position - _panStart); _panStart = position; args.Handled = true;
    }
    private void EndPan(object? sender, PointerReleasedEventArgs args)
    { if (_panPointer == args.Pointer) { ReleasePan(); args.Handled = true; } }
    private void LostPan(object? sender, PointerCaptureLostEventArgs args) => _panPointer = null;
    private void ReleasePan()
    { var pointer = _panPointer; _panPointer = null; pointer?.Capture(null); }

    private void Clear(string message)
    {
        ReleasePan();
        _selectionFeedback.Clear();
        _image.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _frame = null;
        _status.Text = message;
        _status.IsVisible = true;
    }

    public Task CloseAndDrainAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.InvokeAsync(CloseAndDrainAsync);
        _disposed = true;
        return _originalWork.CloseAndDrainAsync(() =>
        {
            var errors = new List<Exception>();
            try { _lifetime.Cancel(); } catch (Exception error) { errors.Add(error); }
            try { Clear("Canvas viewport closed."); } catch (Exception error) { errors.Add(error); }
            try { _selectionBrushBinding.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { _lifetime.Dispose(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Canvas viewport native cleanup failed.", errors);
            return Task.CompletedTask;
        });
    }
    public void Dispose() { _ = CloseAndDrainAsync(); }

    /// <summary>Intrinsic drawing feedback inside the CUI-owned CanvasSpatialSurface. All
    /// curves come from retained donor data; the brush is a live CUI semantic resource.</summary>
    private sealed class SelectionFeedbackControl(CanvasNativeViewport owner) : Control
    {
        internal static readonly StyledProperty<IBrush?> OutlineBrushProperty =
            AvaloniaProperty.Register<SelectionFeedbackControl, IBrush?>(nameof(OutlineBrush));
        public IBrush? OutlineBrush { get => GetValue(OutlineBrushProperty); set => SetValue(OutlineBrushProperty, value); }
        private CanvasSelectionPath? _path;
        private StreamGeometry? _projected;
        private Vector _documentDelta, _pixelDelta;
        private bool _visible;

        internal bool Set(CanvasSelectionPath? path, Vector delta, bool visible)
        {
            var changed = !ReferenceEquals(_path, path);
            _path = path; _documentDelta = delta; _visible = visible;
            if (changed || _projected is null) Reproject(); else { UpdateDelta(); InvalidateVisual(); }
            return Ready() && OutlineBrush is not null;
        }
        internal void Clear() { _path = null; _projected = null; _visible = false; _pixelDelta = default; InvalidateVisual(); }
        private bool Current() => !owner._disposed && owner._bitmap is not null && owner._frame is not null && _path is not null &&
            owner._document.Identity == (_path.ArtifactId, _path.RevisionId);
        private bool Ready() => _visible && _projected is not null && Current();
        internal void Reproject()
        {
            _projected = null;
            if (!Current()) { InvalidateVisual(); return; }
            try
            {
                var geometry = new StreamGeometry();
                using (var drawing = geometry.Open())
                {
                    var path = _path!.Geometry;
                    if (path.SchemaVersion != 1 || path.Segments.Count == 0 || path.Segments.Count > CanvasStructuredStrokeTransactionLimits.MaximumSegmentsPerPath)
                        throw new InvalidDataException("Unsupported selection contour geometry.");
                    drawing.BeginFigure(Map(path.Start.X, path.Start.Y), false);
                    foreach (var segment in path.Segments)
                    {
                        var end = Map(segment.End.X, segment.End.Y);
                        switch (segment.Kind)
                        {
                            case CanvasStrokePathSegmentKind.Line: drawing.LineTo(end); break;
                            case CanvasStrokePathSegmentKind.Quadratic when segment.Control1 is { } one:
                                drawing.QuadraticBezierTo(Map(one.X, one.Y), end); break;
                            case CanvasStrokePathSegmentKind.Cubic when segment.Control1 is { } first && segment.Control2 is { } second:
                                drawing.CubicBezierTo(Map(first.X, first.Y), Map(second.X, second.Y), end); break;
                            default: throw new InvalidDataException("Selection contour control points are invalid.");
                        }
                    }
                    drawing.EndFigure(false);
                }
                _projected = geometry; UpdateDelta();
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException)
            { _projected = null; }
            InvalidateVisual();
        }
        private Point Map(double x, double y)
        {
            var frame = owner._frame!; var bitmap = owner._bitmap!;
            var fitted = Math.Min(owner.Bounds.Width / bitmap.PixelSize.Width, owner.Bounds.Height / bitmap.PixelSize.Height);
            var width = bitmap.PixelSize.Width * fitted; var height = bitmap.PixelSize.Height * fitted;
            var left = (owner.Bounds.Width - width) / 2; var top = (owner.Bounds.Height - height) / 2;
            var result = new Point(((x - frame.X) / frame.Width * width + left) * owner._zoom + owner._pan.X,
                ((y - frame.Y) / frame.Height * height + top) * owner._zoom + owner._pan.Y);
            if (!double.IsFinite(result.X) || !double.IsFinite(result.Y)) throw new InvalidDataException("Selection projection is not finite.");
            return result;
        }
        private void UpdateDelta()
        {
            if (!Current()) { _pixelDelta = default; return; }
            var frame = owner._frame!; var bitmap = owner._bitmap!;
            var fitted = Math.Min(owner.Bounds.Width / bitmap.PixelSize.Width, owner.Bounds.Height / bitmap.PixelSize.Height);
            _pixelDelta = new(_documentDelta.X / frame.Width * bitmap.PixelSize.Width * fitted * owner._zoom,
                _documentDelta.Y / frame.Height * bitmap.PixelSize.Height * fitted * owner._zoom);
            if (!double.IsFinite(_pixelDelta.X) || !double.IsFinite(_pixelDelta.Y)) _projected = null;
        }
        internal bool TryGetBounds(out Rect original, out Rect preview)
        {
            original = preview = default;
            if (!Ready()) return false;
            original = _projected!.Bounds;
            preview = new Rect(original.X + _pixelDelta.X, original.Y + _pixelDelta.Y, original.Width, original.Height);
            return true;
        }
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        { base.OnPropertyChanged(change); if (change.Property == OutlineBrushProperty) InvalidateVisual(); }
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (!Ready() || OutlineBrush is not { } brush) return;
            // Painting only prepared geometry avoids parsing/native work on every frame.
            if (_pixelDelta.X == 0 && _pixelDelta.Y == 0) context.DrawGeometry(null, new Pen(brush, 2), _projected!);
            else
            {
                using (context.PushOpacity(.45)) context.DrawGeometry(null, new Pen(brush, 2), _projected!);
                using (context.PushTransform(new Matrix(1, 0, 0, 1, _pixelDelta.X, _pixelDelta.Y)))
                    context.DrawGeometry(null, new Pen(brush, 2.5), _projected!);
            }
        }
    }

}
