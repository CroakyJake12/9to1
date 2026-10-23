using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;

namespace HavenOS.Apps.Canvas;

public sealed record CanvasPresentInkEditRequest(Guid DocumentId, int ExpectedDocumentVersion, Guid ElementId,
    JsonElement ExpectedBundle, JsonElement ReplacementBundle);
public sealed record CanvasPresentInkViewState(bool Ready, string Code, string Message);

/// <summary>Actual read-only Home/Rnote CUI for one Present-owned payload. All editing remains an explicit
/// callback into the original Present session, with its permission/lifetime checks, CAS, undo and save.</summary>
public sealed class CanvasPresentInkObjectSession : IDisposable
{
    private readonly PresentEditor _owner;
    private readonly HomeProductivityEngine _engine;
    private readonly Guid _documentId, _elementId;
    private readonly Func<bool> _isOwnerCurrent;
    private readonly Func<CanvasPresentInkEditRequest, CancellationToken, ValueTask<PresentSharedObjectUpdateResult>>? _editOwner;
    private readonly CancellationTokenSource _lifetime;
    private readonly ContentControl _host = new();
    private HomeProductivityCuiObjectSession? _rendered;
    private JsonElement? _expectedBundle, _failedBundle;
    private int _displayedVersion, _refreshGeneration;
    private bool _disposed, _editInFlight;
    public Control View => _host;
    public CanvasPresentInkViewState State { get; private set; } = new(false, "NotRendered", "The drawing has not rendered.");
    public Exception? LastError { get; private set; }
    public long RasterByteCount => _rendered?.RasterByteCount ?? 0;
    public IReadOnlyList<string> RetainedUnsupportedProperties => _rendered?.RetainedUnsupportedProperties ?? [];
    /// <summary>Borrow the actual CUI-bound image without rendering, loading or mutating in the paint loop.</summary>
    public bool TryBorrowRasterImage(out IImage? image)
    {
        image = null;
        if (!Dispatcher.UIThread.CheckAccess()) return false;
        try
        {
            if (!IsCurrent() || !State.Ready || _rendered is null || _expectedBundle is not { } expected) return false;
            var matches = _owner.Document.Slides.SelectMany(slide => slide.Elements).Where(element => element.Id == _elementId).ToArray();
            if (matches.Length != 1 || matches[0].Kind != PresentElementKind.SharedObject ||
                matches[0].SharedProductivityBundle is not { } current || !JsonElement.DeepEquals(current, expected)) return false;
            image = _rendered.BorrowRasterImage();
            return true;
        }
        catch (Exception error) { LastError = error; return false; }
    }
    public bool CanRequestEdit => !_disposed && !_editInFlight && State.Ready && _editOwner is not null && IsCurrent();
    public event EventHandler? ViewChanged;

    public CanvasPresentInkObjectSession(PresentEditor owner, Guid elementId, HomeProductivityEngine engine,
        Func<bool> isOwnerCurrent, CancellationToken lifetime,
        Func<CanvasPresentInkEditRequest, CancellationToken, ValueTask<PresentSharedObjectUpdateResult>>? editOwner = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _isOwnerCurrent = isOwnerCurrent ?? throw new ArgumentNullException(nameof(isOwnerCurrent));
        if (elementId == Guid.Empty) throw new ArgumentException("A canonical Present element is required.", nameof(elementId));
        _elementId = elementId; _documentId = owner.Document.Id; _editOwner = editOwner;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _owner.Changed += OwnerChanged;
        Refresh();
    }

