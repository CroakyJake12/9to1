using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Optional trusted local composition. This lease attests original Home state only;
/// the actual SQL owner still exclusively validates its private admission, factory, UUID, revisions and receipt.
/// No graph approval is reused as a definition/pair grant. No remote/distributed transaction is provided.</summary>
public sealed class HomeGraphSqlAssociationLeaseSource
{
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly HomeResourceOperationBroker _broker;
    private readonly HomeGraphPublicationOwner _graphs;
    public HomeGraphSqlAssociationLeaseSource(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, HomeResourceOperationBroker broker, HomeGraphPublicationOwner graphs)
    {
        if (!profiles.IsBoundToStore(store) || !ownership.IsBoundTo(store, profiles) ||
            !graphs.IsBoundToAssociationComposition(store, profiles, ownership, broker))
            throw new UnauthorizedAccessException("Same actual trusted local Home composition required.");
        _store = store; _profiles = profiles; _ownership = ownership; _broker = broker; _graphs = graphs;
    }
    /// <summary>Caller must acquire its actual SQL transaction FIRST and perform ordinary SQL/actor/owner checks
    /// BEFORE this lease. While held, invoke only IsCurrentAsync and the synchronous/awaited actual SQL commit;
    /// never call Home ReadAsync, permission/resource resolvers, actor GetCurrentAsync or legacy SQL admission checks.
    /// Dispose deterministically after commit/refusal, BEFORE Home audit. Absent source means unavailable.</summary>
    public async ValueTask<IHomeLocalOperationLease?> AcquireAsync(HomeGraphPublicationObservation graphObservation,
        IReadOnlyList<HomeClaimedResourceAttestation> originalDefinitionAdmissions, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphObservation); ArgumentNullException.ThrowIfNull(originalDefinitionAdmissions);
        // Only this first dedicated one/two-definition association is supported. Bound actual enumeration,
        // not declared Count, BEFORE any ownership read or local lease acquisition.
        var admissions = new List<HomeClaimedResourceAttestation>();
        foreach (var item in originalDefinitionAdmissions)
        {
            if (admissions.Count == 2 || item is null) return null;
            admissions.Add(item);
        }
        if (admissions.Count == 0 || admissions.Select(item => item.Capability).Distinct().Count() != admissions.Count) return null;
        var actor = graphObservation.Actor;
        var owner = graphObservation.Receipt.Owner;
        if (owner.AppId != "automations" || owner.EntityKind != "automation.reusable-task" ||
            admissions.Any(item => item.Actor != actor || item.Submission.Scope.TargetAppId != owner.AppId ||
                graphObservation.Review.IsPublicationCapability(item.Capability))) return null;
        ct.ThrowIfCancellationRequested();
        if (await _profiles.GetCurrentAsync(ct).ConfigureAwait(false) != actor) return null;
        // Capture actual canonical receipt outside the Home gate. JSON/caller receipt fields are never accepted.
        var ownership = await _ownership.GetVerifiedAsync("automations", owner.StoreId.ToString("D"), ct).ConfigureAwait(false);
        if (ownership is null || ownership.ProfileId != actor.ProfileId ||
            await _profiles.GetCurrentAsync(ct).ConfigureAwait(false) != actor) return null;
        var storePrefix = owner.StoreId.ToString("D") + "/";
        if (admissions.Any(item => item.Submission.Impact.ResourceBinding is not { } binding ||
            !binding.Scopes.Any(scope => (scope.Kind == "automation.reusable-task" || scope.Kind == "automation.definition") &&
                scope.Id.StartsWith(storePrefix, StringComparison.Ordinal) && scope.Access == ResourceAccess.Write))) return null;
        var guard = new AssociationGuard(this, graphObservation, admissions.ToArray(), ownership);
        return await _store.AcquireLocalOperationLeaseCoreAsync(_profiles, actor, guard, ct).ConfigureAwait(false);
    }
    private sealed class AssociationGuard(HomeGraphSqlAssociationLeaseSource issuer,
        HomeGraphPublicationObservation graph, HomeClaimedResourceAttestation[] admissions,
        VerifiedResourceStoreOwnership ownership) : IHomeStateCommitActorGuard
    {
        public ValueTask<bool> CheckAsync(HomeCoreStoredState actualState, AuthenticatedResourceActor originalActor,
            HomeStateCommitPhase phase, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // All checks inspect the SAME locked genuine Home state; no actor/resources/permissions/SQL callbacks.
            return ValueTask.FromResult(graph.Actor == originalActor &&
                HomeLocalStoreOwnership.IsReceiptCurrentInState(actualState, ownership) &&
                issuer._graphs.IsObservationCurrentInState(graph, originalActor, actualState) &&
                admissions.All(item => issuer._broker.IsClaimedAttestationCurrentInState(item, originalActor, actualState)));
        }
    }
}
