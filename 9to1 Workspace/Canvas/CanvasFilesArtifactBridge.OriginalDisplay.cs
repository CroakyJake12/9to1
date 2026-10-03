using System.Security.Cryptography;
using Haven.Application;
using HavenOS.Files;
namespace HavenOS.Apps.Canvas;

public interface ICanvasOriginalDisplaySelection : IDisposable { }

public sealed partial class CanvasFilesArtifactBridge
{
    private sealed class OriginalDisplaySelection(CanvasFilesArtifactBridge issuer, DurableDriveProvider provider,
        AuthenticatedResourceActor actor, HostedItemId fileId, CanvasFilesOpenResult opened, IOriginalCanonicalReadContext? originalReadContext = null, FilesWorkspaceDirectoryBinding? originalBinding = null) : ICanvasOriginalDisplaySelection
    {
        internal CanvasFilesArtifactBridge Issuer { get; } = issuer;
        internal IOriginalCanonicalReadContext? OriginalReadContext { get; } = originalReadContext;
        internal FilesWorkspaceDirectoryBinding? OriginalBinding { get; } = originalBinding;
        internal DurableDriveProvider Provider { get; } = provider;
        internal AuthenticatedResourceActor Actor { get; } = actor;
        internal HostedItemId FileId { get; } = fileId;
        internal Guid StoreId { get; } = opened.StoreId;
        internal FilesRevisionId CasRevisionId { get; } = opened.CasRevisionId;
        internal Guid ArtifactId { get; } = opened.Artifact.ArtifactId;
        internal Guid ArtifactRevision { get; } = opened.Artifact.RevisionId;
        internal string ArtifactHash { get; } = Convert.ToHexString(SHA256.HashData(CanvasArtifactCodec.Serialize(opened.Artifact)));
        private readonly SemaphoreSlim _commits = new(1, 1);
        private int _retired;
        internal bool Available => Volatile.Read(ref _retired) == 0;
        internal void RetireObservedMaterialization()=>Volatile.Write(ref _retired,1);
        internal async ValueTask<IDisposable> RetainAsync(CancellationToken ct)
        {
            await _commits.WaitAsync(ct).ConfigureAwait(false);
            if (!Available) { _commits.Release(); throw new UnauthorizedAccessException("Original Canvas display retired."); }
            return new Retention(_commits);
        }
        public void Dispose()
        {
            _commits.Wait();
            try { Volatile.Write(ref _retired, 1); }
            finally { _commits.Release(); }
        }
        private sealed class Retention(SemaphoreSlim gate) : IDisposable
        { private int _released; public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release(); } }
    }

    public async Task<CanvasFilesOpenResult> OpenForDisplayAsync(HostedItemId fileId, Guid expectedStoreId,
        AuthenticatedResourceActor originalActor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        if (expectedStoreId == Guid.Empty) throw new ArgumentException("Original Files store identity required.", nameof(expectedStoreId));
        if (await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != originalActor)
            throw new UnauthorizedAccessException("Original Canvas display actor unavailable.");
        var provider = providers(originalActor) ?? throw new UnauthorizedAccessException("Original Canvas provider unavailable.");
        var opened = await OpenOriginalProviderAsync(fileId, expectedStoreId, originalActor, provider, () => true, cancellationToken).ConfigureAwait(false);
        if (opened.StoreId != expectedStoreId || !ReferenceEquals(providers(originalActor), provider) ||
            await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != originalActor)
            throw new UnauthorizedAccessException("Original Canvas display changed while opening.");
        return opened with { OriginalSelection = new OriginalDisplaySelection(this, provider, originalActor, fileId, opened) };
    }

    public bool IsOriginalDisplayAvailable(ICanvasOriginalDisplaySelection selection)
        => selection is OriginalDisplaySelection original && ReferenceEquals(original.Issuer,this) && original.Available;

    public void RequireOriginalWorkspaceDirectories(FilesWorkspaceDirectoryResolver originalDirectories)
    {
        if (!ReferenceEquals(directories, originalDirectories))
            throw new UnauthorizedAccessException("Original Canvas directory materializer was substituted.");
    }

    public async Task<CanvasFilesOpenResult> OpenForDisplayAsync(HostedItemId fileId, Guid expectedStoreId,
        AuthenticatedResourceActor originalActor, DurableDriveProvider originalProvider,
        IOriginalCanonicalReadContext originalReadContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalActor);ArgumentNullException.ThrowIfNull(originalProvider);
        ArgumentNullException.ThrowIfNull(originalReadContext);
        if (expectedStoreId == Guid.Empty || originalReadContext.OriginalScope.Id != fileId.ToString()
            || originalReadContext.OriginalScope.Kind != "files.item" || originalReadContext.OriginalScope.Access != ResourceAccess.Read)
            throw new UnauthorizedAccessException("Original Canvas read scope unavailable.");
        var read = await OpenOriginalProviderAsync(fileId, expectedStoreId, originalActor, originalProvider,
            originalReadContext, () => true, cancellationToken).ConfigureAwait(false);
        var opened=read.Opened;
        return opened with { OriginalSelection = new OriginalDisplaySelection(this, originalProvider, originalActor, fileId, opened, originalReadContext, read.Binding) };
    }

    public void RequireOriginalDisplayProvider(ICanvasOriginalDisplaySelection originalSelection,
        DurableDriveProvider originalProvider, AuthenticatedResourceActor originalActor, HostedItemId fileId, Guid originalStoreId)
    {
        if (originalSelection is not OriginalDisplaySelection selected || !ReferenceEquals(selected.Issuer,this) || !selected.Available ||
            !ReferenceEquals(selected.Provider,originalProvider) || !ReferenceEquals(providers(originalActor),selected.Provider) || selected.Actor!=originalActor || selected.FileId!=fileId || selected.StoreId!=originalStoreId)
            throw new UnauthorizedAccessException("Original Canvas display is not bound to the original physical provider.");
    }

    public Task<FilesRevision> SaveOriginalPreparedWithFinalAuthorityAsync(ICanvasOriginalDisplaySelection originalSelection,
        HostedItemId fileId, CanvasArtifact prepared, FilesRevisionId expectedFileRevision, Guid expectedStoreId,
        AuthenticatedResourceActor claimedActor,
        Func<AuthenticatedResourceActor, DurableDriveProvider, CancellationToken, ValueTask<CanvasFilesFinalAuthority>> captureFinalAuthority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared); ArgumentNullException.ThrowIfNull(captureFinalAuthority);
        if (originalSelection is not OriginalDisplaySelection selected || !ReferenceEquals(selected.Issuer, this) || !selected.Available ||
            selected.Actor != claimedActor || selected.FileId != fileId || selected.StoreId != expectedStoreId ||
            selected.CasRevisionId != expectedFileRevision || selected.ArtifactId != prepared.ArtifactId)
            throw new UnauthorizedAccessException("Original Canvas private display provenance unavailable.");
        return SaveCoreAsync(fileId, prepared, expectedFileRevision, claimedActor, expectedStoreId,
            cancellationToken, captureFinalAuthority, selected);
    }
}
