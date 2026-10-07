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
    private readonly CanvasOriginalWorkOwner _originalWork = new();
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

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => _originalWork.RunOriginalAsync(InitializeOriginalAsync, cancellationToken);
    private async Task InitializeOriginalAsync(CancellationToken cancellationToken)
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
            if ((await _originalWork.ObserveOriginalAsync(readiness.CheckAsync(token).AsTask())).State != CuiSceneAvailabilityState.Ready)
                throw new UnauthorizedAccessException("Home cannot authorise this Canvas source.");
            _document = await _originalWork.ObserveOriginalAsync(open(token));
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
            var availability = await _originalWork.ObserveOriginalAsync(_scene.ShowAsync(new("canvas", "Canvas", "Canvas", CanvasCuiWorkspace.LoadDocument(),
                _bindings, _bindings, readiness)
            { IsPublicationCurrent = () => !_disposed && !_lifetime.IsCancellationRequested }, token));
            token.ThrowIfCancellationRequested();
            if (availability.State != CuiSceneAvailabilityState.Ready)
                throw new UnauthorizedAccessException(availability.Message);
            // Recheck the full source reference, content revision and actor after native construction.
            if ((await _originalWork.ObserveOriginalAsync(readiness.CheckAsync(token).AsTask())).State != CuiSceneAvailabilityState.Ready)
                throw new UnauthorizedAccessException("Canvas source access changed during native construction.");
            token.ThrowIfCancellationRequested();
            Content = _scene;
            if (!await _originalWork.ObserveOriginalAsync(_viewport.RefreshAsync(token))) throw new UnauthorizedAccessException("The Canvas source is unavailable.");
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
    public Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
        => _originalWork.RunOriginalAsync(RefreshOriginalAsync, cancellationToken);
    private async Task<bool> RefreshOriginalAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || _viewport is null) return false;
        Interlocked.Increment(ref _operations);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            var ready = await _originalWork.ObserveOriginalAsync(_viewport.RefreshAsync(linked.Token));
            if (!ready) Dispose(); // A retired route needs a freshly opened owner scope, including its metadata.
            return ready;
        }
        catch { Dispose(); throw; }
        finally { EndOperation(); }
    }

    private ValueTask DispatchView(CanvasWorkspaceCommand command, CancellationToken cancellationToken)
        => new(_originalWork.RunOriginalAsync(token => DispatchViewOriginalAsync(command, token), cancellationToken));
    private async Task DispatchViewOriginalAsync(CanvasWorkspaceCommand command, CancellationToken cancellationToken)
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
        Interlocked.Decrement(ref _operations);
    }

    public Task CloseAndDrainAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.InvokeAsync(CloseAndDrainAsync);
        _disposed = true; _available = false;
        return _originalWork.CloseAndDrainAsync(CloseOriginalChildrenAsync);
    }
    private async Task CloseOriginalChildrenAsync()
    {
        var errors = new List<Exception>();
        try { _lifetime.Cancel(); } catch (Exception error) { errors.Add(error); }
        var children = new List<Task>();
        try { if (_ink is not null) { _ink.Changed -= InkChanged; children.Add(_ink.CloseAndDrainAsync()); } }
        catch (Exception error) { errors.Add(error); }
        try { if (_eraser is not null) { _eraser.Changed -= InkChanged; children.Add(_eraser.CloseAndDrainAsync()); } }
        catch (Exception error) { errors.Add(error); }
        try { if (_viewport is not null) children.Add(_viewport.CloseAndDrainAsync()); }
        catch (Exception error) { errors.Add(error); }
        foreach (var actual in children)
            try { await actual; } catch (Exception error) { errors.Add((Exception?)actual.Exception ?? error); }
        try { _tools?.Dispose(); } catch (Exception error) { errors.Add(error); }
        Task? sceneClose = null;
        try { if (_scene is not null) { sceneClose = _scene.CloseOriginalAsync(); await sceneClose; } }
        catch (Exception error) { errors.Add((Exception?)sceneClose?.Exception ?? error); }
        try { Content = null; } catch (Exception error) { errors.Add(error); }
        try { _bindings?.Dispose(); } catch (Exception error) { errors.Add(error); }
        // No original input/render callback can still borrow this document after child joins.
        try { _document?.Dispose(); } catch (Exception error) { errors.Add(error); }
        try { _lifetime.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Canvas surface callbacks and native children did not drain.", errors);
    }
    public void Dispose() { _ = CloseAndDrainAsync(); }
}
