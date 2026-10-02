using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Canvas;

/// <summary>Captured target data only; the hit query conveys no access authority.</summary>
public sealed class CanvasQuickEraseIntent
{
    private readonly JsonElement _arguments;
    private CanvasQuickEraseIntent(HostedItemId fileId, CanvasFilesOpenResult opened, Guid target, double x, double y)
    {
        FileId = fileId; StoreId = opened.StoreId; FilesRevision = opened.CasRevisionId;
        ArtifactId = opened.Artifact.ArtifactId; ArtifactRevision = opened.Artifact.RevisionId;
        StrokeId = target; X = x; Y = y; OperationId = Guid.NewGuid();
        Scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", FileId.ToString(), FilesRevision.ToString(), ResourceAccess.Write) });
        _arguments = JsonSerializer.SerializeToElement(new { operation = "eraser.quick", storeId = StoreId, fileId = FileId.Value,
            expectedFilesRevision = FilesRevision.Value, artifactId = ArtifactId, expectedArtifactRevision = ArtifactRevision,
            operationId = OperationId, strokeId = StrokeId, x = X, y = Y });
    }
    public HostedItemId FileId { get; }
    public Guid StoreId { get; }
    public FilesRevisionId FilesRevision { get; }
    public Guid ArtifactId { get; }
    public Guid ArtifactRevision { get; }
    public Guid OperationId { get; }
    public Guid StrokeId { get; }
    public double X { get; }
    public double Y { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();
    public static CanvasQuickEraseIntent Capture(HostedItemId fileId, CanvasFilesOpenResult opened, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(opened);
        if (fileId.Value == Guid.Empty || opened.StoreId == Guid.Empty || opened.CasRevisionId.Value == Guid.Empty) throw new ArgumentException("Original Files store identity is required.", nameof(opened));
        using var document = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(opened.Artifact));
        var target = document.PreviewQuickErase(x, y, opened.Artifact.RevisionId)
            ?? throw new InvalidOperationException("Quick eraser did not hit an editable stroke.");
        return new(fileId, opened, target, x, y);
    }
}

/// <summary>Revalidates the genuine hit in a detached native owner, then uses shared keyed deletion/history and final Files CAS.</summary>
public sealed class CanvasHomeQuickEraserOperation(CanvasFilesArtifactBridge files, HomeResourceOperationBroker home,
    IAuthenticatedResourceActorSource actors)
{
    public async Task<CanvasHomeStrokeCommit> ExecuteAsync(CanvasQuickEraseIntent intent, HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No current Home actor is available.");
        var opened = await files.OpenAsync(intent.FileId, intent.StoreId, cancellationToken).ConfigureAwait(false);
        if (opened.CasRevisionId != intent.FilesRevision || opened.Artifact.ArtifactId != intent.ArtifactId ||
            opened.Artifact.RevisionId != intent.ArtifactRevision)
            throw new InvalidOperationException("The canonical Quick erase target changed.");
        using var candidate = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(opened.Artifact));
        if (candidate.PreviewQuickErase(intent.X, intent.Y, intent.ArtifactRevision) != intent.StrokeId)
            throw new InvalidOperationException("The genuine Quick erase hit changed.");
        candidate.DeleteStroke(intent.StrokeId, new(intent.ArtifactRevision, intent.OperationId,
            new CanvasActorContext(actor.ActorId, actor.ActorId, "Home Quick eraser")));
        var claimed = await home.ClaimExecutionAsync(capability, CanvasStrokeWriteIntent.TargetAppId, CanvasStrokeWriteIntent.ActionId,
            intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false);
        if (claimed is null || claimed != actor) throw new UnauthorizedAccessException("Home did not grant this exact Quick erase.");
        var snapshot = candidate.Snapshot;
        var committed = await files.SaveAsync(intent.FileId, snapshot, intent.FilesRevision, intent.StoreId, claimed, cancellationToken).ConfigureAwait(false);
        return new(intent.FileId, snapshot, committed, intent.StrokeId);
    }
}
