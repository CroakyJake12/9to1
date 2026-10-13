using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Application.NodeGraph;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public sealed partial class HomeSpaceGraphOwner
{
    public sealed partial class Operation
    {
        private readonly List<Exception> _retainedAssociationFailures = new();
        private Task<SpaceDefinition>? _associationWrite;
        private Task? _associationCleanup;
        private Task<HomeResourceExecutionCapability?>? _associationBegin;
        private Task<AuthenticatedResourceActor?>? _associationClaim;
        /// <summary>One reserved attempt. Later calls only read its exact durable
        /// Space association; they never invoke Begin/Claim/settings mutation again.</summary>
        public async Task<bool> AssociateAsync(CancellationToken token = default)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Owner.DemandHost();
                if (IsRetired) throw new UnauthorizedAccessException("The original Space graph operation retired.");
                ThrowFailures(_retainedAssociationFailures);
                if (_associated) return true;
                if (_associationRefused) return false;
                if (_associationReserved) return _associated = await ObserveAssociationCoreAsync(token).ConfigureAwait(false);
                if (_association is null || _observation is null) throw new InvalidOperationException("Submit the distinct association review first.");
                await Owner.RequireOriginalAsync(this, Intent, _observation.Receipt, token).ConfigureAwait(false);
                var reviewed = await Owner._broker.ObservePreparedReviewAsync(_association, token).ConfigureAwait(false);
                if (reviewed.Request?.State != HomePermissionRequestState.Approved) return false;
                var ownership = await Owner._ownership.GetVerifiedAsync("spaces", Intent.Owner.StoreId.ToString("D"), token).ConfigureAwait(false);
                if (ownership is null || !await Owner._ownership.IsCurrentAsync(ownership, CurrentActor, token).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The actual original Space ownership receipt is unavailable.");
                _associationReserved = true; // BEFORE any capability/claim await.
                try
                {
                    _associationBegin = Owner._broker.BeginExecutionCapabilityAsync(_association.RequestId, _associationArguments, token);
                    _associationCapability = await _associationBegin.ConfigureAwait(false);
                    if (_associationCapability is null) { _associationRefused = true; return false; }
                    _associationClaim = Owner._broker.ClaimExecutionAsync(_associationCapability, "spaces", AssociateAction,
                        _associationScopes, _associationArguments, token);
                    var actor = await _associationClaim.ConfigureAwait(false);
                    if (actor != CurrentActor) { _associationRefused = true; return false; }
                    var attestation = Owner._broker.CaptureClaimedAttestation(_associationCapability);
                    if (attestation is null || _observation.Live?.Review.IsPublicationCapability(attestation.Capability) == true)
                    { _associationRefused = true; return false; }
                    // This private guard holds the actual Home/capability/review
                    // leases through BOTH settings checks and its atomic move.
                    var guard = new AssociationCommitGuard(this, _observation, attestation, ownership);
                    var failures = _retainedAssociationFailures;
                    try
                    {
                        _associationAttempted = true;
                        _associationWrite = Owner._spaces.AssociateGraphPublicationAsync(Intent.Owner.EntityId, Pending.PendingSpaceRevision,
                            Intent.OperationId, _observation.Receipt, guard, token);
                        await _associationWrite.ConfigureAwait(false);
                        _associated = true;
                    }
                    catch (Exception error) { Retain(failures, _associationWrite is { IsFaulted: true } ? _associationWrite.Exception! : error); }
                    await ObserveCleanupAsync(failures, () => _associationCleanup = guard.DisposeAsync().AsTask()).ConfigureAwait(false);
                    ThrowFailures(failures);
                    return _associated;
                }
                catch (Exception error)
                {
                    if (!_associationAttempted) _associationRefused = true; // No settings writer was entered.
                    if (_associationBegin is { IsFaulted: true }) Retain(_retainedAssociationFailures, _associationBegin.Exception!);
                    if (_associationClaim is { IsFaulted: true }) Retain(_retainedAssociationFailures, _associationClaim.Exception!);
                    Retain(_retainedAssociationFailures, error);
                    ThrowFailures(_retainedAssociationFailures);
                    throw; // Unknown writes remain pending; do not infer a rollback.
                }
            }
            finally { _gate.Release(); }
        }

        /// <summary>Separates durable effect, source settlement and audit. It
        /// performs no retry, admission, mutation or permission operation.</summary>
        public async Task<SpaceGraphAssociationOutcome> GetRetainedAssociationOutcomeAsync(CancellationToken token = default)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Task?[] sources = [_associationBegin, _associationClaim, _associationWrite, _associationCleanup, _cancelWrite];
                return new(_associated ? true : _associationRefused ? false : null,
                    sources.All(source => source is null || source.IsCompleted),
                    sources.All(source => source is null || source.IsCompletedSuccessfully) && _retainedAssociationFailures.Count == 0,
                    _auditRecorded, _retainedAssociationFailures.Count,
                    _retainedAssociationFailures.Count != 0 ? "SpaceGraphAssociationNeedsAttention" :
                        _associated ? _auditRecorded ? "SpaceGraphAssociated" : "SpaceGraphAssociatedAuditPending" :
                        _associationRefused ? "SpaceGraphNotAssociated" : _associationReserved ? "SpaceGraphAssociationOutcomeUnconfirmed" : "SpaceGraphAssociationNotStarted");
            }
            finally { _gate.Release(); }
        }

        public async Task<bool> ObserveAssociationAsync(CancellationToken token = default)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try { return _associated = await ObserveAssociationCoreAsync(token).ConfigureAwait(false); }
            finally { _gate.Release(); }
        }
        private async Task<bool> ObserveAssociationCoreAsync(CancellationToken token)
        {
            Owner.DemandHost();
            if (await Owner.RequireActorAsync(token).ConfigureAwait(false) != CurrentActor)
                throw new UnauthorizedAccessException("The original Space graph actor changed.");
            _ = await Owner._spaceAuthority.CaptureWriteAdmissionForActorAsync(CurrentActor, token).ConfigureAwait(false);
            var current = await Owner._spaces.ReadExistingAsync(Intent.Owner.EntityId, token).ConfigureAwait(false);
            var reference = current?.GraphConfiguration?.GetReference(Pending.Page);
            return current is not null && current.Revision >= Pending.PendingSpaceRevision + 1 &&
                reference is not null && ReceiptMatches(Pending, reference.LastAssociatedPublication) &&
                reference.GraphId == Pending.GraphId && reference.CapabilityProfileId == Pending.CapabilityProfileId;
        }

        public async Task<HomePermissionOperationResult> FinishAssociationAuditAsync(CancellationToken token = default)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_retainedAssociationFailures.Count != 0)
                    return new(false, "SPACE_GRAPH_ASSOCIATION_SOURCE_NEEDS_ATTENTION", "The association effect and original source/cleanup failures remain distinct; inspect the retained failure.");
                if (_auditRecorded) return new(true, "SPACE_GRAPH_ASSOCIATION_AUDIT_RECORDED", "The exact association outcome was acknowledged.");
                if (_associationCapability is null || !_associated && !_associationRefused)
                    return new(false, "SPACE_GRAPH_ASSOCIATION_UNCONFIRMED", "Retain pending evidence and observe; no association audit outcome is known.");
                var attestation = Owner._broker.CaptureClaimedAttestation(_associationCapability);
                HomePermissionOperationResult result;
                if (attestation is not null)
                    result = await Owner._broker.CompleteExecutionAsync(_associationCapability,
                        new(_associated ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
                            _associated ? "SPACE_GRAPH_ASSOCIATED" : "SPACE_GRAPH_NOT_ASSOCIATED",
                            _associated ? "The exact published graph was associated with the selected Space page." : "This reserved attempt did not enter the Space writer.",
                            _associated ? [new("space.definition", EntityScope(Intent.Owner))] : []), token).ConfigureAwait(false);
                else
                {
                    result = await Owner._broker.RetryRejectedClaimAuditAsync(_associationCapability, token).ConfigureAwait(false);
                    if (!result.Succeeded) result = await Owner._broker.AbortUnclaimedExecutionAsync(_associationCapability, token).ConfigureAwait(false);
                }
                if (result.Succeeded) _auditRecorded = true;
                return result;
            }
            finally { _gate.Release(); }
        }

        private bool _cancelWriteReserved, _cancelled;
        private Task<SpaceDefinition>? _cancelWrite;
        /// <summary>Clears a pending marker only after the original graph review
        /// has joined and retired with no publication. Unknown effects remain pending.</summary>
        public async Task<bool> CancelUnpublishedAsync(CancellationToken token = default)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ThrowFailures(_retainedAssociationFailures);
                if (_cancelled) return true;
                if (IsRecovery || IsRetired || _association is not null)
                    throw new InvalidOperationException("Only the original unpublished review can clear its own pending marker.");
                await Owner.RequireOriginalAsync(this, Intent, null, token).ConfigureAwait(false);
                if (_cancelWriteReserved)
                {
                    // An uncertain cancellation reply cannot run the writer again.
                    var observed = await Owner._spaces.ReadExistingAsync(Intent.Owner.EntityId, token).ConfigureAwait(false);
                    return observed?.Revision == Pending.PendingSpaceRevision + 1 && observed.GraphConfiguration?.Pending is null &&
                        observed.GraphConfiguration?.GetReference(Pending.Page) == Pending.PriorReference;
                }
                if (!await Publication.RetireAsync(token).ConfigureAwait(false)) return false;
                // Observe AFTER retirement; a concurrent Publish admitted before
                // retirement is now joined, and cannot race this no-effect decision.
                var outcome = await Publication.GetRetainedOutcomeAsync(token).ConfigureAwait(false);
                if (outcome.Published != false && outcome.Code != "GraphPublicationNotStarted") return false;
                _cancelWriteReserved = true;
                try
                {
                    _cancelWrite = Owner._spaces.ClearUnpublishedGraphPendingAsync(Intent.Owner.EntityId,
                        Pending.PendingSpaceRevision, Intent.OperationId, CurrentActor, token);
                    await _cancelWrite.ConfigureAwait(false);
                    _cancelled = true;
                    return true;
                }
                catch (Exception error)
                {
                    Retain(_retainedAssociationFailures, _cancelWrite is { IsFaulted: true } ? _cancelWrite.Exception! : error);
                    ThrowFailures(_retainedAssociationFailures);
                    throw;
                }
            }
            finally { _gate.Release(); }
        }

        /// <summary>Joins all admitted operations and retires their original Home
        /// reviews. Durable pending metadata remains available for restart recovery.</summary>
        public async Task<bool> RetireAsync(CancellationToken token = default)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (IsRetired) { ThrowFailures(_retainedAssociationFailures); return true; }
                var associationRetired = _association is null;
                var publicationRetired = _publication is null;
                Task<bool>? originalAssociation = null, originalPublication = null;
                if (_association is not null)
                {
                    try
                    {
                        originalAssociation = Owner._broker.RetirePreparedReviewAsync(_association, token);
                        associationRetired = await originalAssociation.ConfigureAwait(false);
                    }
                    catch (Exception error) { Retain(_retainedAssociationFailures,
                        originalAssociation is { IsFaulted: true } ? originalAssociation.Exception! : error); }
                }
                if (_publication is not null)
                {
                    try
                    {
                        originalPublication = _publication.RetireAsync(token);
                        publicationRetired = await originalPublication.ConfigureAwait(false);
                    }
                    catch (Exception error) { Retain(_retainedAssociationFailures,
                        originalPublication is { IsFaulted: true } ? originalPublication.Exception! : error); }
                }
                if (associationRetired && publicationRetired)
                {
                    Volatile.Write(ref _retired, 1);
                    Owner._graphResources.RetireSpaceAssociation(this);
                    if (_observation is not null) Owner._observations.TryRemove(_observation, out _);
                    if (Owner._operations.TryRemove(this, out _)) lock (Owner._capacity)
                    { Owner._reserved--; Owner._reservedSpaces.Remove(Intent.Owner.EntityId); }
                }
                ThrowFailures(_retainedAssociationFailures);
                return IsRetired;
            }
            finally { _gate.Release(); }
        }
    }

    private sealed class AssociationCommitGuard(Operation operation, PublicationEvidence observation,
        HomeClaimedResourceAttestation attestation, VerifiedResourceStoreOwnership receipt) : ISettingsCommitAdmission, IAsyncDisposable
    {
        private IAsyncDisposable? _review, _completion;
        private IHomeLocalOperationLease? _home;
        private bool _acquired, _disposed;
        public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token)
        {
            var owner = operation.Owner;
            if (_disposed || operation.IsRetired || !owner.HostAllows() || context.StoreIdentity.SchemaVersion != 1 ||
                context.StoreIdentity.StoreId != operation.Intent.Owner.StoreId ||
                observation.Actor != operation.CurrentActor || attestation.Actor != observation.Actor ||
                attestation.Submission.Scope.TargetAppId != "spaces" || attestation.Submission.Scope.ActionName != AssociateAction ||
                !ReferenceEquals(owner._broker.CaptureClaimedAttestation(attestation.Capability), attestation) ||
                observation.Live?.Review.IsPublicationCapability(attestation.Capability) == true) return false;
            if (!_acquired)
            {
                if (context.Phase != SettingsCommitPhase.Admission) return false;
                _acquired = true;
                // Settings is already locked. Never wait on graph review: its
                // publication origin reads settings before taking the Home gate.
                if (observation.Live is { } live)
                {
                    _review = live.Review.TryAcquireAssociationLifetime(live);
                    if (_review is null) return false;
                }
                _completion = await attestation.Capability.AcquireCommitCompletionLeaseAsync(owner._broker, token).ConfigureAwait(false);
                if (_completion is null) return false;
                _home = await owner._home.AcquireLocalOperationLeaseCoreAsync(owner._actors, operation.CurrentActor,
                    new RawGuard(operation, observation, attestation, receipt), token).ConfigureAwait(false);
                if (_home is null) return false;
            }
            return _home is not null && await _home.IsCurrentAsync(token).ConfigureAwait(false);
        }
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            var failures = new List<Exception>();
            if (_home is not null) await ObserveCleanupAsync(failures, () => _home.DisposeAsync().AsTask()).ConfigureAwait(false);
            if (_completion is not null) await ObserveCleanupAsync(failures, () => _completion.DisposeAsync().AsTask()).ConfigureAwait(false);
            if (_review is not null) await ObserveCleanupAsync(failures, () => _review.DisposeAsync().AsTask()).ConfigureAwait(false);
            ThrowFailures(failures);
        }
    }
    private sealed class RawGuard(Operation operation, PublicationEvidence observation,
        HomeClaimedResourceAttestation attestation, VerifiedResourceStoreOwnership receipt) : IHomeStateCommitActorGuard
    {
        public ValueTask<bool> CheckAsync(HomeCoreStoredState actual, AuthenticatedResourceActor actor,
            HomeStateCommitPhase phase, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var owner = operation.Owner;
            return ValueTask.FromResult(!operation.IsRetired && owner.HostAllows() && actor == operation.CurrentActor &&
                HomeLocalStoreOwnership.IsReceiptCurrentInState(actual, receipt) &&
                owner.IsEvidenceCurrentInState(observation, actor, actual) &&
                owner._broker.IsClaimedAttestationCurrentInState(attestation, actor, actual));
        }
    }

    // Drain every retained lease even if earlier cleanup faults. Keep each
    // original leaf exactly once; a faulted OCE must not become cancellation.
    private static async Task ObserveCleanupAsync(List<Exception> failures, Func<Task> factory)
    {
        Task? actual = null;
        try { actual = factory(); await actual.ConfigureAwait(false); }
        catch (Exception error) { Retain(failures, actual is { IsFaulted: true } ? actual.Exception! : error); }
    }
    private static void Retain(List<Exception> failures, Exception error)
    {
        if (error is AggregateException { InnerExceptions.Count: > 0 } aggregate)
        { foreach (var inner in aggregate.InnerExceptions) Retain(failures, inner); return; }
        if (!failures.Any(prior => ReferenceEquals(prior, error))) failures.Add(error);
    }
    private static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 0) return;
        if (failures.Count == 1 && failures[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException("The original Space graph operation or its joined leases faulted.", failures);
    }
}

/// <summary>Read-only retained evidence; never a capability or permission grant.</summary>
public sealed record SpaceGraphAssociationOutcome(bool? Associated, bool OriginalSourcesJoined,
    bool OriginalSourcesSucceeded, bool AuditRecorded, int RetainedFailureCount, string Code);
