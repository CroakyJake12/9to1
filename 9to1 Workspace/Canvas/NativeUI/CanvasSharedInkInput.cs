using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;

namespace HavenOS.Apps.Canvas;

/// <summary>Shared low-level native input/rendering adapter. CUI owns tools and layout; the app's
/// callback owns insertion, permission, CAS, undo and save. No Canvas document or store is opened.</summary>
public sealed class CanvasSharedInkInput : Control, IDisposable
{
    private readonly HomeProductivityEngine _engine;
    private readonly Func<CanvasSharedInkOwnerSnapshot?> _captureOwner;
    private readonly Func<CanvasSharedInkStrokeRequest, CancellationToken, ValueTask<CanvasSharedInkCommitResult>> _commitOwner;
    private readonly CancellationTokenSource _lifetime;
    private readonly List<RnotePointerSample> _samples = [];
    private HomeProductivityCuiObjectSession? _rendered;
    private CanvasSharedInkScene? _scene;
    private CanvasSharedInkOwnerSnapshot? _shown, _captured;
    private IPointer? _pointer;
    private CanvasRnoteInkStyle _style = CanvasRnoteInkStyle.Default;
    private CanvasRnoteInkStyle? _capturedStyle;
    private Task? _submission, _close;
    private CanvasSharedInkOwnerSnapshot? _failedOwner;
    private bool _disposed, _releasing, _submitting, _retiring;
    public event EventHandler? Changed;
    public bool CanDraw { get; private set; }
    public string Status { get; private set; } = "The shared drawing renderer has not started.";
    public Exception? LastError { get; private set; }
    public Task? PendingSubmission => _submission;
    public IReadOnlyList<string> RetainedUnsupportedProperties => _scene?.Rendered.RetainedUnsupportedProperties ?? [];
    public CanvasRnoteInkStyle Style
    {
        get => _style;
        set { Dispatcher.UIThread.VerifyAccess(); _style = value ?? throw new ArgumentNullException(nameof(value)); CancelCapture(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    public CanvasSharedInkInput(HomeProductivityEngine engine, Func<CanvasSharedInkOwnerSnapshot?> captureOwner,
        Func<CanvasSharedInkStrokeRequest, CancellationToken, ValueTask<CanvasSharedInkCommitResult>> commitOwner,
        CancellationToken lifetime = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _captureOwner = captureOwner ?? throw new ArgumentNullException(nameof(captureOwner));
        _commitOwner = commitOwner ?? throw new ArgumentNullException(nameof(commitOwner));
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        Focusable = true; ClipToBounds = true;
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released;
        PointerCaptureLost += CaptureLost; DetachedFromVisualTree += Detached;
        _lifetime.Token.Register(() => Dispatcher.UIThread.Post(() => { if (!_disposed) { CancelCapture(); CanDraw = false; } }));
    }

    public bool Refresh() => Refresh(retryFailedRender: false);
    public bool Refresh(bool retryFailedRender)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || _retiring || _lifetime.IsCancellationRequested) return false;
        HomeProductivityCuiObjectSession? next = null;
        CanvasSharedInkOwnerSnapshot? rendering = null;
        try
        {
            var owner = _captureOwner();
            if (owner is null) { Clear("Open a current drawing to use the pen."); return false; }
            var captured = CanvasSharedInkStrokeFactory.Snapshot(_engine, owner);
            if (!retryFailedRender && _failedOwner is not null && CanvasSharedInkStrokeFactory.SameOwner(_failedOwner, captured)) return false;
            rendering = captured;
            if (_shown is not null && _rendered is not null && CanvasSharedInkStrokeFactory.SameOwner(_shown, captured))
            { CanDraw = !_submitting; return CanDraw; }
            CancelCapture();
            var scene = CanvasSharedInkStrokeFactory.RenderScene(_engine, captured.Context, captured.LayerId, captured.Bundles);
            next = new HomeProductivityCuiObjectSession(scene.Rendered, captured.LayerId);
            if (!Current(captured)) { Clear("The drawing changed while its renderer was preparing. Refresh it."); return false; }
            var old = _rendered; _rendered = next; next = null; _scene = scene; _shown = captured;
            CanDraw = !_submitting; LastError = null; _failedOwner = null;
            Status = "Draw with pen, mouse or touch. Release to add a stroke; Undo restores the previous drawing." +
                (scene.Rendered.RetainedUnsupportedProperties.Count == 0 ? "" : " Some drawing properties are retained but cannot be displayed here.");
            old?.Dispose(); InvalidateVisual(); Changed?.Invoke(this, EventArgs.Empty); return CanDraw;
        }
        catch (Exception error)
        {
            LastError = error; _failedOwner = rendering; Clear("Drawing is unavailable: " + error.Message); return false;
        }
        finally { next?.Dispose(); }
    }

    /// <summary>The same cached donor raster and native bounds; borrowed until the next refresh/dispose.</summary>
    public bool TryBorrowScene(out IImage? image, out Rect logicalBounds)
    {
        Dispatcher.UIThread.VerifyAccess(); image = null; logicalBounds = default;
        if (_disposed || _rendered is null || _scene is not { IsEmpty: false } scene || _shown is null || !Current(_shown)) return false;
        image = _rendered.BorrowRasterImage(); logicalBounds = new(scene.X, scene.Y, scene.Width, scene.Height); return true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // Avalonia draw-list hit testing needs a full-bounds drawing even before the first stroke.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        // Only already prepared native output is painted. No parsing, loading or engine work occurs here.
        if (_rendered is null || _scene is not { IsEmpty: false } scene) return;
        var image = _rendered.BorrowRasterImage();
        var destination = new Rect(scene.X / CanvasSharedInkStrokeFactory.LogicalWidth * Bounds.Width,
            scene.Y / CanvasSharedInkStrokeFactory.LogicalHeight * Bounds.Height,
            scene.Width / CanvasSharedInkStrokeFactory.LogicalWidth * Bounds.Width,
            scene.Height / CanvasSharedInkStrokeFactory.LogicalHeight * Bounds.Height);
        context.DrawImage(image, new Rect(image.Size), destination);
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsFinite(availableSize.Width) ? availableSize.Width : CanvasSharedInkStrokeFactory.LogicalWidth,
        double.IsFinite(availableSize.Height) ? availableSize.Height : CanvasSharedInkStrokeFactory.LogicalHeight);

    private bool Current(CanvasSharedInkOwnerSnapshot expected)
    {
        if (_disposed || _retiring || _lifetime.IsCancellationRequested) return false;
        var current = _captureOwner();
        return current is not null && CanvasSharedInkStrokeFactory.SameOwner(expected, current);
    }

    private bool Add(Point position, PointerPointProperties properties, PointerType type)
    {
        if (_samples.Count >= CanvasSharedInkStrokeFactory.MaximumSamples || Bounds.Width <= 0 || Bounds.Height <= 0 ||
            position.X < 0 || position.Y < 0 || position.X > Bounds.Width || position.Y > Bounds.Height)
        { CancelCapture(); Status = "The stroke exceeded the drawing bounds; no partial stroke was added."; return false; }
        var pen = type == PointerType.Pen;
        var sample = new RnotePointerSample(position.X / Bounds.Width * CanvasSharedInkStrokeFactory.LogicalWidth,
            position.Y / Bounds.Height * CanvasSharedInkStrokeFactory.LogicalHeight,
            pen ? properties.Pressure : .5, pen ? properties.XTilt : 0, pen ? properties.YTilt : 0);
        if (!sample.IsValid) { CancelCapture(); Status = "The input device returned invalid pen data."; return false; }
        _samples.Add(sample); return true;
    }

    private void Pressed(object? sender, PointerPressedEventArgs args)
    {
        if (!CanDraw || _disposed || _retiring || _submitting || _pointer is not null || _shown is null || !IsEffectivelyEnabled) return;
        try
        {
            if (!Current(_shown)) { Refresh(); return; }
            var point = args.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed || point.Properties.IsEraser) return;
            _captured = _shown; _capturedStyle = _style; _pointer = args.Pointer; _samples.Clear();
            if (!Add(point.Position, point.Properties, args.Pointer.Type)) return;
            Focus(); args.Pointer.Capture(this); args.PreventGestureRecognition(); args.Handled = true;
        }
        catch (Exception error) { LastError = error; CancelCapture(); Clear("Drawing input is unavailable: " + error.Message); }
    }

