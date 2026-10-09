using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeInstalledApplicationRegistry
{
    /// <summary>Pure reference composition proof only. A matched registry tuple
    /// cannot prove that a package, publisher, installer or execution grant exists.</summary>
    public bool HasOriginalComposition(object originalStateStore, IAuthenticatedResourceActorSource originalActors,
        IReadOnlyList<IInstalledApplicationObservationProvider> originalProviders) =>
        ReferenceEquals(store, originalStateStore) && ReferenceEquals(actors, originalActors) &&
        actors is HomeLocalProfileIdentity profiles && profiles.IsBoundToStore(store) &&
        originalProviders.Count == _providers.Length && _providers.Select((provider, index) =>
            ReferenceEquals(provider, originalProviders[index])).All(same => same);

    public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshForActorWithinOriginalSourceAsync(
        AuthenticatedResourceActor expectedActor, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expectedActor); ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask); DemandOriginalScopedComposition();
        return RefreshCoreAsync(expectedActor, token, new(originalSynchronousScope, retainOriginalTask));
    }

    public async ValueTask<InstalledApplicationReference?> ResolveLaunchForActorWithinOriginalSourceAsync(
        Guid applicationId, long expectedRevision, AuthenticatedResourceActor expectedActor,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expectedActor); ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask); DemandOriginalScopedComposition();
        if (applicationId == Guid.Empty || expectedRevision < 1) return null;
        return (await RefreshCoreAsync(expectedActor, token, new(originalSynchronousScope, retainOriginalTask))
            .ConfigureAwait(false)).SingleOrDefault(actual => actual.ApplicationId == applicationId &&
                actual.Revision == expectedRevision && actual.Enabled && actual.ProfileAccessible);
    }

    private void DemandOriginalScopedComposition()
    {
        if (actors is not HomeLocalProfileIdentity profiles || !profiles.IsBoundToStore(store) ||
            _providers.Any(provider => provider is not IInstalledApplicationOriginalScopedObservationProvider))
            throw new InvalidOperationException("The original registry requires its SAME Home profile/store and actual source-scoped platform providers.");
    }

    private Task<AuthenticatedResourceActor?> ReadOriginalRegistryActorAsync(HomeOwnershipOriginalSourceCallbacks? source,
        CancellationToken token) => source is null ? actors.GetCurrentAsync(token).AsTask() :
        source.ReadAsync(() => ((HomeLocalProfileIdentity)actors).GetCurrentAsync(source.Run, source.Retain, token).AsTask());

    private string ReadOriginalRegistryProviderId(IInstalledApplicationObservationProvider provider,
        HomeOwnershipOriginalSourceCallbacks? source) => source is null ? provider.ProviderId : source.Invoke(() => provider.ProviderId);

    private async Task<IReadOnlyList<InstalledApplicationProfileObservation>> ReadOriginalRegistryProviderAsync(
        IInstalledApplicationObservationProvider provider, HomeOwnershipOriginalSourceCallbacks? source,
        CancellationToken token)
    {
        if (source is null) return await provider.ObserveAsync(token).ConfigureAwait(false);
        var observed = await source.ReadAsync(() => ((IInstalledApplicationOriginalScopedObservationProvider)provider)
            .ObserveWithinOriginalSourceAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        // A provider's IReadOnlyList can itself be an actual native/custom source.
        // Snapshot its bounded iteration under the SAME physical caller guard, then
        // let the unchanged reconciliation validate these managed detached values.
        return source.Invoke(() =>
        {
            if (observed is null) return null!;
            var profiles = observed.Take(257).ToArray();
            if (profiles.Length > 256) throw new InvalidDataException("Invalid platform profile observations.");
            var snapshot = new List<InstalledApplicationProfileObservation>(); var total = 0;
            foreach (var profile in profiles)
            {
                if (profile is null || profile.Applications is null) { snapshot.Add(profile!); continue; }
                var applications = profile.Applications.Take(Math.Min(10001, 100001 - total)).ToArray();
                total += applications.Length;
                if (applications.Length > 10000 || total > 100000)
                    throw new InvalidDataException("Installed application observation exceeds the registry bound.");
                snapshot.Add(profile with { Applications = applications });
            }
            return snapshot.ToArray();
        });
    }

    private Task<HomeStateReadResult> ReadOriginalRegistryStoreAsync(HomeOwnershipOriginalSourceCallbacks? source,
        CancellationToken token) => source is null ? store.ReadAsync(token) : source.ReadAsync(() => store.ReadAsync(token));

    private Task<HomeStateWriteResult> WriteOriginalRegistryStoreAsync(HomeOwnershipOriginalSourceCallbacks? source,
        HomeCoreStateRecord record, long expectedRevision, AuthenticatedResourceActor originalActor,
        IHomeStateCommitActorGuard originalGuard, CancellationToken token) => source is null
            ? store.WriteGuardedAsync(record, expectedRevision, originalActor, originalGuard, token)
            : source.ReadAsync(() => store.WriteGuardedAsync(record, expectedRevision, originalActor,
                new OriginalRegistryProfileGuard((HomeLocalProfileIdentity)actors, originalActor, source), token));

    private sealed class OriginalRegistryProfileGuard(HomeLocalProfileIdentity sameProfiles,
        AuthenticatedResourceActor originalActor, HomeOwnershipOriginalSourceCallbacks captured)
        : IHomeOriginalScopedStateCommitActorGuard
    {
        public ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor,
            HomeStateCommitPhase phase, CancellationToken token) =>
            CheckCoreAsync(lockedState, expectedActor, phase, captured.Run, captured.Retain, token);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor,
            HomeStateCommitPhase phase, Action<Action> originalSynchronousScope,
            Action<Task> retainOriginalTask, CancellationToken token)
        {
            var supplied = new HomeOwnershipOriginalSourceCallbacks(originalSynchronousScope, retainOriginalTask);
            return CheckCoreAsync(lockedState, expectedActor, phase,
                body => captured.Run(() => supplied.Run(body)),
                raw => { captured.Retain(raw); supplied.Retain(raw); }, token);
        }
        private async ValueTask<bool> CheckCoreAsync(HomeCoreStoredState lockedState,
            AuthenticatedResourceActor expectedActor, HomeStateCommitPhase phase, Action<Action> scope,
            Action<Task> retain, CancellationToken token)
        {
            if (expectedActor != originalActor) return false;
            Task<bool>? actual = null;
            scope(() =>
            {
                actual = sameProfiles.CheckAsync(lockedState, originalActor, phase, scope, retain, token).AsTask();
                retain(actual);
            });
            if (actual is null) throw new InvalidOperationException("The actual locked-state registry profile check returned no Task.");
            // Directly join this guard only. Sweeping captured children here would
            // attempt to join the encompassing SAME guarded Home publication.
            try { return await actual.ConfigureAwait(false); }
            catch when (actual.Exception is { InnerExceptions.Count: > 1 }) { throw actual.Exception!; }
        }
    }
}
