using System.Collections.Immutable;
using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Canvas;

/// <summary>Captured operation data. Actor identity and authority are never accepted from these arguments.</summary>
public sealed class CanvasStrokeWriteIntent
{
    private readonly ImmutableArray<RnotePointerSample> _samples;
    private readonly JsonElement _arguments;
    private CanvasStrokeWriteIntent(HostedItemId fileId, FilesRevisionId expectedFilesRevision, Guid artifactId,
        Guid expectedArtifactRevision, Guid operationId, ImmutableArray<RnotePointerSample> samples, CanvasRnoteInkStyle style,Guid expectedStoreId = default)
    {
        ExpectedStoreId=expectedStoreId;FileId = fileId; ExpectedFilesRevision = expectedFilesRevision; ArtifactId = artifactId;
        ExpectedArtifactRevision = expectedArtifactRevision; OperationId = operationId; _samples = samples; Style = style;
        _arguments = JsonSerializer.SerializeToElement(new
        {
            operation = "stroke.draw", expectedStoreId, fileId = fileId.Value, expectedFilesRevision = expectedFilesRevision.Value,
            artifactId, expectedArtifactRevision, operationId, samples, style
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", fileId.ToString(), expectedFilesRevision.ToString(), ResourceAccess.Write) });
    }
    public const string TargetAppId = "canvas";
    public const string ActionId = "canvas.file.save";
    public Guid ExpectedStoreId {get;}
    public HostedItemId FileId { get; }
    public FilesRevisionId ExpectedFilesRevision { get; }
    public Guid ArtifactId { get; }
    public Guid ExpectedArtifactRevision { get; }
    public Guid OperationId { get; }
    public CanvasRnoteInkStyle Style { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();
    internal IReadOnlyList<RnotePointerSample> Samples => _samples;

    /// <summary>Explicit original-store-bound successor. Never infers the store from the current save provider.</summary>
    public static CanvasStrokeWriteIntent Capture(HostedItemId fileId,FilesRevisionId expectedFilesRevision,Guid artifactId,
        Guid expectedArtifactRevision,Guid operationId,Guid expectedStoreId,IReadOnlyList<RnotePointerSample> samples,CanvasRnoteInkStyle? style=null)
    {
        if(expectedStoreId==Guid.Empty)throw new ArgumentException("The original Files store identity is required.",nameof(expectedStoreId));
        var detached=Capture(fileId,expectedFilesRevision,artifactId,expectedArtifactRevision,operationId,samples,style);
        return new(fileId,expectedFilesRevision,artifactId,expectedArtifactRevision,operationId,detached._samples,detached.Style,expectedStoreId);
    }
    public static CanvasStrokeWriteIntent Capture(HostedItemId fileId, FilesRevisionId expectedFilesRevision, Guid artifactId,
        Guid expectedArtifactRevision, Guid operationId, IReadOnlyList<RnotePointerSample> samples, CanvasRnoteInkStyle? style = null)
    {
        if (fileId.Value == Guid.Empty || expectedFilesRevision.Value == Guid.Empty || artifactId == Guid.Empty ||
            expectedArtifactRevision == Guid.Empty || operationId == Guid.Empty)
            throw new ArgumentException("Canvas writes require exact committed Files and artifact identities/revisions.");
        var captured = RnoteCanvasEngine.CaptureSamples(samples);
        var resolvedStyle = style ?? CanvasRnoteInkStyle.Default;
        resolvedStyle.BrushSnapshot(); // validate the actual native-supported immutable style
        return new(fileId, expectedFilesRevision, artifactId, expectedArtifactRevision, operationId, captured, resolvedStyle);
    }
}

public sealed record CanvasHomeStrokeCommit(HostedItemId FileId, CanvasArtifact Artifact, FilesRevision FilesRevision, Guid StrokeId);

/// <summary>
/// Owning native draw transaction: prepare a private candidate, claim the canonical issuer-bound Home handle,
/// then publish through Files CAS. No active document is mutated before the durable commit acknowledgement.
/// Callers must treat a thrown save/IO error as requiring recovery; this port never fabricates a successful result.
/// </summary>
public sealed class CanvasHomeStrokeOperation(CanvasFilesArtifactBridge files, HomeResourceOperationBroker home,
    IAuthenticatedResourceActorSource actors)
{
    public async Task<CanvasHomeStrokeCommit> ExecuteAsync(CanvasStrokeWriteIntent intent, HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(capability);
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No current authenticated Home actor is available.");
        var opened = intent.ExpectedStoreId==Guid.Empty
            ? await files.OpenAsync(intent.FileId,cancellationToken).ConfigureAwait(false)
            : await files.OpenAsync(intent.FileId,intent.ExpectedStoreId,cancellationToken).ConfigureAwait(false);
        if (opened.CasRevisionId != intent.ExpectedFilesRevision || opened.Artifact.ArtifactId != intent.ArtifactId ||
            opened.Artifact.RevisionId != intent.ExpectedArtifactRevision)
            throw new InvalidOperationException("The canonical Canvas target changed before draw preparation.");
        using var candidate = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(opened.Artifact));
        var stroke = candidate.DrawStroke(intent.Samples, new CanvasMutationRequest(intent.ExpectedArtifactRevision, intent.OperationId,
            new CanvasActorContext(actor.ActorId, actor.ActorId, "Home resource operation")), intent.Style);
        var snapshot = candidate.Snapshot;
        var claimed = await home.ClaimExecutionAsync(capability, CanvasStrokeWriteIntent.TargetAppId, CanvasStrokeWriteIntent.ActionId,
            intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false);
        if (claimed is null || claimed != actor)
            throw new UnauthorizedAccessException("Home did not grant this exact current Canvas write transaction.");
        // The bridge rechecks live actor/ACL/read-only state and metadata CAS
        // before and after immutable candidate IO. Only this acknowledged
        // revision can replace the host's active document.
        var committed = intent.ExpectedStoreId==Guid.Empty
            ? await files.SaveAsync(intent.FileId,snapshot,intent.ExpectedFilesRevision,claimed,cancellationToken).ConfigureAwait(false)
            : await files.SaveAsync(intent.FileId,snapshot,intent.ExpectedFilesRevision,intent.ExpectedStoreId,claimed,cancellationToken).ConfigureAwait(false);
        return new(intent.FileId, snapshot, committed, stroke);
    }
}
