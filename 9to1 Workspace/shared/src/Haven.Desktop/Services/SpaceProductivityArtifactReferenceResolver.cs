using Haven.Application;
using HavenOS.Files;

namespace Haven.Desktop.Services;

/// <summary>Shared object metadata comes from the existing Space and Files owners, never a copied title or path.</summary>
public sealed class SpaceProductivityArtifactReferenceResolver(SpaceFilesArtifactActionRouter router)
    : IProductivityArtifactReferenceResolver
{
    public async Task<ResolvedProductivityArtifactReference> ResolveAsync(ProductivityArtifactReferenceSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Context);
        if (source.Context.HostedFileId is not { } fileId || fileId == Guid.Empty ||
            source.FilesRevisionId == Guid.Empty || string.IsNullOrWhiteSpace(source.Context.RevisionToken))
            throw new UnauthorizedAccessException("An exact backed canonical reference is required.");
        var target = await router.ResolveAsync(new(source.SpaceId, source.SpaceRevision, source.Context.ContextId,
            fileId, source.Context.CanonicalEntityId, new FilesRevisionId(source.FilesRevisionId), false), cancellationToken);
        if (target.Source != source.Context)
            throw new UnauthorizedAccessException("The shared pointer differs from the current canonical Space reference.");
        cancellationToken.ThrowIfCancellationRequested();
        return new(source, target.Artifact.DisplayName, target.Artifact.ArtifactType);
    }
}
