using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using HavenOS.Apps.Canvas;
using HavenOS.Home.Core;

namespace Haven.Desktop.Controls;

/// <summary>Exact Space/Files authority adapter over the one owning native Canvas surface.</summary>
public sealed class SpaceCanvasCuiSurface(SpaceFilesArtifactAction action, SpaceFilesArtifactActionRouter router,
    NativeFilesArtifactContentReader content, HomeCoreRuntime home, IAuthenticatedResourceActorSource actors,
    ResourceAuthorizationService resources) : UserControl, IActivatablePage, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CanvasNativeCuiSurface? _surface;
    private bool _initialized;
    private bool _disposed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("This captured Canvas source is already open.");
        _initialized = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            if (action.Writing) throw new ArgumentException("Opening a Canvas source requires a read route.", nameof(action));
            var target = await router.ResolveAsync(action, linked.Token);
            if (target.Artifact.OwnerAppId != "canvas") throw new NotSupportedException("This source requires its owning app surface.");
            var readiness = new HomeResourceCuiReadiness(home, actors, resources, target.ActionId,
                async token => (await router.ResolveAsync(action, token)).Scopes);
            _surface = new CanvasNativeCuiSurface(OpenCapturedAsync, readiness);
            await _surface.InitializeAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            Content = _surface;
        }
        catch { Dispose(); throw; }
    }

    private async Task<CanvasRnoteDocument> OpenCapturedAsync(CancellationToken token)
    {
        var target = await router.ResolveAsync(action, token);
        var canonical = await content.ReadAsync("canvas", target.Artifact.FileId, cancellationToken: token);
        if (canonical.Metadata.CurrentRevisionId != action.ExpectedFilesRevision)
            throw new InvalidOperationException("The Files revision changed before the surface opened.");
        var document = CanvasRnoteDocument.Open(canonical.Bytes);
        try
        {
            if (!Guid.TryParse(target.Artifact.ArtifactId, out var artifactId) || document.Snapshot.ArtifactId != artifactId ||
                !Guid.TryParse(canonical.Revision.OwningAppRevisionId, out var revisionId) || document.Snapshot.RevisionId != revisionId)
                throw new InvalidDataException("The native document does not match its canonical Files identity and revision.");
            await router.ResolveAsync(action, token);
            token.ThrowIfCancellationRequested();
            return document;
        }
        catch { document.Dispose(); throw; }
    }

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        return !_disposed && _surface is not null && await _surface.RefreshAsync(cancellationToken);
    }
    public Task ActivateAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);
    public void Deactivate() { }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        _surface?.Dispose(); Content = null; _lifetime.Dispose();
    }
}
