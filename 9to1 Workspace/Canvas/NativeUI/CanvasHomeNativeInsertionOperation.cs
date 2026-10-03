using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;
namespace HavenOS.Apps.Canvas;

/// <summary>Genuine donor-prepared insertion bound before Home review. Immutable proposal data is never authority.</summary>
public sealed class CanvasNativeInsertionIntent
{
    private readonly byte[] _prepared;
    private readonly JsonElement _arguments;
    private readonly HomeCoreStateRecord _originalConfiguration;
    private CanvasNativeInsertionIntent(CanvasHomeOriginalInsertionDisplay display,
        IReadOnlyList<RnotePointerSample> samples,CanvasRnoteInkStyle style,Guid? originalNativeLayerId=null)
    {
        var file=display.FileId;var original=display.Opened;var actor=display.Actor;
        OriginalClaimedStore=display.Issuer;OriginalProvider=display.Provider;OriginalWrite=display.OriginalWrite;
        OriginalSelection=original.OriginalSelection??throw new UnauthorizedAccessException("Original Canvas private display required.");
        _originalConfiguration=display.CopyConfiguration();OriginalConfigurationHash=display.ConfigurationHash;
        if(file.Value==Guid.Empty||original.StoreId==Guid.Empty||original.CasRevisionId.Value==Guid.Empty)
            throw new ArgumentException("Insertion requires original canonical Files provenance.");
        FileId=file;StoreId=original.StoreId;FilesRevision=original.CasRevisionId;OriginalActor=actor;
        ArtifactId=original.Artifact.ArtifactId;OriginalRevisionId=original.Artifact.RevisionId;StrokeId=Guid.NewGuid();
        if(originalNativeLayerId is { } layer && (layer==Guid.Empty||!original.Artifact.Pages.Single().Layers.Any(item=>item.LayerId==layer)))
            throw new ArgumentException("Exact original native layer unavailable.",nameof(originalNativeLayerId));
        CapturedNativeLayerId=originalNativeLayerId;
        var source=CanvasArtifactCodec.Serialize(original.Artifact);
        OriginalContentHash=Convert.ToHexString(SHA256.HashData(source));
        var captured=RnoteCanvasEngine.CaptureSamples(samples);
        using var candidate=CanvasRnoteDocument.Open(source);
        _ = candidate.PreviewSelection(CanvasSelectionStyle.Single,[captured[0]],OriginalRevisionId); // Actual complete native identity/order preflight; imported/unbound fallback is unavailable.
        var mutation=new CanvasMutationRequest(OriginalRevisionId,StrokeId,new(actor.ActorId,actor.ActorId,"Native insertion preview"));
        if(originalNativeLayerId is { } target)candidate.DrawStrokeIntoNativeUserLayer(captured,mutation,target,style);
        else candidate.DrawStroke(captured,mutation,style);
        _prepared=candidate.Serialize();PreparedHash=Convert.ToHexString(SHA256.HashData(_prepared));
        StrokeOrder=candidate.Snapshot.Pages.Single().StrokeOrder.ToImmutableArray();
        if(!StrokeOrder.Where(id=>id!=StrokeId).SequenceEqual(original.Artifact.Pages.Single().StrokeOrder))
            throw new InvalidDataException("Insertion changed original relative order.");
        Scopes=Array.AsReadOnly(new[]{new ResourceScope("files.item",file.ToString(),FilesRevision.ToString(),ResourceAccess.Write)});
        _arguments=JsonSerializer.SerializeToElement(new {operation="stroke.insert.native-order",fileId=file.Value,storeId=StoreId,
            expectedFilesRevision=FilesRevision.Value,artifactId=ArtifactId,expectedArtifactRevision=OriginalRevisionId,
            originalActor=actor.ActorId,originalProfile=actor.ProfileId,OriginalContentHash,StrokeId,style,
            samples=captured,PreparedHash,StrokeOrder,OriginalConfigurationHash},new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if(originalNativeLayerId is { } approvedLayer)
        {
            var explicitArguments=System.Text.Json.Nodes.JsonNode.Parse(_arguments.GetRawText())!.AsObject();
            explicitArguments.Add("originalNativeLayerId",System.Text.Json.Nodes.JsonValue.Create(approvedLayer));
            _arguments=JsonSerializer.SerializeToElement(explicitArguments,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
    }
    public Guid? CapturedNativeLayerId{get;}
    internal IOriginalCanonicalWriteContext OriginalWrite{get;}
    internal CanvasHomeClaimedNativeInsertionStore OriginalClaimedStore{get;}
    internal DurableDriveProvider OriginalProvider{get;}
    public ICanvasOriginalDisplaySelection OriginalSelection{get;}
    public string OriginalConfigurationHash{get;}
    internal HomeCoreStateRecord CopyOriginalConfiguration()=>_originalConfiguration with {Payload=_originalConfiguration.Payload.Clone()};
    public HostedItemId FileId{get;} public Guid StoreId{get;} public FilesRevisionId FilesRevision{get;}
    public AuthenticatedResourceActor OriginalActor{get;} public Guid ArtifactId{get;} public Guid OriginalRevisionId{get;}
    public Guid StrokeId{get;} public string OriginalContentHash{get;} public string PreparedHash{get;}
    public ImmutableArray<Guid> StrokeOrder{get;} public IReadOnlyList<ResourceScope> Scopes{get;}
    public JsonElement Arguments=>_arguments.Clone();internal byte[] CopyPrepared()=>_prepared.ToArray();
    public static CanvasNativeInsertionIntent CaptureIntoNativeUserLayer(CanvasHomeOriginalInsertionDisplay originalDisplay,
        IReadOnlyList<RnotePointerSample> samples,Guid originalLayerId,CanvasRnoteInkStyle? style=null)
    {
        ArgumentNullException.ThrowIfNull(originalDisplay);ArgumentNullException.ThrowIfNull(samples);
        return new(originalDisplay,samples,style??CanvasRnoteInkStyle.Default,originalLayerId);
    }
    public static CanvasNativeInsertionIntent Capture(CanvasHomeOriginalInsertionDisplay originalDisplay,
        IReadOnlyList<RnotePointerSample> samples,CanvasRnoteInkStyle? style=null)
    {
        ArgumentNullException.ThrowIfNull(originalDisplay);ArgumentNullException.ThrowIfNull(samples);
        return new(originalDisplay,samples,style??CanvasRnoteInkStyle.Default);
    }
}
public sealed record CanvasNativeInsertionCommit(HostedItemId FileId,CanvasArtifact Artifact,FilesRevision FilesRevision,Guid StrokeId);
public sealed class CanvasHomeNativeInsertionOperation(CanvasFilesArtifactBridge files,HomeResourceOperationBroker home,
    IAuthenticatedResourceActorSource actors, ICanvasHomeClaimedNativeInsertionStore? claimedStore = null)
{
    internal bool IsBoundToHome(HomeResourceOperationBroker candidate)=>ReferenceEquals(home,candidate);
    public async Task<CanvasNativeInsertionCommit> ExecuteAsync(CanvasNativeInsertionIntent intent,HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(intent);ArgumentNullException.ThrowIfNull(capability);
        if(claimedStore is null || !ReferenceEquals(claimedStore,intent.OriginalClaimedStore))throw new NotSupportedException("Original claimed native insertion publication is unavailable.");
        intent.OriginalClaimedStore.RequireOriginalComposition();
        if(await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)!=intent.OriginalActor)
            throw new UnauthorizedAccessException("Native insertion original actor changed.");
        files.RequireOriginalDisplayProvider(intent.OriginalSelection,intent.OriginalProvider,intent.OriginalActor,intent.FileId,intent.StoreId);
        var opened=await files.OpenOriginalDisplayAsync(intent.OriginalSelection,cancellationToken).ConfigureAwait(false);
        if(opened.CasRevisionId!=intent.FilesRevision||opened.Artifact.ArtifactId!=intent.ArtifactId||
            opened.Artifact.RevisionId!=intent.OriginalRevisionId||
            Convert.ToHexString(SHA256.HashData(CanvasArtifactCodec.Serialize(opened.Artifact)))!=intent.OriginalContentHash)
            throw new InvalidOperationException("Native insertion original source changed.");
        var prepared=intent.CopyPrepared();
        if(Convert.ToHexString(SHA256.HashData(prepared))!=intent.PreparedHash)
            throw new InvalidDataException("Native insertion proposal bytes changed.");
        using var candidate=CanvasRnoteDocument.Open(prepared);var snapshot=candidate.Snapshot;
        if(snapshot.ArtifactId!=intent.ArtifactId||!snapshot.Pages.Single().StrokeOrder.SequenceEqual(intent.StrokeOrder)||
            snapshot.Pages.Single().Strokes.Count(stroke=>stroke.StrokeId==intent.StrokeId)!=1)
            throw new InvalidDataException("Native insertion proposal identity/order changed.");
        var claimed=await home.ClaimExecutionWithOriginalWriteObservedAsync(capability,CanvasStrokeWriteIntent.TargetAppId,CanvasStrokeWriteIntent.ActionId,
            intent.Scopes,intent.Arguments,intent.OriginalWrite,cancellationToken).ConfigureAwait(false);
        if(claimed.Disposition!=HomeResourceClaimDisposition.Claimed||claimed.Actor!=intent.OriginalActor)throw new UnauthorizedAccessException("Home refused exact native insertion proposal.");
        var revision=await claimedStore.SaveAsync(intent,capability,claimed.Actor!,cancellationToken).ConfigureAwait(false);
        return new(intent.FileId,snapshot,revision,intent.StrokeId);
    }
}
