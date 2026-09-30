using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace HavenOS.Images;

/// <summary>One exact non-destructive edit on a committed Picture revision. No actor, path or source replacement is accepted.</summary>
public sealed class PictureEditIntent
{
    private readonly JsonElement _arguments;
    private PictureEditIntent(HostedItemId fileId, FilesRevisionId filesRevision, Guid documentId,
        long documentRevision, PictureOperation operation)
    {
        FileId = fileId; ExpectedFilesRevision = filesRevision; DocumentId = documentId;
        ExpectedDocumentRevision = documentRevision; Operation = operation;
        _arguments = JsonSerializer.SerializeToElement(new
        {
            fileId = fileId.Value, expectedFilesRevision = filesRevision.Value,
            documentId, expectedDocumentRevision = documentRevision, operation
        });
        Scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", fileId.ToString(), filesRevision.ToString(), ResourceAccess.Write) });
    }
    public const string ActionId = "picture.file.save";
    public HostedItemId FileId { get; }
    public FilesRevisionId ExpectedFilesRevision { get; }
    public Guid DocumentId { get; }
    public long ExpectedDocumentRevision { get; }
    public PictureOperation Operation { get; }
    public JsonElement Arguments => _arguments.Clone();
    public IReadOnlyList<ResourceScope> Scopes { get; }

    public static PictureEditIntent Capture(PictureFilesOpenResult current, PictureOperation operation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(operation);
        var document = current.Artifact.Document;
        if (current.Artifact.BackingFileId == Guid.Empty || current.CasRevisionId.Value == Guid.Empty || document.DocumentId == Guid.Empty)
            throw new ArgumentException("Editing requires a committed canonical Picture document.");
        Apply(document, operation); // Validate geometry before creating any pending request.
        return new(new(current.Artifact.BackingFileId), current.CasRevisionId, document.DocumentId, document.Revision, operation);
    }

    internal static PictureDocument Apply(PictureDocument document, PictureOperation operation) => operation switch
    {
        CropOperation crop => document.Crop(crop.X, crop.Y, crop.Width, crop.Height),
        RotateOperation rotate when rotate.ClockwiseQuarterTurns is >= 1 and <= 3 => document.Rotate(rotate.ClockwiseQuarterTurns),
        FlipOperation flip => document.Flip(flip.Horizontal),
        ResizeOperation resize => document.Resize(resize.Width, resize.Height),
        _ => throw new NotSupportedException("This Picture edit has no owning implementation.")
    };
}

/// <summary>The same owning edit transaction is used by native Picture surfaces and shared callers.
/// Only an acknowledged Files commit may replace the host's currently displayed document.</summary>
public sealed class PictureHomeEditOperation(PictureFilesArtifactBridge files, HomeResourceOperationBroker home,
    IAuthenticatedResourceActorSource actors)
{
    public async Task<PictureFilesOpenResult> ExecuteAsync(PictureEditIntent intent, HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(capability);
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No current verified Home profile is available.");
        var current = await files.OpenAsync(intent.FileId, cancellationToken).ConfigureAwait(false);
        if (current.CasRevisionId != intent.ExpectedFilesRevision || current.Artifact.Document.DocumentId != intent.DocumentId ||
            current.Artifact.Document.Revision != intent.ExpectedDocumentRevision)
            throw new InvalidOperationException("The Picture document changed before this edit could be applied.");
        var candidate = current.Artifact with { Document = PictureEditIntent.Apply(current.Artifact.Document, intent.Operation) };
        var claimed = await home.ClaimExecutionAsync(capability, "picture", PictureEditIntent.ActionId,
            intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false);
        if (claimed is null || claimed != actor)
            throw new UnauthorizedAccessException("Home did not approve this exact Picture edit for the current profile.");
        var committed = await files.SaveAsync(candidate, current.CasRevisionId, claimed, cancellationToken).ConfigureAwait(false);
        return new(candidate, committed, committed.Id);
    }
}
