using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Canvas;

public enum CanvasStrokeEditKind { Delete, Translate }

/// <summary>Exact captured edit data; authority and actor come only from Home.</summary>
public sealed class CanvasStrokeEditIntent
{
    private readonly JsonElement _arguments;
    private CanvasStrokeEditIntent(HostedItemId fileId, FilesRevisionId filesRevision, Guid artifactId,
        Guid artifactRevision, Guid operationId, Guid strokeId, CanvasStrokeEditKind kind, double deltaX, double deltaY)
    {
        FileId = fileId; ExpectedFilesRevision = filesRevision; ArtifactId = artifactId;
        ExpectedArtifactRevision = artifactRevision; OperationId = operationId; StrokeId = strokeId;
        Kind = kind; DeltaX = deltaX; DeltaY = deltaY;
        _arguments = JsonSerializer.SerializeToElement(new
        {
            operation = kind == CanvasStrokeEditKind.Delete ? "stroke.delete" : "stroke.translate",
            fileId = fileId.Value, expectedFilesRevision = filesRevision.Value,
            artifactId, expectedArtifactRevision = artifactRevision, operationId, strokeId, deltaX, deltaY
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", fileId.ToString(), filesRevision.ToString(), ResourceAccess.Write) });
    }
    public HostedItemId FileId { get; }
    public FilesRevisionId ExpectedFilesRevision { get; }
    public Guid ArtifactId { get; }
    public Guid ExpectedArtifactRevision { get; }
    public Guid OperationId { get; }
    public Guid StrokeId { get; }
    public CanvasStrokeEditKind Kind { get; }
    public double DeltaX { get; }
    public double DeltaY { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();

    public static CanvasStrokeEditIntent Capture(HostedItemId fileId, FilesRevisionId filesRevision, Guid artifactId,
        Guid artifactRevision, Guid operationId, Guid strokeId, CanvasStrokeEditKind kind, double deltaX = 0, double deltaY = 0)
    {
        if (fileId.Value == Guid.Empty || filesRevision.Value == Guid.Empty || artifactId == Guid.Empty ||
            artifactRevision == Guid.Empty || operationId == Guid.Empty || strokeId == Guid.Empty || !Enum.IsDefined(kind))
            throw new ArgumentException("Canvas edits require exact committed Files, artifact and stroke identities/revisions.");
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY) ||
            (kind == CanvasStrokeEditKind.Delete && (deltaX != 0 || deltaY != 0)))
            throw new ArgumentException("Canvas edit coordinates must be finite and apply only to translation.");
        return new(fileId, filesRevision, artifactId, artifactRevision, operationId, strokeId, kind, deltaX, deltaY);
    }
}

/// <summary>Private donor candidate, exact issuer-bound claim, then authenticated Files CAS.</summary>
public sealed class CanvasHomeStrokeEditOperation(CanvasFilesArtifactBridge files, HomeResourceOperationBroker home,
    IAuthenticatedResourceActorSource actors)
{
    public async Task<CanvasHomeStrokeCommit> ExecuteAsync(CanvasStrokeEditIntent intent, HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(capability);
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No current authenticated Home actor is available.");
        var opened = await files.OpenAsync(intent.FileId, cancellationToken).ConfigureAwait(false);
        if (opened.CasRevisionId != intent.ExpectedFilesRevision || opened.Artifact.ArtifactId != intent.ArtifactId ||
            opened.Artifact.RevisionId != intent.ExpectedArtifactRevision)
            throw new InvalidOperationException("The canonical Canvas target changed before edit preparation.");
        using var candidate = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(opened.Artifact));
        var request = new CanvasMutationRequest(intent.ExpectedArtifactRevision, intent.OperationId,
            new CanvasActorContext(actor.ActorId, actor.ActorId, "Home resource operation"));
        if (intent.Kind == CanvasStrokeEditKind.Delete) candidate.DeleteStroke(intent.StrokeId, request);
        else candidate.TranslateStroke(intent.StrokeId, intent.DeltaX, intent.DeltaY, request);
        var snapshot = candidate.Snapshot;
        var claimed = await home.ClaimExecutionAsync(capability, CanvasStrokeWriteIntent.TargetAppId, CanvasStrokeWriteIntent.ActionId,
            intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false);
        if (claimed is null || claimed != actor)
            throw new UnauthorizedAccessException("Home did not grant this exact current Canvas edit transaction.");
        var committed = await files.SaveAsync(intent.FileId, snapshot, intent.ExpectedFilesRevision, claimed, cancellationToken).ConfigureAwait(false);
        return new(intent.FileId, snapshot, committed, intent.StrokeId);
    }
}
