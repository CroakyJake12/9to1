using HavenOS.Files;

namespace HavenOS.Apps.Canvas;

/// <summary>The current, permission-checked Files row resolved for a stable Canvas artifact
/// reference. The file/store/CAS identities are retained only inside the owning Canvas session.</summary>
public sealed record CanvasEmbeddedArtifactOpenResult(HostedItemId FileId, CanvasFilesOpenResult Opened);

public sealed partial class CanvasFilesArtifactBridge
{
    /// <summary>Resolve one stable Canvas owner identity through the canonical Files index, then
    /// open only that exact item through the ordinary current Home/Files read path. This performs
    /// metadata lookup before the selected payload read; it never scans or reads unrelated bodies.</summary>
    public async Task<CanvasEmbeddedArtifactOpenResult> OpenEmbeddedArtifactAsync(Guid artifactId,
        Guid expectedArtifactRevisionId, CancellationToken cancellationToken = default)
    {
        if (artifactId == Guid.Empty) throw new ArgumentException("A canonical Canvas artifact ID is required.", nameof(artifactId));
        if (expectedArtifactRevisionId == Guid.Empty)
            throw new ArgumentException("The expected Canvas artifact revision is required.", nameof(expectedArtifactRevisionId));

        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No verified Home actor is active.");
        var provider = providers(actor)
            ?? throw new UnauthorizedAccessException("No canonical Files provider is bound to the Home actor.");
        var storeId = (await provider.GetStoreEvidenceAsync(cancellationToken).ConfigureAwait(false)).StoreId;
        if (storeId == Guid.Empty) throw new InvalidDataException("The current Files store identity is unavailable.");

        // Canonical Canvas references are registered with the owning-app identity in N form.
        // Files rejects duplicate owner identities as an explicit recovery error.
        var reference = await provider.GetArtifactByOwnerIdentityAsync(OwnerAppId, artifactId.ToString("N"), cancellationToken)
            .ConfigureAwait(false);
        if (!reference.IsSuccess || reference.Value is not { } canonical || canonical.OwnerAppId != OwnerAppId ||
            canonical.ArtifactType != nameof(FilesArtifactType.Canvas) || canonical.FileId.Value == Guid.Empty ||
            !Guid.TryParse(canonical.ArtifactId, out var registeredId) || registeredId != artifactId)
            throw new InvalidDataException(reference.Error?.Message ?? "The canonical Canvas artifact identity is unavailable.");

        // The normal bridge path performs Home read authorization, verifies the current
        // Files item/revision and payload hash, and binds the exact store/provider.
        var opened = await OpenAsync(canonical.FileId, storeId, cancellationToken).ConfigureAwait(false);
        if (opened.StoreId != storeId || opened.Artifact.ArtifactId != artifactId ||
            opened.Artifact.RevisionId != expectedArtifactRevisionId)
            throw new InvalidOperationException("The embedded Canvas reference is stale; reopen its current revision.");
        if (await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor ||
            !ReferenceEquals(providers(actor), provider) ||
            (await provider.GetStoreEvidenceAsync(storeId, cancellationToken).ConfigureAwait(false)).StoreId != storeId)
            throw new UnauthorizedAccessException("The Home actor or Files store changed during embedded Canvas open.");

        return new(canonical.FileId, opened);
    }
}
