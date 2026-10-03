using System.Security.Cryptography;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
namespace HavenOS.Apps.Canvas;

public interface ICanvasHomeClaimedNativeInsertionStore
{
    Task<FilesRevision> SaveAsync(CanvasNativeInsertionIntent intent, HomeResourceExecutionCapability capability,
        AuthenticatedResourceActor claimedActor, CancellationToken cancellationToken = default);
}

/// <summary>Exact typed donor proposal is the sole input; the owning composition supplies original provider configuration.</summary>
public sealed class CanvasHomeClaimedNativeInsertionStore(CanvasFilesArtifactBridge files, HomeResourceOperationBroker home,
    FileHomeCoreStateStore homeStore, HomeLocalProfileIdentity profiles, HomeResourceStoreOwnershipAuthority ownership,
    Func<bool> isOriginalHostCurrent, FilesArtifactResourceResolver? originalReadOwner) : ICanvasHomeClaimedNativeInsertionStore
{
    public CanvasHomeClaimedNativeInsertionStore(CanvasFilesArtifactBridge files, HomeResourceOperationBroker home,
        FileHomeCoreStateStore homeStore, HomeLocalProfileIdentity profiles, HomeResourceStoreOwnershipAuthority ownership,
        Func<bool> isOriginalHostCurrent) : this(files,home,homeStore,profiles,ownership,isOriginalHostCurrent,null) { }

    internal bool OriginalHostCurrent()=>isOriginalHostCurrent();
    internal ValueTask<IOriginalCanonicalReadContext> CaptureOriginalReadAsync(NativeFilesWorkspace workspace,
        HostedItemId fileId, Func<bool> originalLifetime, CancellationToken ct)
        => (originalReadOwner ?? throw new NotSupportedException("Registered original Files read issuer unavailable."))
            .CaptureOriginalCanvasReadAsync(workspace,fileId,originalLifetime,ct);

    internal ValueTask<IOriginalCanonicalWriteContext> CaptureOriginalWriteAsync(IOriginalCanonicalReadContext originalRead, CancellationToken ct)
        => (originalReadOwner ?? throw new NotSupportedException("Registered original Files write issuer unavailable."))
            .CaptureOriginalCanvasWriteAsync(originalRead,ct);

    internal bool IsBoundToHome(HomeResourceOperationBroker candidate)=>ReferenceEquals(home,candidate);

    internal void RequireOriginalComposition()
    {
        if(!HomeLocalCommitComposition.IsBound(home,homeStore,profiles,ownership))
            throw new NotSupportedException("Original Canvas Home commit composition is unavailable.");
    }

    public async Task<CanvasHomeOriginalInsertionDisplay> CaptureDisplayAsync(NativeFilesWorkspaceAuthority authority,
        HostedItemId fileId, Guid originalStoreId, AuthenticatedResourceActor originalActor, CancellationToken cancellationToken=default)
    {
        RequireOriginalComposition();
        if(await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false)!=originalActor)
            throw new UnauthorizedAccessException("Original Canvas Home display actor differs from the actual profile.");
        return await CanvasHomeOriginalInsertionDisplay.CaptureAsync(this,files,authority,fileId,originalStoreId,originalActor,cancellationToken).ConfigureAwait(false);
    }

    private void RequireOriginalInsertion(CanvasNativeInsertionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);RequireOriginalComposition();
        if(!ReferenceEquals(intent.OriginalClaimedStore,this)||!isOriginalHostCurrent())
            throw new UnauthorizedAccessException("Original insertion issuer/host unavailable.");
        files.RequireOriginalDisplayProvider(intent.OriginalSelection,intent.OriginalProvider,intent.OriginalActor,intent.FileId,intent.StoreId);
    }
    public Task<HomePermissionAuthorization> AuthorizeAsync(CanvasNativeInsertionIntent intent,string preview,
        string? backupId,string sessionId,CancellationToken cancellationToken=default)
    {
        RequireOriginalInsertion(intent);
        return home.AuthorizeWithOriginalWriteForActorAsync(intent.OriginalActor,intent.OriginalWrite,
            CanvasStrokeWriteIntent.TargetAppId,CanvasStrokeWriteIntent.ActionId,intent.Scopes,intent.Arguments,
            preview,backupId,sessionId,cancellationToken);
    }
    public Task<HomeResourceExecutionCapability?> BeginExecutionAsync(CanvasNativeInsertionIntent intent,string requestId,
        CancellationToken cancellationToken=default)
    {
        RequireOriginalInsertion(intent);
        return home.BeginExecutionWithOriginalWriteCapabilityAsync(requestId,intent.Arguments,intent.OriginalWrite,cancellationToken);
    }
    public async Task<FilesRevision> SaveAsync(CanvasNativeInsertionIntent intent, HomeResourceExecutionCapability capability,
        AuthenticatedResourceActor claimedActor, CancellationToken cancellationToken = default)
    {
        RequireOriginalComposition();
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(claimedActor);
        if (!ReferenceEquals(intent.OriginalClaimedStore,this) || claimedActor != intent.OriginalActor || !isOriginalHostCurrent() ||
            !home.MatchesClaimedOriginalArguments(capability, CanvasStrokeWriteIntent.TargetAppId,
                CanvasStrokeWriteIntent.ActionId, intent.Scopes, intent.Arguments))
            throw new UnauthorizedAccessException("Exact original native insertion claim unavailable.");
        files.RequireOriginalDisplayProvider(intent.OriginalSelection,intent.OriginalProvider,intent.OriginalActor,intent.FileId,intent.StoreId);
        var original=await files.OpenOriginalDisplayAsync(intent.OriginalSelection,cancellationToken).ConfigureAwait(false);
        if (original.CasRevisionId!=intent.FilesRevision || original.Artifact.ArtifactId!=intent.ArtifactId ||
            original.Artifact.RevisionId!=intent.OriginalRevisionId ||
            Convert.ToHexString(SHA256.HashData(CanvasArtifactCodec.Serialize(original.Artifact)))!=intent.OriginalContentHash)
            throw new InvalidOperationException("Original native insertion baseline changed.");
        var bytes=intent.CopyPrepared();
        if(Convert.ToHexString(SHA256.HashData(bytes))!=intent.PreparedHash)
            throw new InvalidDataException("Captured native insertion proposal changed.");
        var candidate=CanvasArtifactCodec.Deserialize(bytes);
        if(candidate.ArtifactId!=intent.ArtifactId || !candidate.Pages.Single().StrokeOrder.SequenceEqual(intent.StrokeOrder) ||
            candidate.Pages.Single().Strokes.Count(stroke=>stroke.StrokeId==intent.StrokeId)!=1)
            throw new InvalidDataException("Captured native insertion identity/order changed.");
        return await files.SaveOriginalPreparedWithOriginalWriteAuthorityAsync(intent.OriginalSelection,intent.OriginalWrite,intent.FileId,candidate,intent.FilesRevision,intent.StoreId,claimedActor,
            async (actor,provider,ct)=>
            {
                if(actor!=intent.OriginalActor || !isOriginalHostCurrent())
                    throw new UnauthorizedAccessException("Original Canvas host changed before final admission.");
                var configuration=intent.CopyOriginalConfiguration();
                if(Convert.ToHexString(SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(configuration)))!=intent.OriginalConfigurationHash)
                    throw new InvalidDataException("Original Canvas configuration fingerprint changed.");
                var fence=await HomeClaimedResourceCommitFence.CaptureAsync(home,homeStore,profiles,ownership,"files",
                    intent.StoreId.ToString("D"),capability,actor,[configuration],isOriginalHostCurrent,ct).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("Original Canvas claimed final fence unavailable.");
                return new CanvasFilesFinalAuthority(actor.ActorId,token=>fence.ValidateAsync(token),fence);
            },cancellationToken).ConfigureAwait(false);
    }
}
