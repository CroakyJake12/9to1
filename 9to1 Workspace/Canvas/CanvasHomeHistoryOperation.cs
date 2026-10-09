using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Canvas;

public enum CanvasHistoryKind { Undo, Redo }

/// <summary>Captured history request data. Persisted snapshots carry no authority.</summary>
public sealed class CanvasHistoryIntent
{
    private readonly JsonElement _arguments;
    private CanvasHistoryIntent(HostedItemId fileId, FilesRevisionId filesRevision, Guid artifactId,
        Guid artifactRevision, Guid operationId, CanvasHistoryKind kind, Guid expectedStoreId)
    {
        FileId = fileId; ExpectedFilesRevision = filesRevision; ArtifactId = artifactId; ExpectedStoreId = expectedStoreId;
        ExpectedArtifactRevision = artifactRevision; OperationId = operationId; Kind = kind;
        _arguments = JsonSerializer.SerializeToElement(new
        {
            operation = kind == CanvasHistoryKind.Undo ? "history.undo" : "history.redo",
            fileId = fileId.Value, expectedStoreId, expectedFilesRevision = filesRevision.Value,
            artifactId, expectedArtifactRevision = artifactRevision, operationId
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", fileId.ToString(), filesRevision.ToString(), ResourceAccess.Write) });
    }
    public HostedItemId FileId { get; }
    public Guid ExpectedStoreId { get; }
    public FilesRevisionId ExpectedFilesRevision { get; }
    public Guid ArtifactId { get; }
    public Guid ExpectedArtifactRevision { get; }
    public Guid OperationId { get; }
    public CanvasHistoryKind Kind { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();

    public static CanvasHistoryIntent Capture(HostedItemId fileId, FilesRevisionId filesRevision, Guid artifactId,
        Guid artifactRevision, Guid operationId, CanvasHistoryKind kind, Guid expectedStoreId)
    {
        if (expectedStoreId == Guid.Empty || fileId.Value == Guid.Empty || filesRevision.Value == Guid.Empty || artifactId == Guid.Empty ||
            artifactRevision == Guid.Empty || operationId == Guid.Empty || !Enum.IsDefined(kind))
            throw new ArgumentException("Canvas history requires exact committed Files and artifact identities/revisions.");
        return new(fileId, filesRevision, artifactId, artifactRevision, operationId, kind, expectedStoreId);
    }
}

public sealed record CanvasHomeHistoryCommit(HostedItemId FileId, CanvasArtifact Artifact, FilesRevision FilesRevision);

/// <summary>Restore the canonical donor candidate, then publish under a fresh exact Home claim and Files CAS.</summary>
public sealed class CanvasHomeHistoryOperation(CanvasFilesArtifactBridge files, HomeResourceOperationBroker home,
    IAuthenticatedResourceActorSource actors)
{
    public async Task<CanvasHomeHistoryCommit> ExecuteAsync(CanvasHistoryIntent intent, HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(capability);
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No current authenticated Home actor is available.");
        var opened = await files.OpenAsync(intent.FileId, intent.ExpectedStoreId, cancellationToken).ConfigureAwait(false);
        if (opened.StoreId != intent.ExpectedStoreId || opened.CasRevisionId != intent.ExpectedFilesRevision || opened.Artifact.ArtifactId != intent.ArtifactId ||
            opened.Artifact.RevisionId != intent.ExpectedArtifactRevision)
            throw new InvalidOperationException("The canonical Canvas target changed before history preparation.");
        using var candidate = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(opened.Artifact));
        var request = new CanvasMutationRequest(intent.ExpectedArtifactRevision, intent.OperationId,
            new CanvasActorContext(actor.ActorId, actor.ActorId, "Home resource operation"));
        if (intent.Kind == CanvasHistoryKind.Undo) candidate.Undo(request); else candidate.Redo(request);
        var snapshot = candidate.Snapshot;
        if (snapshot.SharedResources.Count != 0 || snapshot.Pages.Any(page => page.Objects.Count != 0))
            throw new NotSupportedException("History containing spatial objects or shared resources requires their current owning content and access checks before restoration.");
        var claimed = await home.ClaimExecutionAsync(capability, CanvasStrokeWriteIntent.TargetAppId, CanvasStrokeWriteIntent.ActionId,
            intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false);
        if (claimed is null || claimed != actor)
            throw new UnauthorizedAccessException("Home did not grant this exact current Canvas history transaction.");
        var committed = await files.SaveOriginalAsync(intent.FileId, snapshot, intent.ExpectedFilesRevision, intent.ExpectedStoreId, claimed, capability, cancellationToken).ConfigureAwait(false);
        return new(intent.FileId, snapshot, committed);
    }
}
