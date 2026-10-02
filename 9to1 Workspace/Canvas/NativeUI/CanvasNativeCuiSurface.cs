using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using HavenOS.Files;

namespace HavenOS.Apps.Canvas;

/// <summary>Owning Canvas CUI over a document opened by the host's exact canonical Files proof.
/// The supplied document transfers ownership to this surface; readiness remains the shared Home policy.</summary>
public sealed class CanvasNativeCuiSurface(Func<CancellationToken, Task<CanvasRnoteDocument>> open,
    ICuiSceneReadiness readiness, CanvasNativeInkContext? ink = null,CanvasNativeEraserContext? eraser = null) : UserControl, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CuiSceneHost? _scene;
    private CanvasCuiWorkspace? _bindings;
    private CanvasNativeViewport? _viewport;
    private CanvasRnoteDocument? _document;
    private CanvasToolState? _tools;
    private CanvasNativeInkInput? _ink;
    private CanvasNativeEraserInput? _eraser;
    private bool _available;
    private bool _disposed;
    private bool _initialized;
    private int _operations;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("This captured Canvas document has already been opened.");
        _initialized = true;
        Interlocked.Increment(ref _operations);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        try
        {
            if ((await readiness.CheckAsync(token)).State != CuiSceneAvailabilityState.Ready)
                throw new UnauthorizedAccessException("Home cannot authorise this Canvas source.");
            _document = await open(token);
            token.ThrowIfCancellationRequested();
            _viewport = new CanvasNativeViewport(_document, readiness);
            var registry = new CuiControlRegistry();
            registry.RegisterControlType("CanvasSpatialSurface", _ => _viewport!);
            _scene = new CuiSceneHost(registry);
            if (ink is not null)
            {
                if(eraser is not null && (ink.ExpectedStoreId==Guid.Empty || ink.ExpectedStoreId!=eraser.StoreId || ink.FileId!=eraser.FileId || ink.FilesRevision!=eraser.FilesRevision))
                    throw new InvalidDataException("Pen and eraser contexts must retain the same original Files owner.");
                if (_document.Identity != (ink.ArtifactId, ink.ArtifactRevision))
                    throw new InvalidDataException("The pen context differs from the exact displayed Canvas identity/revision.");
            }
            _tools = new CanvasToolState(tool =>
                tool == CanvasPrimaryTool.Pan && _available
                    ? new(true, "Pan this view with the pointer; wheel zooms around its position. The document is unchanged.")
                    : tool == CanvasPrimaryTool.Pen && _available && ink is not null &&
                      ink.IsAvailable() && _ink?.HasSubmittedStroke != true && _eraser?.HasSubmittedOperation!=true
                        ? new(true, "Capture native pen input, then review the exact stroke in Home.")
                        : tool==CanvasPrimaryTool.Eraser && _available && eraser is not null && _ink?.HasSubmittedStroke!=true && _eraser?.HasSubmittedOperation!=true
                            ? eraser.Capability(_document,_tools?.EraserOptions ?? new())
                        : CanvasToolCapability.Unavailable(_eraser?.Status ?? _ink?.Status ?? "This native tool has no current owning input operation."),
                selection => _viewport.PanWithPrimaryButton = selection.Tool == CanvasPrimaryTool.Pan,
                eraserCapability: options => _available && eraser is not null && _ink?.HasSubmittedStroke!=true && _eraser?.HasSubmittedOperation!=true
                    ? eraser.Capability(_document,options) : CanvasToolCapability.Unavailable("Finish the current request or reopen this Canvas."));
            if(eraser is not null)
            {
                _eraser=new(_viewport,_document,_tools,eraser);
                _eraser.Changed+=InkChanged;
            }
            _bindings = new CanvasCuiWorkspace(DispatchView,
                kind => _available && (kind is CanvasWorkspaceCommandKind.FitView or CanvasWorkspaceCommandKind.ZoomIn or CanvasWorkspaceCommandKind.ZoomOut ||
                    _ink?.HasSubmittedStroke!=true && (kind==CanvasWorkspaceCommandKind.Undo ? _eraser?.CanHistory(CanvasHistoryKind.Undo)==true : kind==CanvasWorkspaceCommandKind.Redo && _eraser?.CanHistory(CanvasHistoryKind.Redo)==true)), _tools,_eraser);
            _bindings.Refresh(_document.Snapshot, ink is null ? "Opened canonical Files revision · read-only view" : "Opened canonical Files revision · native pen input requires Home approval");
            var availability = await _scene.ShowAsync(new("canvas", "Canvas", "Canvas", CanvasCuiWorkspace.LoadDocument(),
                _bindings, _bindings, readiness), token);
            token.ThrowIfCancellationRequested();
            if (availability.State != CuiSceneAvailabilityState.Ready)
                throw new UnauthorizedAccessException(availability.Message);
            // Recheck the full source reference, content revision and actor after native construction.
            if ((await readiness.CheckAsync(token)).State != CuiSceneAvailabilityState.Ready)
                throw new UnauthorizedAccessException("Canvas source access changed during native construction.");
            token.ThrowIfCancellationRequested();
            Content = _scene;
            if (!await _viewport.RefreshAsync(token)) throw new UnauthorizedAccessException("The Canvas source is unavailable.");
            _available = true;
            if (ink is not null)
            {
                _tools!.Select(CanvasPrimaryTool.Pen);
                _ink = new CanvasNativeInkInput(_viewport, _document, _tools, ink.FileId, ink.FilesRevision,
                    () => _available && !_disposed && ink.IsAvailable(), ink.RequestOperation,ink.ExpectedStoreId);
                _ink.Changed += InkChanged;
            }
            else _tools.Select(CanvasPrimaryTool.Pan);
            _bindings.RefreshAvailability();
        }
        catch
        {
            Dispose();
            throw;
        }
        finally { EndOperation(); }
    }

    /// <summary>Revalidates the captured source; stale or revoked access removes the displayed raster.</summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || _viewport is null) return false;
        Interlocked.Increment(ref _operations);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            var ready = await _viewport.RefreshAsync(linked.Token);
            if (!ready) Dispose(); // A retired route needs a freshly opened owner scope, including its metadata.
            return ready;
        }
        catch { Dispose(); throw; }
        finally { EndOperation(); }
    }

    private async ValueTask DispatchView(CanvasWorkspaceCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_available || _disposed || _viewport is null || _document is null ||
            _document.Identity != (command.ArtifactId, command.BaseRevisionId))
            throw new InvalidOperationException("The displayed Canvas view is no longer available.");
        if(command.Kind is CanvasWorkspaceCommandKind.Undo or CanvasWorkspaceCommandKind.Redo)
        {
            if(_eraser is null || _ink?.HasSubmittedStroke==true)throw new NotSupportedException("History has no current owning operation.");
            await _eraser.RequestHistoryAsync(command.Kind==CanvasWorkspaceCommandKind.Undo ? CanvasHistoryKind.Undo : CanvasHistoryKind.Redo,cancellationToken);
            return;
        }
        if (command.Kind == CanvasWorkspaceCommandKind.FitView) _viewport.ResetView();
        else if (command.Kind is CanvasWorkspaceCommandKind.ZoomIn or CanvasWorkspaceCommandKind.ZoomOut)
            _viewport.ZoomAt(command.Kind == CanvasWorkspaceCommandKind.ZoomIn ? 1.2 : 1 / 1.2,
                new(_viewport.Bounds.Width / 2, _viewport.Bounds.Height / 2));
        else throw new NotSupportedException("This command has no authorised owning operation.");
        return;
    }

    private void InkChanged(object? sender, EventArgs args)
    { _tools?.RefreshCapabilities(); _bindings?.RefreshAvailability(); }

    private void EndOperation()
    {
        if (Interlocked.Decrement(ref _operations) == 0 && _disposed) _document?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _available = false;
        _lifetime.Cancel();
        if (_ink is not null) { _ink.Changed -= InkChanged; _ink.Dispose(); }
        if(_eraser is not null){_eraser.Changed-=InkChanged;_eraser.Dispose();}
        _tools?.Dispose();
        Content = null;
        _scene?.Dispose();
        _viewport?.Dispose();
        _bindings?.Dispose();
        if (Volatile.Read(ref _operations) == 0) _document?.Dispose();
        _lifetime.Dispose();
    }
}
