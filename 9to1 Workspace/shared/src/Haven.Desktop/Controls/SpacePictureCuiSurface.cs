using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core.Media;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using HavenOS.Home.Core;
using HavenOS.Images;

namespace Haven.Desktop.Controls;

/// <summary>Owning Picture preview over a canonical Space reference and retained Files source lease.</summary>
public sealed class SpacePictureCuiSurface(SpaceFilesArtifactAction action, SpaceFilesArtifactActionRouter router,
    NativeFilesArtifactContentReader content, NativeFilesMediaAssetSourceResolver media, HomeCoreRuntime home,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources) : UserControl, IActivatablePage, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private PicturePinnedRasterSource? _source;
    private PictureArtifactEnvelope? _artifact;
    private PictureCuiWorkspace? _bindings;
    private CuiSceneHost? _scene;
    private HomeResourceCuiReadiness? _readiness;
    private Bitmap? _bitmap;
    private bool _initialized;
    private bool _disposed;
    private bool _available;
    private bool _sourceInvalidated;
    private int _operations;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("This captured Picture source is already open.");
        _initialized = true;
        Interlocked.Increment(ref _operations);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        try
        {
            if (action.Writing) throw new ArgumentException("Opening a Picture source requires a read route.", nameof(action));
            var target = await router.ResolveAsync(action, token);
            if (target.Artifact.OwnerAppId != "picture") throw new NotSupportedException("This source requires its owning app surface.");
            var canonical = await content.ReadAsync("picture", target.Artifact.FileId, cancellationToken: token);
            _artifact = PictureArtifactCodec.Deserialize(canonical.Bytes);
            if (canonical.Metadata.CurrentRevisionId != action.ExpectedFilesRevision || _artifact.BackingFileId != action.FileId ||
                !Guid.TryParse(target.Artifact.ArtifactId, out var documentId) || documentId != _artifact.Document.DocumentId ||
                canonical.Revision.OwningAppRevisionId != _artifact.Document.DocumentId.ToString("N") + ":" + _artifact.Document.Revision.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("Picture content differs from its canonical Files identity or revision.");
            _readiness = new HomeResourceCuiReadiness(home, actors, resources, target.ActionId,
                _ => ValueTask.FromResult(target.Scopes));
            await RequireReadyAsync(token);
            var renderer = new PictureFilesSourceRenderer((source, ct) => media.ResolveRetainedAsync(source.FileId.ToString(),
                new MediaAssetId(source.AssetId), source.RevisionId.ToString(), ct), resources);
            _source = await renderer.LoadAnimationWithGlycinAsync(_artifact, action.ExpectedFilesRevision.Value, new PictureGlycinDecoder(), token);
            token.ThrowIfCancellationRequested();
            await router.ResolveAsync(action, token);
            await RequireReadyAsync(token);
            _available = true;
            _bindings = new PictureCuiWorkspace(DispatchAsync, kind => !_disposed && _available &&
                kind == PictureWorkspaceCommandKind.NextFrame && _source?.CanAdvanceFrames == true);
            UpdateBindings();
            var registry = new CuiControlRegistry();
            registry.RegisterControlType("PictureRasterSurface", _ => _image);
            _scene = new CuiSceneHost(registry);
            var state = await _scene.ShowAsync(new("picture", "Picture", "Picture", PictureCuiWorkspace.LoadDocument(),
                _bindings, _bindings, _readiness), token);
            token.ThrowIfCancellationRequested();
            if (state.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(state.Message);
            Render();
            Content = _scene;
        }
        catch { Dispose(); throw; }
        finally { EndOperation(); }
    }

    private async ValueTask DispatchAsync(PictureWorkspaceCommand command, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || !_available || _source is null || _artifact is null || command.Kind != PictureWorkspaceCommandKind.NextFrame ||
            command.DocumentId != _artifact.Document.DocumentId || command.BaseRevision != _artifact.Document.Revision ||
            command.BackingFileId != _artifact.BackingFileId)
            throw new UnauthorizedAccessException("This Picture preview operation is unavailable.");
        Interlocked.Increment(ref _operations);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            await RequireReadyAsync(linked.Token);
            await _source.AdvanceFrameAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            await RequireReadyAsync(linked.Token);
            Render();
            UpdateBindings();
        }
        catch { Clear(); throw; }
        finally { EndOperation(); }
    }

    private async Task RequireReadyAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var state = await _readiness!.CheckAsync(token);
        if (state.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(state.Message);
    }

    private void Render()
    {
        if (_disposed || !_available || _sourceInvalidated)
            throw new UnauthorizedAccessException("The Picture preview is no longer available.");
        var next = _source!.Render(); // Owning renderer replays the canonical non-destructive composition.
        var previous = _bitmap;
        _bitmap = next;
        _image.Source = next;
        previous?.Dispose();
    }
    private void UpdateBindings() => _bindings?.Refresh(_artifact?.Document,
        _available ? "Canonical Files document · read-only preview" : "Source access changed. Reopen the canonical source.",
        _available ? "Native frame stepping. Animation export is unavailable; original source is retained."
            : "Picture preview unavailable", _artifact?.BackingFileId, _available ? _source!.FrameDelayMicroseconds : null);
    private void Clear()
    {
        _available = false;
        _sourceInvalidated = true;
        if (Volatile.Read(ref _operations) == 0) _source?.Dispose();
        _image.Source = null;
        _bitmap?.Dispose(); _bitmap = null;
        UpdateBindings();
    }
    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || !_initialized || !_available) return;
        Interlocked.Increment(ref _operations);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            await RequireReadyAsync(linked.Token);
            await _source!.ValidateAccessAsync(linked.Token);
            await RequireReadyAsync(linked.Token);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidOperationException or OperationCanceledException)
        { Clear(); }
        finally { EndOperation(); }
    }
    public void Deactivate() { }
    private void EndOperation()
    {
        if (Interlocked.Decrement(ref _operations) == 0 && (_disposed || _sourceInvalidated)) _source?.Dispose();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Clear(); Content = null;
        _scene?.Dispose();
        if (Volatile.Read(ref _operations) == 0) _source?.Dispose();
        _lifetime.Dispose();
    }
}
