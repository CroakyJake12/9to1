namespace Haven.Application;

/// <summary>An existing Space context reference and its exact backing Files revision; neither is a grant.</summary>
public sealed record ProductivityArtifactReferenceSource(Guid SpaceId, long SpaceRevision,
    SpaceContextReference Context, Guid FilesRevisionId);

public sealed record ResolvedProductivityArtifactReference(ProductivityArtifactReferenceSource Source,
    string DisplayName, string ArtifactType);

/// <summary>Resolves both canonical owners and current resource permissions before exposing display metadata.</summary>
public interface IProductivityArtifactReferenceResolver
{
    Task<ResolvedProductivityArtifactReference> ResolveAsync(ProductivityArtifactReferenceSource source,
        CancellationToken cancellationToken = default);
}
