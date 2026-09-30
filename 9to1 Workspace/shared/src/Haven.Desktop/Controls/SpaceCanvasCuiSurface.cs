using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using HavenOS.Apps.Canvas;
using HavenOS.Home.Core;

namespace Haven.Desktop.Controls;

/// <summary>Native owning-app scene mounted from a canonical Space source; no second artifact store.</summary>
public sealed class SpaceCanvasCuiSurface(SpaceFilesArtifactAction action, SpaceFilesArtifactActionRouter router,
    NativeFilesArtifactContentReader content, HomeCoreRuntime home, IAuthenticatedResourceActorSource actors,
    ResourceAuthorizationService resources) : UserControl, IActivatablePage, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CuiSceneHost? _scene;
    private CanvasCuiWorkspace? _bindings;
    private CanvasSpatialViewport? _viewport;
    private CanvasRnoteDocument? _document;
    private bool _disposed;
    private bool _initialized;
    private int _operations;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("This captured Space route has already been opened.");
        _initialized = true;
        Interlocked.Increment(ref _operations);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        try
        {
            if (action.Writing) throw new ArgumentException("Opening a Space surface requires a read route.", nameof(action));
            var target = await router.ResolveAsync(action, token);
            if (target.Artifact.OwnerAppId != "canvas") throw new NotSupportedException("This source requires its owning app surface.");
            var canonical = await content.ReadAsync("canvas", target.Artifact.FileId, cancellationToken: token);
            if (canonical.Metadata.CurrentRevisionId != action.ExpectedFilesRevision)
                throw new InvalidOperationException("The Files revision changed before the surface opened.");
            var readiness = new HomeResourceCuiReadiness(home, actors, resources, target.ActionId,
                _ => ValueTask.FromResult(target.Scopes));
            if ((await readiness.CheckAsync(token)).State != CuiSceneAvailabilityState.Ready)
                throw new UnauthorizedAccessException("Home cannot authorise this Space source.");
            token.ThrowIfCancellationRequested();
            _document = CanvasRnoteDocument.Open(canonical.Bytes);
            if (!Guid.TryParse(target.Artifact.ArtifactId, out var artifactId) || _document.Snapshot.ArtifactId != artifactId ||
                !Guid.TryParse(canonical.Revision.OwningAppRevisionId, out var revisionId) || _document.Snapshot.RevisionId != revisionId)
                throw new InvalidDataException("The native document does not match its canonical Files identity and revision.");
            _viewport = new CanvasSpatialViewport(_document, readiness);
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
            await router.ResolveAsync(action, token);
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
        try { return await _viewport.RefreshAsync(cancellationToken); }
        finally { EndOperation(); }
    }

    public Task ActivateAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);
    public void Deactivate() { }

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
