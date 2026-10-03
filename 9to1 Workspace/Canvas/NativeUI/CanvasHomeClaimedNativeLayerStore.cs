using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;
namespace HavenOS.Apps.Canvas;

/// <summary>Explicit original display composition; no default host or ambient provider adoption.</summary>
public sealed class CanvasHomeClaimedNativeLayerStore(CanvasFilesArtifactBridge files,HomeResourceOperationBroker home,
    FileHomeCoreStateStore homeStore,HomeLocalProfileIdentity profiles,HomeResourceStoreOwnershipAuthority ownership,
    CanvasHomeClaimedNativeInsertionStore originalDisplayIssuer,Func<bool> isOriginalHostCurrent)
{
    internal bool IsBoundToHome(HomeResourceOperationBroker broker)=>ReferenceEquals(home,broker);
    internal void RequireComposition()
    {
        if(!originalDisplayIssuer.IsBoundToHome(home)||!HomeLocalCommitComposition.IsBound(home,homeStore,profiles,ownership))
            throw new NotSupportedException("Original Canvas layer commit composition unavailable.");
        originalDisplayIssuer.RequireOriginalComposition();
    }
    public CanvasNativeLayerEditIntent Capture(CanvasHomeOriginalInsertionDisplay display,CanvasNativeLayerEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(display);ArgumentNullException.ThrowIfNull(command);RequireComposition();
        if(!ReferenceEquals(display.Issuer,originalDisplayIssuer)||!isOriginalHostCurrent())
            throw new UnauthorizedAccessException("Original native layer display unavailable.");
        files.RequireOriginalDisplayProvider(display.Opened.OriginalSelection!,display.Provider,display.Actor,display.FileId,display.Opened.StoreId);
        return new(this,display,command);
    }
    internal void RequireOriginal(CanvasNativeLayerEditIntent intent)
    {
        RequireComposition();
        if(!ReferenceEquals(intent.Issuer,this)||!ReferenceEquals(intent.Display.Issuer,originalDisplayIssuer)||!isOriginalHostCurrent())
            throw new UnauthorizedAccessException("Exact original layer proposal unavailable.");
        files.RequireOriginalDisplayProvider(intent.Display.Opened.OriginalSelection!,intent.Display.Provider,intent.OriginalActor,intent.FileId,intent.StoreId);
    }
    internal async Task RequireBaselineAsync(CanvasNativeLayerEditIntent intent,CancellationToken cancellationToken)
    {
        RequireOriginal(intent);
        var original=await files.OpenOriginalDisplayAsync(intent.Display.Opened.OriginalSelection!,cancellationToken).ConfigureAwait(false);
        if(original.CasRevisionId!=intent.FilesRevision||original.Artifact.ArtifactId!=intent.ArtifactId||
            original.Artifact.RevisionId!=intent.OriginalRevisionId||
            CanvasNativeLayerEditIntent.Hash(CanvasArtifactCodec.Serialize(original.Artifact))!=intent.OriginalContentHash)
            throw new InvalidOperationException("Original native layer baseline changed.");
    }
    public Task<HomePermissionAuthorization> AuthorizeAsync(CanvasNativeLayerEditIntent intent,string preview,
        string? backupId,string sessionId,CancellationToken cancellationToken=default)
    {
        RequireOriginal(intent);
        return home.AuthorizeWithOriginalWriteForActorAsync(intent.OriginalActor,intent.Display.OriginalWrite,
            CanvasStrokeWriteIntent.TargetAppId,CanvasStrokeWriteIntent.ActionId,intent.Scopes,intent.Arguments,
            preview,backupId,sessionId,cancellationToken);
    }
    public Task<HomeResourceExecutionCapability?> BeginExecutionAsync(CanvasNativeLayerEditIntent intent,string requestId,
        CancellationToken cancellationToken=default)
    {
        RequireOriginal(intent);
        return home.BeginExecutionWithOriginalWriteCapabilityAsync(requestId,intent.Arguments,intent.Display.OriginalWrite,cancellationToken);
    }
    public async Task<FilesRevision> SaveAsync(CanvasNativeLayerEditIntent intent,HomeResourceExecutionCapability capability,
        AuthenticatedResourceActor claimedActor,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(intent);ArgumentNullException.ThrowIfNull(capability);ArgumentNullException.ThrowIfNull(claimedActor);
        RequireOriginal(intent);
        if(claimedActor!=intent.OriginalActor||!home.MatchesClaimedOriginalArguments(capability,
            CanvasStrokeWriteIntent.TargetAppId,CanvasStrokeWriteIntent.ActionId,intent.Scopes,intent.Arguments))
            throw new UnauthorizedAccessException("Exact claimed native layer command unavailable.");
        await RequireBaselineAsync(intent,cancellationToken).ConfigureAwait(false);
        var bytes=intent.CopyPrepared();
        if(CanvasNativeLayerEditIntent.Hash(bytes)!=intent.PreparedHash)throw new InvalidDataException("Prepared layer proposal changed.");
        var candidate=CanvasArtifactCodec.Deserialize(bytes);
        if(candidate.ArtifactId!=intent.ArtifactId||candidate.RevisionId==intent.OriginalRevisionId)
            throw new InvalidDataException("Prepared native layer identity changed.");
        return await files.SaveOriginalPreparedWithOriginalWriteAuthorityAsync(intent.Display.Opened.OriginalSelection!,intent.Display.OriginalWrite,intent.FileId,candidate,
            intent.FilesRevision,intent.StoreId,claimedActor,async(actor,provider,ct)=>
            {
                if(actor!=intent.OriginalActor||!isOriginalHostCurrent())throw new UnauthorizedAccessException("Original layer host changed before final admission.");
                // This is the ORIGINAL pre-review provider/configuration, never a fresh replacement capture.
                if(!ReferenceEquals(provider,intent.Display.Provider))throw new UnauthorizedAccessException("Original layer provider changed.");
                var configuration=intent.CopyConfiguration();
                if(CanvasNativeLayerEditIntent.Hash(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(configuration))!=intent.OriginalConfigurationHash)
                    throw new InvalidDataException("Original layer configuration changed.");
                var fence=await HomeClaimedResourceCommitFence.CaptureAsync(home,homeStore,profiles,ownership,"files",
                    intent.StoreId.ToString("D"),capability,actor,[configuration],isOriginalHostCurrent,ct).ConfigureAwait(false)
                    ??throw new UnauthorizedAccessException("Original claimed layer final fence unavailable.");
                return new CanvasFilesFinalAuthority(actor.ActorId,token=>fence.ValidateAsync(token),fence);
            },cancellationToken).ConfigureAwait(false);
    }
}
