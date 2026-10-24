using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private bool _documentCoordinates, _eraseMode, _capturedErase;
    private double _eraserWidth = 12;
    private sealed record ViewStamp(Size Bounds, Point? Origin, Point? BasisX, Point? BasisY, double Scale, Size? Viewport, IReadOnlyList<ClipStamp> Clips);
    private sealed record ClipStamp(Size Bounds, Point? Origin, Point? BasisX, Point? BasisY, bool ClipsBounds, Rect? GeometryBounds);
    private ViewStamp? _capturedView;
    private sealed record QueuedRelease(long Generation, object Pointer, CanvasSharedInkOwnerSnapshot Owner,
        CanvasRnoteInkStyle Style, bool Erase, double EraserWidth, IReadOnlyList<RnotePointerSample> Samples, ViewStamp? View);
    private long _gestureGeneration;
    private QueuedRelease? _activeRelease;
    private bool _releaseQueued;
    /// <summary>Explicit page transform: one saved document unit is one local DIP.
    /// Bounds/scroll/DPI changes cancel capture, never rescale canonical coordinates.</summary>
    public bool DocumentCoordinates
    {
        get => _documentCoordinates;
        set { Dispatcher.UIThread.VerifyAccess(); if (_documentCoordinates == value) return; CancelCapture(); _documentCoordinates = value; InvalidateVisual(); }
    }
    public bool EraserEnabled { get; set; }
    public bool EraseMode
    {
        get => _eraseMode;
        set { Dispatcher.UIThread.VerifyAccess(); if (_eraseMode == value) return; CancelCapture(); _eraseMode = value; }
    }
    public double EraserWidth
    {
        get => _eraserWidth;
        set
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!double.IsFinite(value) || value is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(value));
            if (_eraserWidth == value) return; CancelCapture(); _eraserWidth = value;
        }
    }
    private ViewStamp CurrentView()
    {
        var root = TopLevel.GetTopLevel(this);
        var clips = new List<ClipStamp>();
        foreach (var control in this.GetVisualAncestors().OfType<Control>().Prepend(this))
        {
            if (DocumentCoordinates && control.Clip is not null && control.Clip is not RectangleGeometry)
                throw new NotSupportedException("Page ink capture requires a supported rectangular clip; original ink is retained.");
            if (control.ClipToBounds || control.Clip is not null)
                clips.Add(new(control.Bounds.Size, root is null ? null : control.TranslatePoint(default, root),
                    root is null ? null : control.TranslatePoint(new(1, 0), root), root is null ? null : control.TranslatePoint(new(0, 1), root),
                    control.ClipToBounds, control.Clip?.Bounds));
        }
        return new(Bounds.Size, root is null ? null : this.TranslatePoint(default, root),
            root is null ? null : this.TranslatePoint(new(1, 0), root), root is null ? null : this.TranslatePoint(new(0, 1), root),
            root?.RenderScaling ?? 1, root?.ClientSize, clips);
    }
    private static bool SameView(ViewStamp left, ViewStamp right) => left.Bounds == right.Bounds && left.Origin == right.Origin &&
        left.BasisX == right.BasisX && left.BasisY == right.BasisY && left.Scale == right.Scale && left.Viewport == right.Viewport && left.Clips.SequenceEqual(right.Clips);

    private Task? _submission, _close;
    private readonly CanvasSharedInkSubmissionLedger _originalSubmissions = new();
    private bool _paintPreparedScene = true;
    private CanvasSharedInkOwnerSnapshot? _failedOwner;
    private bool _disposed, _releasing, _submitting, _retiring;
    public event EventHandler? Changed;
    public bool CanDraw { get; private set; }
    public string Status { get; private set; } = "The shared drawing renderer has not started.";
    public Exception? LastError { get; private set; }
    public Task? PendingSubmission => _submission;
    public IReadOnlyList<Task> OriginalSubmissions => _originalSubmissions.Originals;
    /// <summary>False when the owning ordered scene already paints this exact canonical ink below the input overlay.</summary>
    public bool PaintPreparedScene
    {
        get => _paintPreparedScene;
        set { Dispatcher.UIThread.VerifyAccess(); _paintPreparedScene = value; InvalidateVisual(); }
    }
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
        if (!PaintPreparedScene || _rendered is null || _shown is null || _scene is not { IsEmpty: false } scene) return;
        var image = _rendered.BorrowRasterImage();
        var destination = DocumentCoordinates ? new Rect(scene.X, scene.Y, scene.Width, scene.Height) :
            new Rect(scene.X / _shown.LogicalWidth * Bounds.Width, scene.Y / _shown.LogicalHeight * Bounds.Height,
                scene.Width / _shown.LogicalWidth * Bounds.Width, scene.Height / _shown.LogicalHeight * Bounds.Height);
        context.DrawImage(image, new Rect(image.Size), destination);
    }

    protected override Size MeasureOverride(Size availableSize) => DocumentCoordinates ? default : new(
        double.IsFinite(availableSize.Width) ? availableSize.Width : CanvasSharedInkStrokeFactory.LogicalWidth,
        double.IsFinite(availableSize.Height) ? availableSize.Height : CanvasSharedInkStrokeFactory.LogicalHeight);

    private bool Current(CanvasSharedInkOwnerSnapshot expected) => Current(expected, _capturedView);
    private bool Current(CanvasSharedInkOwnerSnapshot expected, ViewStamp? view)
    {
        if (_disposed || _retiring || _lifetime.IsCancellationRequested || view is not null && !SameView(view, CurrentView())) return false;
        var current = _captureOwner();
        return current is not null && CanvasSharedInkStrokeFactory.SameOwner(expected, current);
    }

    private bool Add(Point position, PointerPointProperties properties, PointerType type)
    {
        if (_samples.Count >= CanvasSharedInkStrokeFactory.MaximumSamples || Bounds.Width <= 0 || Bounds.Height <= 0 ||
            position.X < 0 || position.Y < 0 || position.X > Bounds.Width || position.Y > Bounds.Height || Clip?.FillContains(position) == false)
        { CancelCapture(); Status = "The stroke exceeded the drawing bounds; no partial stroke was added."; return false; }
        var pen = type == PointerType.Pen;
        var logicalWidth = _captured?.LogicalWidth ?? _shown?.LogicalWidth ?? CanvasSharedInkStrokeFactory.LogicalWidth;
        var logicalHeight = _captured?.LogicalHeight ?? _shown?.LogicalHeight ?? CanvasSharedInkStrokeFactory.LogicalHeight;
        foreach (var ancestor in this.GetVisualAncestors().OfType<Control>().Where(control => control.ClipToBounds || control.Clip is not null))
        {
            if (this.TranslatePoint(position, ancestor) is not { } local ||
                ancestor.ClipToBounds && (local.X < 0 || local.Y < 0 || local.X > ancestor.Bounds.Width || local.Y > ancestor.Bounds.Height) ||
                ancestor.Clip?.FillContains(local) == false)
            { CancelCapture(); Status = "The gesture left the visible page; no partial operation was submitted."; return false; }
        }
        var x = DocumentCoordinates ? position.X : position.X / Bounds.Width * logicalWidth;
        var y = DocumentCoordinates ? position.Y : position.Y / Bounds.Height * logicalHeight;
        if (x > logicalWidth || y > logicalHeight) { CancelCapture(); Status = "The gesture left the saved page extent."; return false; }
        var sample = new RnotePointerSample(x, y,
            pen ? properties.Pressure : .5, pen ? properties.XTilt : 0, pen ? properties.YTilt : 0);
        if (!sample.IsValid) { CancelCapture(); Status = "The input device returned invalid pen data."; return false; }
        _samples.Add(sample); return true;
    }

    private void Pressed(object? sender, PointerPressedEventArgs args)
    {
        if (!CanDraw || _disposed || _retiring || _submitting || _releaseQueued || _pointer is not null || _shown is null || !IsEffectivelyEnabled) return;
        try
        {
            if (!Current(_shown)) { Refresh(); return; }
            var point = args.GetCurrentPoint(this);
            if (!CanBeginGesture(args.Pointer.Type, point.Properties.IsLeftButtonPressed, point.Properties.IsEraser, EraserEnabled)) return;
            _gestureGeneration=checked(_gestureGeneration+1);_activeRelease=null;_releaseQueued=false;
            _captured = _shown; _capturedStyle = _style; _capturedErase = EraserEnabled && (EraseMode || point.Properties.IsEraser);
            _capturedView = CurrentView(); _pointer = args.Pointer; _samples.Clear();
            if (!Add(point.Position, point.Properties, args.Pointer.Type)) return;
            Focus(); args.Pointer.Capture(this); args.PreventGestureRecognition(); args.Handled = true;
        }
        catch (Exception error) { LastError = error; CancelCapture(); Clear("Drawing input is unavailable: " + error.Message); }
    }

    internal static bool CanBeginGesture(PointerType type, bool primaryPressed, bool eraserEnd, bool eraserEnabled)
        => (primaryPressed || eraserEnd || type == PointerType.Touch) && (!eraserEnd || eraserEnabled);

    private void Moved(object? sender, PointerEventArgs args)
    {
        if (_pointer != args.Pointer) return;
        if (_releaseQueued) { args.Handled=true; return; }
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
        if (_pointer != args.Pointer || _disposed || _retiring || _submitting || _releaseQueued) return;
        QueuedRelease? release;
        try { release=CaptureRelease(args); }
        catch(Exception error) { LastError=error;CancelCapture();Status="The shared stroke release could not be captured. "+error.Message;Changed?.Invoke(this,EventArgs.Empty);return; }
        if(release is null)
        { if(_pointer==args.Pointer)CancelCapture(); return; }
        _activeRelease=release;_releaseQueued=true;args.Handled=true;
        ReleasePointer();_samples.Clear();_captured=null;_capturedStyle=null;_capturedView=null;_capturedErase=false;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _submission = SubmitAfterPublicationAsync(start.Task, release);
        _originalSubmissions.Observe(_submission); start.SetResult();
        _ = ObserveSubmissionAsync(_submission);
    }

    private QueuedRelease? CaptureRelease(PointerReleasedEventArgs args)
    {
        var expected=_captured;var style=_capturedStyle;var view=_capturedView;
        if(expected is null||style is null||!Current(expected,view)||!IsEffectivelyEnabled)return null;
        var point=args.GetCurrentPoint(this);
        if(!Add(point.Position,point.Properties,args.Pointer.Type))return null;
        return new(_gestureGeneration,args.Pointer,expected,style,_capturedErase,_eraserWidth,
            Array.AsReadOnly(_samples.ToArray()),view);
    }

    private bool Owns(QueuedRelease release)=>release.Generation==_gestureGeneration&&ReferenceEquals(_activeRelease,release);

    private async Task SubmitAfterPublicationAsync(Task start, QueuedRelease release)
    {
        await start;
        Exception? submissionFailure = null;
        string? submissionNotice = null;
        var ownerInvoked = false;
        var submissionOwned = false;
        try
        {
            if (!Owns(release)||!Current(release.Owner,release.View)||!IsEffectivelyEnabled) return;
            var expected=release.Owner;var style=release.Style;
            CanvasSharedInkStrokeRequest request;
            IReadOnlyList<System.Text.Json.JsonElement> proposed;
            if (release.Erase)
            {
                request = CanvasSharedInkStrokeFactory.CaptureWholeErase(_engine, expected, release.Samples, release.EraserWidth);
                proposed = request.ReplacementBundles!;
                if (proposed.Count == expected.Bundles.Count && proposed.Zip(expected.Bundles).All(pair => System.Text.Json.JsonElement.DeepEquals(pair.First, pair.Second)))
                { submissionNotice = "No stroke intersects this eraser gesture."; return; }
            }
            else
            {
                var bundle = CanvasSharedInkStrokeFactory.Capture(_engine, expected.Context, expected.LayerId, release.Samples, style, expected.LogicalWidth, expected.LogicalHeight);
                request = new(expected, bundle); proposed = [.. expected.Bundles, bundle];
            }
            // Actual donor render/raster validation precedes owner insertion. Failure leaves canonical state intact.
            _ = CanvasSharedInkStrokeFactory.RenderScene(_engine, expected.Context, expected.LayerId, proposed);
            if (!Owns(release)||!Current(expected,release.View)) { submissionNotice = "The owning drawing changed; this stroke was not added."; return; }
            _submitting = true; submissionOwned=true;CanDraw = false; Changed?.Invoke(this, EventArgs.Empty);
            _lifetime.Token.ThrowIfCancellationRequested();
            // Changed is synchronous and may reenter the owner. Revalidate both the
            // exact bundle/revision and its typed inverse-placement map at the final
            // boundary before invoking the app's single owning history commit.
            if (!Owns(release)||!Current(expected,release.View) || request.Erasure is not null &&
                !CanvasSharedInkStrokeFactory.IsCurrentEraseRequest(_engine, request, _captureOwner()))
            { submissionNotice = "The owning drawing placement changed; this erase was not submitted."; return; }
            ownerInvoked = true;
            var result = await _commitOwner(request, _lifetime.Token);
            // An owner result is not reclassified as rollback if disposal follows the actual mutation.
            submissionNotice = result.Message;
            if (!result.Succeeded) submissionFailure = new InvalidOperationException(result.Code + ": " + result.Message);
        }
        catch (Exception error)
        {
            submissionFailure = error;
            submissionNotice = (ownerInvoked
                ? "The owner did not finish reporting this drawing operation. Inspect saved state before retrying. "
                : "The drawing operation was not submitted. ") + error.Message;
            throw;
        }
        finally
        {
            // A retired release may finish after a later gesture has begun. Only
            // the release/submission that still owns these mutable buffers may clear them.
            var ownsRelease=Owns(release);
            if(submissionOwned)_submitting=false;
            if(ownsRelease)
            {
                _activeRelease=null;_releaseQueued=false;_gestureGeneration=checked(_gestureGeneration+1);
            }
            if(submissionOwned||ownsRelease)
            {
                _samples.Clear();_captured=null;_capturedStyle=null;_capturedView=null;_capturedErase=false;
            }
            if ((submissionOwned||ownsRelease)&&!_disposed && !_retiring && !_lifetime.IsCancellationRequested)
            {
                Refresh();
                if (submissionFailure is not null) LastError = submissionFailure;
                if (submissionNotice is not null) Status = submissionNotice;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private static async Task ObserveSubmissionAsync(Task task) { try { await task; } catch { /* Retained by PendingSubmission and LastError. */ } }
    private void ReleasePointer()
    {
        var pointer = _pointer; _pointer = null; _releasing = true;
        try { pointer?.Capture(null); } finally { _releasing = false; }
    }
    private void CancelCapture()
    {
        _gestureGeneration=checked(_gestureGeneration+1);_activeRelease=null;_releaseQueued=false;
        ReleasePointer();_samples.Clear();_captured=null;_capturedStyle=null;_capturedView=null;_capturedErase=false;
    }
    internal static bool IsRelevantCaptureLoss(object? capturedPointer, object lostPointer, bool releasing)
        => !releasing && capturedPointer is not null && ReferenceEquals(capturedPointer, lostPointer);
    private void CaptureLost(object? sender, PointerCaptureLostEventArgs args)
    { if (IsRelevantCaptureLoss(_pointer, args.Pointer, _releasing)) CancelCapture(); }
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
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _close = CloseAfterPublicationAsync(start.Task); start.SetResult(); return _close;
    }
    private async Task CloseAfterPublicationAsync(Task start)
    {
        await start;
        var failures = new List<Exception>();
        try { await _originalSubmissions.DrainAsync(); } catch (Exception error) { failures.Add(error); }
        try { Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Shared ink retirement retained original submission and cleanup failures.", failures);
    }
    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        if (_originalSubmissions.Originals.Any(original => !original.IsCompleted)) throw new InvalidOperationException("The original shared ink submission must settle before disposal.");
        _disposed = true; _lifetime.Cancel(); Clear("Drawing closed.");
        PointerPressed -= Pressed; PointerMoved -= Moved; PointerReleased -= Released;
        PointerCaptureLost -= CaptureLost; DetachedFromVisualTree -= Detached; _lifetime.Dispose(); Changed = null;
    }
}

/// <summary>Original task custody only. This does not emulate input, renderer readiness or artifact success.</summary>
internal sealed class CanvasSharedInkSubmissionLedger
{
    private readonly List<Task> _originals = [];
    public IReadOnlyList<Task> Originals => _originals.AsReadOnly();
    public void Observe(Task original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (_originals.Any(item => ReferenceEquals(item, original))) return;
        _originals.Add(original); _ = ObserveFailureAsync(original);
    }
    private static async Task ObserveFailureAsync(Task original)
    { try { await original; } catch { /* The exact original remains retained for the final independent join. */ } }
    public async Task DrainAsync()
    {
        var originals = _originals.ToArray(); var failures = new List<Exception>();
        foreach (var original in originals)
            try { await original; }
            catch (Exception error)
            {
                if (original.IsFaulted && original.Exception is { } aggregate) failures.AddRange(aggregate.InnerExceptions);
                else failures.Add(error);
            }
        if (failures.Count > 0) throw new AggregateException("Original shared ink submissions retained their failures.", failures);
    }
}
