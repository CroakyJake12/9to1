using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;
namespace HavenOS.Apps.Canvas;
public sealed record CanvasNativeLayerCommit(HostedItemId FileId,CanvasArtifact Artifact,FilesRevision FilesRevision,Guid OperationId);
/// <summary>Owning primitive only. A host must retain acknowledgement before separate audit/adoption awaits.</summary>
public sealed class CanvasHomeNativeLayerOperation(HomeResourceOperationBroker home,IAuthenticatedResourceActorSource actors,
    CanvasHomeClaimedNativeLayerStore store)
{
    internal bool IsBoundToHome(HomeResourceOperationBroker broker)=>ReferenceEquals(home,broker)&&store.IsBoundToHome(broker);

    public async Task<CanvasNativeLayerCommit> ExecuteAsync(CanvasNativeLayerEditIntent intent,HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(intent);ArgumentNullException.ThrowIfNull(capability);
        if(!store.IsBoundToHome(home)||!ReferenceEquals(intent.Issuer,store))
            throw new NotSupportedException("Original native layer operation composition changed.");
        store.RequireOriginal(intent);
        if(await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)!=intent.OriginalActor)
            throw new UnauthorizedAccessException("Original native layer actor changed.");
        await store.RequireBaselineAsync(intent,cancellationToken).ConfigureAwait(false);
        var snapshot=CanvasArtifactCodec.Deserialize(intent.CopyPrepared());
        var claimed=await home.ClaimExecutionWithOriginalWriteObservedAsync(capability,CanvasStrokeWriteIntent.TargetAppId,CanvasStrokeWriteIntent.ActionId,
            intent.Scopes,intent.Arguments,intent.Display.OriginalWrite,cancellationToken).ConfigureAwait(false);
        if(claimed.Disposition!=HomeResourceClaimDisposition.Claimed||claimed.Actor!=intent.OriginalActor)throw new UnauthorizedAccessException("Home refused exact original layer proposal.");
        var revision=await store.SaveAsync(intent,capability,claimed.Actor!,cancellationToken).ConfigureAwait(false);
        return new(intent.FileId,snapshot,revision,intent.OperationId);
    }
}
