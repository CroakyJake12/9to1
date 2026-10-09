namespace Haven.Application;

/// <summary>Optional owning original-session registry admission. Actor metadata is not authority.
/// Consumers must deny when this port is absent; no current-actor fallback.</summary>
public interface IInstalledApplicationOriginalActorRegistry
{
    ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshForActorAsync(AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken);
    ValueTask<InstalledApplicationReference?> ResolveLaunchForActorAsync(Guid applicationId, long expectedRevision,
        AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken);
}