    private bool IsCurrent()
    {
        if (_disposed || _lifetime.IsCancellationRequested || _owner.Document.Id != _documentId) return false;
        try { return _isOwnerCurrent(); }
        catch (Exception error) { LastError = error; return false; }
    }
    private void OwnerChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(() => { if (!_disposed) Refresh(); });
    }

    public bool Refresh() => Refresh(retryFailedRender: false);

    public bool Refresh(bool retryFailedRender)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return false;
        var generation = ++_refreshGeneration;
        HomeProductivityCuiObjectSession? next = null;
        JsonElement? renderingBundle = null;
        try
        {
            if (!IsCurrent()) return Clear("OwnerUnavailable", "The original Present owner is no longer available.");
            var document = _owner.Document;
            var version = document.Version;
            var matches = document.Slides.SelectMany(slide => slide.Elements).Where(element => element.Id == _elementId).ToArray();
            if (matches.Length != 1 || matches[0].Kind != PresentElementKind.SharedObject || matches[0].SharedProductivityBundle is not { } bundle)
                return Clear("ObjectUnavailable", "The exact drawing is no longer in this presentation.");
            var element = matches[0];
            var captured = bundle.Clone();
            if (!retryFailedRender && _rendered is null && _failedBundle is { } failed && JsonElement.DeepEquals(failed, captured)) return false;
            renderingBundle = captured;
            if (_rendered is not null && _expectedBundle is { } previous && JsonElement.DeepEquals(previous, captured))
            {
                _displayedVersion = document.Version;
                State = new(true, "RenderedReadOnly", "Showing the canonical Home/Rnote drawing; edits require the Present owner.");
                return true;
            }
            var bridge = new CanvasPresentInkBridge(_engine, document.Id, document.Version);
            var canonical = bridge.Capture(captured);
            if (canonical.ObjectId != _elementId) return Clear("ObjectIdentityMismatch", "The embedded drawing identity differs from its Present element.");
            next = new HomeProductivityCuiObjectSession(bridge.Render(captured), canonical.ObjectId);
            if (!IsCurrent() || generation != _refreshGeneration || !ReferenceEquals(document, _owner.Document) ||
                document.Version != version || !document.Slides.SelectMany(slide => slide.Elements).Any(item => ReferenceEquals(item, element)) ||
                element.SharedProductivityBundle is not { } observed || !JsonElement.DeepEquals(observed, captured))
                return Clear("PayloadConflict", "The Present owner or drawing changed while rendering.");
            var old = _rendered; _rendered = next; next = null;
            _host.Content = _rendered.View; _expectedBundle = captured; _displayedVersion = document.Version;
            LastError = null; _failedBundle = null;
            State = new(true, "RenderedReadOnly", "Showing the canonical Home/Rnote drawing; edits require the Present owner.");
            old?.Dispose(); ViewChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception error)
        {
            LastError = error; _failedBundle = renderingBundle;
            return Clear("RenderUnavailable", "The actual Home/Rnote drawing could not be rendered; no substitute image is shown.");
        }
        finally { next?.Dispose(); }
    }

    public async ValueTask<PresentSharedObjectUpdateResult> RequestEditAsync(HomeProductivityAction action, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanRequestEdit || _expectedBundle is not { } expected || _editOwner is null)
            return new(false, "ReadOnlyOrRetired", "This read-only view has no current Present editing owner.");
        var document = _owner.Document;
        var expectedVersion = _displayedVersion;
        var generation = _refreshGeneration;
        if (document.Version != expectedVersion) return new(false, "RevisionConflict", "Refresh the drawing after the Present revision changed.");
        var matches = document.Slides.SelectMany(slide => slide.Elements).Where(element => element.Id == _elementId).ToArray();
        if (matches.Length != 1 || matches[0].Locked || matches[0].SharedProductivityBundle is not { } current || !JsonElement.DeepEquals(current, expected))
            return new(false, "PayloadConflict", "The displayed drawing changed before this edit request.");
        var bridge = new CanvasPresentInkBridge(_engine, document.Id, document.Version);
        PresentSharedObjectCapture prepared;
        try { prepared = bridge.PrepareAction(expected, action); }
        catch (PresentSharedObjectCapabilityException error) { return new(false, error.Code, error.Message); }
        catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or NotSupportedException)
        { return new(false, "InvalidOrUnsupportedAction", error.Message); }
        if (!IsCurrent() || generation != _refreshGeneration || !ReferenceEquals(document, _owner.Document) || document.Version != expectedVersion ||
            matches[0].SharedProductivityBundle is not { } observed || !JsonElement.DeepEquals(observed, expected))
            return new(false, "PayloadConflict", "The original Present owner changed during action preparation.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        linked.Token.ThrowIfCancellationRequested();
        _editInFlight = true;
        try
        {
            var result = await _editOwner(new(document.Id, expectedVersion, _elementId, expected.Clone(), prepared.Bundle.Clone()), linked.Token);
            // A completed owner result is retained even if disposal followed the actual mutation; never claim rollback.
            if (!_disposed && IsCurrent()) Refresh();
            return result;
        }
        finally { _editInFlight = false; }
    }

    private bool Clear(string code, string message)
    {
        _host.Content = null; _expectedBundle = null;
        var old = _rendered; _rendered = null;
        State = new(false, code, message);
        old?.Dispose();
        return false;
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        _disposed = true; ++_refreshGeneration;
        _owner.Changed -= OwnerChanged;
        _lifetime.Cancel();
        try { Clear("Disposed", "The Present drawing view is closed."); }
        finally { _lifetime.Dispose(); ViewChanged = null; }
    }
}
