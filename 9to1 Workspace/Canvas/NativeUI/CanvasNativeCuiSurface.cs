using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using HavenOS.Files;

namespace HavenOS.Apps.Canvas;

/// <summary>Owning Canvas CUI over a document opened by the host's exact canonical Files proof.
/// The supplied document transfers ownership to this surface; readiness remains the shared Home policy.</summary>
public sealed class CanvasNativeCuiSurface(Func<CancellationToken, Task<CanvasRnoteDocument>> open,
    ICuiSceneReadiness readiness, CanvasNativeInkContext? ink = null) : UserControl, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CuiSceneHost? _scene;
    private CanvasCuiWorkspace? _bindings;
    private CanvasNativeViewport? _viewport;
    private CanvasRnoteDocument? _document;
    private CanvasToolState? _tools;
    private CanvasNativeInkInput? _ink;
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
                if (_document.Identity != (ink.ArtifactId, ink.ArtifactRevision))
                    throw new InvalidDataException("The pen context differs from the exact displayed Canvas identity/revision.");
                _tools = new CanvasToolState(tool => tool == CanvasPrimaryTool.Pen && _available &&
                    ink.IsAvailable() && _ink?.HasSubmittedStroke != true
                        ? new(true, "Capture native pen input, then review the exact stroke in Home.")
                        : CanvasToolCapability.Unavailable(_ink?.Status ?? "This native tool has no current owning input operation."), _ => { });
            }
            _bindings = new CanvasCuiWorkspace((_, _) => throw new NotSupportedException("This command has no authorised owning operation."), _ => false, _tools);
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
                    () => _available && !_disposed && ink.IsAvailable(), ink.RequestOperation);
                _ink.Changed += InkChanged;
            }
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
        _tools?.Dispose();
        Content = null;
        _scene?.Dispose();
        _viewport?.Dispose();
        _bindings?.Dispose();
        if (Volatile.Read(ref _operations) == 0) _document?.Dispose();
        _lifetime.Dispose();
    }
}
