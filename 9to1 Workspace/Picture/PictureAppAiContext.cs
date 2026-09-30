using System.Text.Json;
using Haven.Application;
using NineToOne.Cui.AI;

namespace HavenOS.Images;

/// <summary>Trusted host target: backing editable artifact identity is separate from the linked source asset.</summary>
public sealed record PictureAiTarget(PictureDocument Document, Guid BackingFileId, Guid FilesRevisionId,
    string CurrentTool, string CompositionMode, bool HostAllowsWrites);

/// <summary>Shared Dulche context for the current supported editable raster graph, guarded by canonical Files authority.</summary>
public sealed class PictureAppAiContext(
    Func<CancellationToken, ValueTask<PictureAiTarget?>> currentTarget,
    ResourceAuthorizationService authorization) : IAppAiContext
{
    public async ValueTask<AppAiContextSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        var target = await currentTarget(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No canonical Files-backed Picture document is active.");
        if (target.BackingFileId == Guid.Empty || target.FilesRevisionId == Guid.Empty || target.Document.DocumentId == Guid.Empty)
            throw new UnauthorizedAccessException("Picture AI requires stable committed canonical target identities.");
        var scope = new ResourceScope("files.item", target.BackingFileId.ToString(), target.FilesRevisionId.ToString(), ResourceAccess.Read);
        var actor = await authorization.AuthorizeAsync("picture.file.open", [scope], cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Picture AI cannot inspect this editable Files artifact.");
        var document = target.Document;
        // This adapter exposes the supported non-destructive raster graph only.
        // Source content needs its owner's separate permission and hydration;
        // a legacy machine path is never model context or object identity.
        var semantic = JsonSerializer.SerializeToElement(new
        {
            document.DocumentId, document.Revision, document.DisplayName,
            document.CanvasWidth, document.CanvasHeight,
            SourceAsset = new { document.FileId, document.SourceRevision, ContentStatus = "Linked asset content requires separate source-owner permission" },
            Operations = document.Operations.ToArray(), target.CurrentTool, target.CompositionMode,
            CapabilityStatus = "Crop, quarter-turn rotation, flip and resize raster operation graph; element/mask/vector semantics are not provided by this adapter",
        });
        var current = await currentTarget(cancellationToken).ConfigureAwait(false);
        if (current is null || current.BackingFileId != target.BackingFileId || current.FilesRevisionId != target.FilesRevisionId ||
            current.Document.DocumentId != document.DocumentId || current.Document.Revision != document.Revision ||
            current.CurrentTool != target.CurrentTool || current.CompositionMode != target.CompositionMode ||
            current.HostAllowsWrites != target.HostAllowsWrites ||
            await authorization.AuthorizeAsync("picture.file.open", [scope], cancellationToken).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Picture target, revision or resource authority changed during capture.");
        return new("picture", "Picture.Workspace", document.DocumentId.ToString(),
            $"{document.DisplayName}: {document.CanvasWidth} × {document.CanvasHeight} editable raster canvas.", null,
            new Dictionary<string, JsonElement> { ["Picture"] = semantic }, AppAiDataSensitivity.UserContent,
            DateTimeOffset.UtcNow, document.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), [],
            target.HostAllowsWrites ? "Editable; AI mutations require separate Home-brokered typed actions" : "ReadOnly");
    }
}
