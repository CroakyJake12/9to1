using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core.Media;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using HavenOS.Home.Core;
using HavenOS.Images;

namespace Haven.Desktop.Controls;

/// <summary>Space authority adapter over the owning native Picture surface and retained Files source lease.</summary>
public sealed class SpacePictureCuiSurface(SpaceFilesArtifactAction action, SpaceFilesArtifactActionRouter router,
    NativeFilesArtifactContentReader content, NativeFilesMediaAssetSourceResolver media, HomeCoreRuntime home,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources,
    IMotionPreferenceSource? motionPreferences = null) : UserControl, IActivatablePage, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private PictureNativeCuiSurface? _surface;
    private bool _initialized;
    private bool _disposed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("This captured Picture source is already open.");
        _initialized = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            if (action.Writing) throw new ArgumentException("Opening a Picture source requires a read route.", nameof(action));
            var target = await router.ResolveAsync(action, linked.Token);
            if (target.Artifact.OwnerAppId != "picture") throw new NotSupportedException("This source requires its owning app surface.");
            var readiness = new HomeResourceCuiReadiness(home, actors, resources, target.ActionId,
                async token => (await router.ResolveAsync(action, token)).Scopes);
            var renderer = new PictureFilesSourceRenderer((source, token) => media.ResolveRetainedAsync(source.FileId.ToString(),
                new MediaAssetId(source.AssetId), source.RevisionId.ToString(), token), resources);
            _surface = new PictureNativeCuiSurface(OpenCapturedAsync, renderer, new PictureGlycinDecoder(), readiness, motionPreferences: motionPreferences);
            await _surface.InitializeAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            Content = _surface;
        }
        catch { Dispose(); throw; }
    }

    private async Task<PictureFilesOpenResult> OpenCapturedAsync(CancellationToken token)
    {
        var target = await router.ResolveAsync(action, token);
        var canonical = await content.ReadAsync("picture", target.Artifact.FileId, cancellationToken: token);
        var artifact = PictureArtifactCodec.Deserialize(canonical.Bytes);
        if (canonical.Metadata.CurrentRevisionId != action.ExpectedFilesRevision || artifact.BackingFileId != action.FileId ||
            !Guid.TryParse(target.Artifact.ArtifactId, out var documentId) || documentId != artifact.Document.DocumentId ||
            canonical.Revision.OwningAppRevisionId != artifact.Document.DocumentId.ToString("N") + ":" + artifact.Document.Revision.ToString(CultureInfo.InvariantCulture))
            throw new InvalidDataException("Picture content differs from its canonical Files identity or revision.");
        await router.ResolveAsync(action, token);
        return new(artifact, canonical.Revision, action.ExpectedFilesRevision);
    }

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || _surface is null) return;
        try { await _surface.ValidateAccessAsync(cancellationToken); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidOperationException or OperationCanceledException)
        { /* The owning surface has already cleared and disposed its displayed copy on denial. */ }
    }
    public void Deactivate() { }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        _surface?.Dispose(); Content = null; _lifetime.Dispose();
    }
}