    private void Moved(object? sender, PointerEventArgs args)
    {
        if (_pointer != args.Pointer) return;
        try
        {
            if (_captured is null || !Current(_captured) || !IsEffectivelyEnabled) { CancelCapture(); return; }
            foreach (var point in args.GetIntermediatePoints(this))
                if (!Add(point.Position, point.Properties, args.Pointer.Type)) return;
            args.Handled = true;
        }
        catch (Exception error) { LastError = error; CancelCapture(); Clear("Drawing input is unavailable: " + error.Message); }
    }

    private void Released(object? sender, PointerReleasedEventArgs args)
    {
        if (_pointer != args.Pointer || _disposed || _retiring || _submitting) return;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _submission = SubmitAfterPublicationAsync(start.Task, args); start.SetResult();
        _ = ObserveSubmissionAsync(_submission);
    }

    private async Task SubmitAfterPublicationAsync(Task start, PointerReleasedEventArgs args)
    {
        await start;
        Exception? submissionFailure = null;
        string? submissionNotice = null;
        try
        {
            var expected = _captured; var style = _capturedStyle;
            if (expected is null || style is null || !Current(expected) || !IsEffectivelyEnabled) return;
            var point = args.GetCurrentPoint(this);
            if (!Add(point.Position, point.Properties, args.Pointer.Type)) return;
            var bundle = CanvasSharedInkStrokeFactory.Capture(_engine, expected.Context, expected.LayerId, _samples, style);
            // Actual donor render/raster validation precedes owner insertion. Failure leaves canonical state intact.
            _ = CanvasSharedInkStrokeFactory.RenderScene(_engine, expected.Context, expected.LayerId, [.. expected.Bundles, bundle]);
            if (!Current(expected)) { submissionNotice = "The owning drawing changed; this stroke was not added."; return; }
            ReleasePointer(); _samples.Clear(); _captured = null; _capturedStyle = null;
            _submitting = true; CanDraw = false; Changed?.Invoke(this, EventArgs.Empty);
            _lifetime.Token.ThrowIfCancellationRequested();
            var result = await _commitOwner(new(expected, bundle), _lifetime.Token);
            // An owner result is not reclassified as rollback if disposal follows the actual mutation.
            submissionNotice = result.Message;
            if (!result.Succeeded) submissionFailure = new InvalidOperationException(result.Code + ": " + result.Message);
        }
        catch (Exception error) { submissionFailure = error; submissionNotice = "The stroke could not be added. " + error.Message; throw; }
        finally
        {
            CancelCapture(); _submitting = false;
            if (!_disposed && !_retiring && !_lifetime.IsCancellationRequested)
            {
                Refresh();
                if (submissionFailure is not null) LastError = submissionFailure;
                if (submissionNotice is not null) Status = submissionNotice;
                Changed?.Invoke(this, EventArgs.Empty);
            }
            args.Handled = true;
        }
    }

