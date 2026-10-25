namespace Haven.Application;

/// <summary>
/// The source owner's native revision without converting between its revision model and another
/// application's. A sequence and version ID are mutually exclusive; this value is descriptive
/// identity only and grants no access to the artifact.
/// </summary>
public sealed record BoardEmbeddedArtifactRevision(long? Sequence, Guid? VersionId)
{
    public bool IsValid => (Sequence is >= 0 && VersionId is null) ||
        (Sequence is null && VersionId is { } version && version != Guid.Empty);
}

/// <summary>
/// Stable cross-app identity for a Board page that embeds an artifact owned by another app.
/// The owner must resolve this identity and verify the pinned revision and current permission
/// before publishing any embedded surface. Copying this reference never creates an access grant.
/// </summary>
public sealed record BoardEmbeddedArtifactReference(string ApplicationId, Guid ArtifactId,
    BoardEmbeddedArtifactRevision ExpectedRevision)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApplicationId) || ApplicationId.Length > 64 ||
            ApplicationId != ApplicationId.ToLowerInvariant() ||
            ApplicationId.Any(character => !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')) ||
            ArtifactId == Guid.Empty || ExpectedRevision is null || !ExpectedRevision.IsValid)
            throw new InvalidDataException("A Board embedded artifact requires a canonical app, artifact and owner revision identity.");
    }
}
