namespace Haven.Application;

/// <summary>Optional source-owned installed registry observation. Implementing this
/// interface grants no package/publisher/installation authority. The canonical
/// registry issuer must check its SAME constructor state/actor/provider references
/// and retain every actual child source and physical callback through settlement.
/// Consumers refuse when this owning source contract is absent.</summary>
public interface IInstalledApplicationOriginalScopedActorRegistry : IInstalledApplicationOriginalActorRegistry
{
    bool HasOriginalComposition(object originalStateStore, IAuthenticatedResourceActorSource originalActors,
        IReadOnlyList<IInstalledApplicationObservationProvider> originalProviders);
    ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshForActorWithinOriginalSourceAsync(
        AuthenticatedResourceActor expectedActor, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
    ValueTask<InstalledApplicationReference?> ResolveLaunchForActorWithinOriginalSourceAsync(
        Guid applicationId, long expectedRevision, AuthenticatedResourceActor expectedActor,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken cancellationToken);
}