    private static async Task ObserveSubmissionAsync(Task task) { try { await task; } catch { /* Retained by PendingSubmission and LastError. */ } }
    private void ReleasePointer()
    {
        var pointer = _pointer; _pointer = null; _releasing = true;
        try { pointer?.Capture(null); } finally { _releasing = false; }
    }
    private void CancelCapture() { ReleasePointer(); _samples.Clear(); _captured = null; _capturedStyle = null; }
    private void CaptureLost(object? sender, PointerCaptureLostEventArgs args) { if (!_releasing) CancelCapture(); }
    private void Detached(object? sender, VisualTreeAttachmentEventArgs args) { CancelCapture(); }
    private void Clear(string status)
    {
        CancelCapture(); CanDraw = false; Status = status; _shown = null; _scene = null;
        var old = _rendered; _rendered = null; old?.Dispose(); InvalidateVisual(); Changed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>Retire ingress, join the exact original callback, then release only this view's resources.</summary>
    public Task CloseAndDrainAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_close is not null) return _close;
        _retiring = true; CanDraw = false; CancelCapture();
        var original = _submission;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _close = CloseAfterPublicationAsync(start.Task, original); start.SetResult(); return _close;
    }
    private async Task CloseAfterPublicationAsync(Task start, Task? original)
    {
        await start;
        var failures = new List<Exception>();
        if (original is not null)
            try { await original; } catch (Exception error) { failures.Add(error); }
        try { Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Shared ink retirement retained original submission and cleanup failures.", failures);
    }
    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        if (_submission is { IsCompleted: false }) throw new InvalidOperationException("The original shared ink submission must settle before disposal.");
        _disposed = true; _lifetime.Cancel(); Clear("Drawing closed.");
        PointerPressed -= Pressed; PointerMoved -= Moved; PointerReleased -= Released;
        PointerCaptureLost -= CaptureLost; DetachedFromVisualTree -= Detached; _lifetime.Dispose(); Changed = null;
    }
}
