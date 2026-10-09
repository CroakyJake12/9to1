using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeLocalStoreOwnership
{
    /// <summary>The SAME new-empty binding protocol with actual nested source custody.
    /// Scope callbacks and observed IDs grant no ownership; current actor/evidence/guarded CAS remain required.</summary>
    public Task<HomeLocalStoreBinding> BindNewEmptyWithinOriginalSourceAsync(AuthenticatedResourceActor expectedActor,
        string resourceKind, string storeId, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask);
        return BindNewEmptyCoreAsync(expectedActor, resourceKind, storeId, token,
            new(originalSynchronousScope, retainOriginalTask));
    }

    private Task<AuthenticatedResourceActor?> ReadOriginalBindingActorAsync(HomeOwnershipOriginalSourceCallbacks? source,
        CancellationToken token) => source is null ? profiles.GetCurrentAsync(token).AsTask() :
        source.ReadAsync(() => profiles.GetCurrentAsync(source.Run, source.Retain, token).AsTask());

    private Task<HomeLocalStoreEvidence?> ReadOriginalBindingEvidenceAsync(HomeOwnershipOriginalSourceCallbacks? source,
        string kind, string storeId, CancellationToken token)
    {
        if (source is null) return evidence.ReadAsync(kind, storeId, token).AsTask();
        if (evidence is not IHomeOriginalScopedLocalStoreEvidenceSource actual)
            throw new InvalidOperationException("HOME_OWNERSHIP_SCOPE_REQUIRED: new-empty binding requires its actual scoped evidence owner.");
        return source.ReadAsync(() => actual.ReadWithinOriginalSourceAsync(kind, storeId, source.Run, source.Retain, token).AsTask());
    }

    /// <summary>Issuer-owned guard delegates to the SAME Home profiles under the SAME locked state.
    /// It never reacquires the Home writer lease or certifies resource access by itself.</summary>
    private sealed class OriginalBindingProfileGuard(HomeLocalProfileIdentity sameProfiles,
        AuthenticatedResourceActor originalActor, HomeOwnershipOriginalSourceCallbacks capturedSource)
        : IHomeOriginalScopedStateCommitActorGuard
    {
        public ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor,
            HomeStateCommitPhase phase, CancellationToken token) =>
            CheckCoreAsync(lockedState, expectedActor, phase, capturedSource.Run, capturedSource.Retain, token);

        public ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor,
            HomeStateCommitPhase phase, Action<Action> originalSynchronousScope,
            Action<Task> retainOriginalTask, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
            var suppliedSource = new HomeOwnershipOriginalSourceCallbacks(originalSynchronousScope, retainOriginalTask);
            return CheckCoreAsync(lockedState, expectedActor, phase,
                body => capturedSource.Run(() => suppliedSource.Run(body)),
                actual => { capturedSource.Retain(actual); suppliedSource.Retain(actual); }, token);
        }

        private async ValueTask<bool> CheckCoreAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor,
            HomeStateCommitPhase phase, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            if (expectedActor != originalActor) return false;
            Task<bool>? actual = null;
            scope(() =>
            {
                actual = sameProfiles.CheckAsync(lockedState, originalActor, phase, scope, retain, token).AsTask();
                retain(actual);
            });
            if (actual is null) throw new InvalidOperationException("The actual locked-state profile check returned no Task.");
            // Do not sweep the encompassing WriteGuarded task here: it owns this guard callback.
            // The binding's outer original joins every retained raw child after that SAME write returns.
            try { return await actual.ConfigureAwait(false); }
            catch when (actual.Exception is { InnerExceptions.Count: > 1 }) { throw actual.Exception!; }
        }
    }
}
