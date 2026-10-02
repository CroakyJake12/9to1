using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace HavenOS.Images;

public sealed class PictureHistoryIntent
{
    private readonly JsonElement _arguments;
    private PictureHistoryIntent(PictureFilesOpenResult current, bool undo)
    {
        FileId = new(current.Artifact.BackingFileId);
        ExpectedStoreId = current.StoreId;
        ExpectedFilesRevision = current.CasRevisionId;
        DocumentId = current.Artifact.Document.DocumentId;
        ExpectedDocumentRevision = current.Artifact.Document.Revision;
        Undo = undo;
        _arguments = JsonSerializer.SerializeToElement(new
        {
            fileId = FileId.Value, expectedStoreId = ExpectedStoreId,
            expectedFilesRevision = ExpectedFilesRevision.Value, documentId = DocumentId,
            expectedDocumentRevision = ExpectedDocumentRevision, undo
        });
        Scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", FileId.ToString(), ExpectedFilesRevision.ToString(), ResourceAccess.Write) });
    }
    public const string ActionId = "picture.file.save";
    public HostedItemId FileId { get; }
    public Guid ExpectedStoreId { get; }
    public FilesRevisionId ExpectedFilesRevision { get; }
    public Guid DocumentId { get; }
    public long ExpectedDocumentRevision { get; }
    public bool Undo { get; }
    public JsonElement Arguments => _arguments.Clone();
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public static PictureHistoryIntent Capture(PictureFilesOpenResult current, bool undo)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.StoreId == Guid.Empty || current.CasRevisionId.Value == Guid.Empty ||
            current.Artifact.BackingFileId == Guid.Empty || current.Artifact.Document.DocumentId == Guid.Empty)
            throw new ArgumentException("History requires a committed Picture in its original Files store.");
        PictureArtifactHistory.Restore(current.Artifact, undo); // Validate retained data before requesting approval.
        return new(current, undo);
    }
}

public sealed class PictureHomeHistoryOperation(PictureFilesArtifactBridge files, HomeResourceOperationBroker home,
    IAuthenticatedResourceActorSource actors)
{
    public async Task<PictureFilesOpenResult> ExecuteAsync(PictureHistoryIntent intent,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(capability);
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No current verified Home profile is available.");
        var current = await files.OpenAsync(intent.FileId, intent.ExpectedStoreId, cancellationToken).ConfigureAwait(false);
        if (current.StoreId != intent.ExpectedStoreId || current.CasRevisionId != intent.ExpectedFilesRevision ||
            current.Artifact.Document.DocumentId != intent.DocumentId || current.Artifact.Document.Revision != intent.ExpectedDocumentRevision)
            throw new InvalidOperationException("The original Picture store or document changed before history restoration.");
        var candidate = PictureArtifactHistory.Restore(current.Artifact, intent.Undo);
        var claimed = await home.ClaimExecutionAsync(capability, "picture", PictureHistoryIntent.ActionId,
            intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false);
        if (claimed is null || claimed != actor)
            throw new UnauthorizedAccessException("Home did not approve this exact history restoration for the current profile.");
        var committed = await files.SaveAsync(candidate, current.CasRevisionId, intent.ExpectedStoreId, claimed, cancellationToken).ConfigureAwait(false);
        return new(candidate, committed, committed.Id) { StoreId = intent.ExpectedStoreId };
    }
}
