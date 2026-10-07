using System.Collections.Immutable;
using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;
namespace HavenOS.Apps.Canvas;

/// <summary>Exact genuine donor eraser gesture and canonical preview data; never persisted authority.</summary>
public sealed class CanvasEraserIntent
{
    private readonly JsonElement _arguments;
    private CanvasEraserIntent(HostedItemId fileId,CanvasFilesOpenResult current,
        ImmutableArray<RnotePointerSample> samples,double width,IReadOnlyList<Guid> strokeIds)
    {
        FileId=fileId; ExpectedStoreId=current.StoreId; ExpectedFilesRevision=current.CasRevisionId;
        ArtifactId=current.Artifact.ArtifactId; ExpectedArtifactRevision=current.Artifact.RevisionId;
        Samples=samples; Width=width; StrokeIds=Array.AsReadOnly(strokeIds.Order().ToArray()); OperationId=Guid.NewGuid();
        _arguments=JsonSerializer.SerializeToElement(new { operation="ink.erase-whole",fileId=fileId.Value,
            expectedStoreId=ExpectedStoreId,expectedFilesRevision=ExpectedFilesRevision.Value,
            artifactId=ArtifactId,expectedArtifactRevision=ExpectedArtifactRevision,operationId=OperationId,
            samples,width,strokeIds=StrokeIds },new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Scopes=Array.AsReadOnly(new[] { new ResourceScope("files.item",fileId.ToString(),ExpectedFilesRevision.ToString(),ResourceAccess.Write) });
    }
    public HostedItemId FileId { get; }
    public Guid ExpectedStoreId { get; }
    public FilesRevisionId ExpectedFilesRevision { get; }
    public Guid ArtifactId { get; }
    public Guid ExpectedArtifactRevision { get; }
    public Guid OperationId { get; }
    public ImmutableArray<RnotePointerSample> Samples { get; }
    public double Width { get; }
    public IReadOnlyList<Guid> StrokeIds { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments=>_arguments.Clone();
    public static CanvasEraserIntent Capture(HostedItemId fileId,CanvasFilesOpenResult current,
        IReadOnlyList<RnotePointerSample> samples,double width)
    {
        ArgumentNullException.ThrowIfNull(current);
        if(fileId.Value==Guid.Empty || current.StoreId==Guid.Empty || current.CasRevisionId.Value==Guid.Empty)
            throw new ArgumentException("Erasure requires exact original Files store and committed document identities.");
        var captured=RnoteCanvasEngine.CaptureSamples(samples);
        using var preview=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(current.Artifact));
        var ids=preview.PreviewWholeStrokeErase(captured,width,current.Artifact.RevisionId);
        if(ids.Count==0) throw new InvalidOperationException("The genuine eraser gesture affects no supported canonical ink.");
        return new(fileId,current,captured,width,ids);
    }
}

public sealed record CanvasHomeEraserCommit(HostedItemId FileId,CanvasArtifact Artifact,FilesRevision FilesRevision);

public sealed class CanvasHomeEraserOperation(CanvasFilesArtifactBridge files,HomeResourceOperationBroker home,
    IAuthenticatedResourceActorSource actors)
{
    public async Task<CanvasHomeEraserCommit> ExecuteAsync(CanvasEraserIntent intent,
        HomeResourceExecutionCapability capability,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        var actor=await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No current authenticated Home actor is available.");
        var opened=await files.OpenAsync(intent.FileId,intent.ExpectedStoreId,cancellationToken).ConfigureAwait(false);
        if(opened.StoreId!=intent.ExpectedStoreId || opened.CasRevisionId!=intent.ExpectedFilesRevision ||
            opened.Artifact.ArtifactId!=intent.ArtifactId || opened.Artifact.RevisionId!=intent.ExpectedArtifactRevision)
            throw new InvalidOperationException("The original Canvas eraser target changed.");
        using var candidate=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(opened.Artifact));
        candidate.EraseWholeStrokes(intent.Samples,intent.Width,intent.StrokeIds,
            new(intent.ExpectedArtifactRevision,intent.OperationId,new(actor.ActorId,actor.ActorId,"Home resource operation")));
        var snapshot=candidate.Snapshot;
        if(snapshot.SharedResources.Count!=0 || snapshot.Pages.Any(page=>page.Objects.Count!=0))
            throw new NotSupportedException("Erasure of referenced spatial content requires current owning source access validation.");
        var claimed=await home.ClaimExecutionAsync(capability,CanvasStrokeWriteIntent.TargetAppId,CanvasStrokeWriteIntent.ActionId,
            intent.Scopes,intent.Arguments,cancellationToken).ConfigureAwait(false);
        if(claimed is null || claimed!=actor) throw new UnauthorizedAccessException("Home did not approve this exact current eraser gesture.");
        var committed=await files.SaveOriginalAsync(intent.FileId,snapshot,intent.ExpectedFilesRevision,intent.ExpectedStoreId,claimed,capability,cancellationToken).ConfigureAwait(false);
        return new(intent.FileId,snapshot,committed);
    }
}
