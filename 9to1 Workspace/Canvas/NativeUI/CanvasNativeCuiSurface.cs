using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Canvas;

/// <summary>Owning Canvas CUI over a document opened by the host's exact canonical Files proof.
/// The supplied document transfers ownership to this surface; readiness remains the shared Home policy.</summary>
public sealed class CanvasNativeCuiSurface(Func<CancellationToken, Task<CanvasRnoteDocument>> open,
    ICuiSceneReadiness readiness) : UserControl, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CuiSceneHost? _scene;
    private CanvasCuiWorkspace? _bindings;
    private CanvasNativeViewport? _viewport;
    private CanvasRnoteDocument? _document;
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
            _bindings = new CanvasCuiWorkspace((_, _) => throw new NotSupportedException("This surface has no authorised edit operation."), _ => false);
            _bindings.Refresh(_document.Snapshot, "Opened canonical Files revision · read-only view");
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
            await _viewport.RefreshAsync(token);
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
        try { return await _viewport.RefreshAsync(linked.Token); }
        finally { EndOperation(); }
    }

    private void EndOperation()
    {
        if (Interlocked.Decrement(ref _operations) == 0 && _disposed) _document?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Content = null;
        _scene?.Dispose();
        _viewport?.Dispose();
        _bindings?.Dispose();
        if (Volatile.Read(ref _operations) == 0) _document?.Dispose();
        _lifetime.Dispose();
    }
}
