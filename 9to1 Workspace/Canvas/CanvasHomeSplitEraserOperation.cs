using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;
namespace HavenOS.Apps.Canvas;

/// <summary>Exact native split data and assigned fragment identities, never serialized authority.</summary>
public sealed class CanvasSplitEraseIntent
{
    private readonly JsonElement _arguments;
    private CanvasSplitEraseIntent(HostedItemId fileId, CanvasFilesOpenResult opened, CanvasSplitErasePreview preview)
    {
        FileId = fileId; StoreId = opened.StoreId; FilesRevision = opened.CasRevisionId;
        ArtifactId = opened.Artifact.ArtifactId; ArtifactRevision = opened.Artifact.RevisionId;
        Preview = preview; OperationId = Guid.NewGuid();
        Scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", FileId.ToString(), FilesRevision.ToString(), ResourceAccess.Write) });
        _arguments = JsonSerializer.SerializeToElement(new
        {
            operation = "eraser.split-colliding", storeId = StoreId, fileId = FileId.Value,
            expectedFilesRevision = FilesRevision.Value, artifactId = ArtifactId, expectedArtifactRevision = ArtifactRevision,
            operationId = OperationId, samples = preview.Samples, width = preview.Width,
            nativeCandidateHash = preview.NativeHash, materializationReceiptHash = preview.ReceiptHash,
            fragmentIdentities = preview.FragmentIdentities.OrderBy(pair => pair.Key).Select(pair => new { nativeKey = pair.Key, strokeId = pair.Value }).ToArray(),
            removedStrokeIds = preview.RemovedStrokeIds, fullStrokeOrder = preview.StrokeOrder,
            samplesRole = "preserved-original-input-provenance", geometry = "authoritative-schema2-exact-line-quadratic-cubic"
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    public HostedItemId FileId { get; }
    public Guid StoreId { get; }
    public FilesRevisionId FilesRevision { get; }
    public Guid ArtifactId { get; }
    public Guid ArtifactRevision { get; }
    public Guid OperationId { get; }
    public CanvasSplitErasePreview Preview { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();
    public static CanvasSplitEraseIntent Capture(HostedItemId fileId, CanvasFilesOpenResult opened,
        IReadOnlyList<RnotePointerSample> samples, double width)
    {
        ArgumentNullException.ThrowIfNull(opened);
        if (fileId.Value == Guid.Empty || opened.StoreId == Guid.Empty || opened.CasRevisionId.Value == Guid.Empty)
            throw new ArgumentException("Split erasing requires the original committed Files store and item identity.");
        using var document = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(opened.Artifact));
        var preview = document.PreviewSplitErase(samples, width, opened.Artifact.RevisionId);
        if (preview.CopyReplacements().Length == 0 && preview.RemovedStrokeIds.IsEmpty)
            throw new InvalidOperationException("The genuine split eraser affects no supported editable ink.");
        return new(fileId, opened, preview);
    }
}

public sealed record CanvasSplitEraseCommit(HostedItemId FileId, CanvasArtifact Artifact, FilesRevision FilesRevision);

public sealed class CanvasHomeSplitEraserOperation(CanvasFilesArtifactBridge files, HomeResourceOperationBroker home,
    IAuthenticatedResourceActorSource actors)
{
    public async Task<CanvasSplitEraseCommit> ExecuteAsync(CanvasSplitEraseIntent intent, HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No current Home actor is available.");
        var opened = await files.OpenAsync(intent.FileId, intent.StoreId, cancellationToken).ConfigureAwait(false);
        if (opened.CasRevisionId != intent.FilesRevision || opened.Artifact.ArtifactId != intent.ArtifactId ||
            opened.Artifact.RevisionId != intent.ArtifactRevision)
            throw new InvalidOperationException("The original owning split target changed.");
        using var candidate = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(opened.Artifact));
        var fresh = candidate.PreviewSplitErase(intent.Preview.Samples, intent.Preview.Width, intent.ArtifactRevision, intent.Preview.FragmentIdentities);
        if (fresh.NativeHash != intent.Preview.NativeHash || fresh.ReceiptHash != intent.Preview.ReceiptHash ||
            !fresh.StrokeOrder.SequenceEqual(intent.Preview.StrokeOrder) || !fresh.RemovedStrokeIds.SequenceEqual(intent.Preview.RemovedStrokeIds))
            throw new InvalidOperationException("The genuine split result differs from its exact approved native receipt.");
        candidate.ApplySplitErase(fresh, new(intent.ArtifactRevision, intent.OperationId,
            new CanvasActorContext(actor.ActorId, actor.ActorId, "Home genuine split eraser")));
        var claimed = await home.ClaimExecutionAsync(capability, CanvasStrokeWriteIntent.TargetAppId, CanvasStrokeWriteIntent.ActionId,
            intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false);
        if (claimed is null || claimed != actor) throw new UnauthorizedAccessException("Home did not grant this exact owning split transaction.");
        var snapshot = candidate.Snapshot;
        var committed = await files.SaveOriginalAsync(intent.FileId, snapshot, intent.FilesRevision, intent.StoreId, claimed, capability, cancellationToken).ConfigureAwait(false);
        return new(intent.FileId, snapshot, committed);
    }
}
