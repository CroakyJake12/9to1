using System.Text.Json;
using Haven.Application.Automations;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

public sealed record AutomationDefinitionOperationResult(bool? Committed, string Code,
    AutomationDefinitionCommitResult? Commit, [property: System.Text.Json.Serialization.JsonIgnore] AutomationDefinitionAuditRecovery? AuditRecovery)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public AutomationDefinitionCommitObservationRecovery? ObservationRecovery { get; init; }
}

/// <summary>A host-issued audit-only object. It retains the original capability/outcome and never resolves,
/// approves or executes the definition again. It is not serializable execution authority.</summary>
public sealed class AutomationDefinitionAuditRecovery
{
    private readonly HomeResourceOperationBroker _home;
    private readonly HomeResourceExecutionCapability _capability;
    private readonly HomeExecutionOutcome _outcome;
    private readonly bool _claimWasUnavailable;
    private readonly bool _abortUnclaimed;
    internal AutomationDefinitionAuditRecovery(HomeResourceOperationBroker home, HomeResourceExecutionCapability capability,
        HomeExecutionOutcome outcome, bool claimWasUnavailable, bool abortUnclaimed = false)
    { _home = home; _capability = capability; _outcome = outcome; _claimWasUnavailable = claimWasUnavailable; _abortUnclaimed = abortUnclaimed; }
    internal bool IsUnclaimedAbort => _abortUnclaimed;
    public async Task<bool> FinishAsync(CancellationToken cancellationToken = default)
    {
        if (_abortUnclaimed) return (await _home.AbortUnclaimedExecutionAsync(_capability, cancellationToken).ConfigureAwait(false)).Succeeded;
        if ((await _home.CompleteExecutionAsync(_capability, _outcome, cancellationToken).ConfigureAwait(false)).Succeeded) return true;
        // A consumed rejected claim has its own retained rejection, not a fabricated owner completion.
        if (!_claimWasUnavailable) return false;
        if ((await _home.RetryRejectedClaimAuditAsync(_capability, cancellationToken).ConfigureAwait(false)).Succeeded) return true;
        var actual = await _home.GetExecutionDecisionAsync(_capability, cancellationToken).ConfigureAwait(false);
        return actual?.State is HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked or HomePermissionRequestState.Cancelled ||
            actual is { State: HomePermissionRequestState.Failed, Code: "HOME_RESOURCE_CLAIM_REJECTED" };
    }
}

/// <summary>Same-host exact commit observation only. It never claims or writes SQL or resolves a new execution grant.
/// Missing/superseded receipts remain unknown. Only a confirmed original commit may supply its fixed Home completion.</summary>
public sealed class AutomationDefinitionCommitObservationRecovery
{
    private readonly IAutomationOwnerRepository _definitions;
    private readonly IReusableTaskOwnerRepository _tasks;
    private readonly AutomationDefinitionChange _change;
    private readonly HomeResourceOperationBroker _home;
    private readonly HomeResourceExecutionCapability _capability;
    private readonly SemaphoreSlim _observations = new(1, 1);
    private AutomationDefinitionOperationResult? _confirmed;
    private bool _sqlAdmissionIssued;
    internal AutomationDefinitionCommitObservationRecovery(IAutomationOwnerRepository definitions, IReusableTaskOwnerRepository tasks,
        AutomationDefinitionChange change, HomeResourceOperationBroker home, HomeResourceExecutionCapability capability)
    { _definitions = definitions; _tasks = tasks; _change = change; _home = home; _capability = capability; }
    internal void EnableAfterOwnerAdmission() => _sqlAdmissionIssued = true;
    internal void SealKnown(AutomationDefinitionOperationResult result) => Interlocked.CompareExchange(ref _confirmed, result, null);
    public async Task<AutomationDefinitionOperationResult> ObserveAsync(CancellationToken cancellationToken = default)
    {
        AutomationDefinitionOperationResult Unknown() => new(null, "DefinitionOutcomeUnconfirmed", null, null) { ObservationRecovery = this };
        await _observations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_confirmed is not null)
            {
                if (_confirmed.AuditRecovery is { } pending)
                {
                    try
                    {
                        if (await pending.FinishAsync(cancellationToken).ConfigureAwait(false))
                            _confirmed = _confirmed with { Committed = pending.IsUnclaimedAbort ? false : _confirmed.Committed,
                                Code = _confirmed.Committed == true ? "DefinitionCommitted" : "DefinitionNotCommitted", AuditRecovery = null };
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException) { }
                }
                return _confirmed;
            }
            if (!_sqlAdmissionIssued) return Unknown();
            if ((await _definitions.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false)).StoreId != _change.StoreID) return Unknown();
            var observed = _change.EntityKind == AutomationOwnerEntityKind.Automation
                ? await _definitions.ObserveCommitAsync(_change.EntityID, _change.OperationID, _change.PayloadSHA256,
                    _change.ExpectedRevision, cancellationToken).ConfigureAwait(false)
                : await _tasks.ObserveTaskCommitAsync(_change.EntityID, _change.OperationID, _change.PayloadSHA256,
                    _change.ExpectedRevision, cancellationToken).ConfigureAwait(false);
            if (observed.Observation != AutomationCommitReceiptObservation.CurrentCommitted || observed.Receipt is not { } receipt ||
                receipt.StoreId != _change.StoreID || receipt.ObservedOwner.StoreId != _change.StoreID ||
                receipt.ObservedOwner.ActorId != _change.OriginalActor.ActorId || receipt.ObservedOwner.ProfileId != _change.OriginalActor.ProfileId ||
                receipt.ObservedOwner.AuthenticationRevision != _change.OriginalActor.AuthenticationRevision ||
                receipt.ObservedOwner.AccountId != _change.OriginalActor.AccountId || receipt.ObservedOwner.OrganisationId != _change.OriginalActor.OrganisationId)
                return Unknown();
            var commit = new AutomationDefinitionCommitResult(true, "DefinitionCommitted", receipt.CommittedRevision,
                _change.OperationID, _change.PayloadSHA256);
            var audit = new AutomationDefinitionAuditRecovery(_home, _capability,
                new(HomePermissionRequestState.Succeeded, "DefinitionCommitted", "The canonical definition revision committed.", []), false);
            // Seal the first actual committed receipt before any cancellable/faulting audit transport.
            // Later supersession cannot erase an already observed commit or authorize owner replay.
            _confirmed = new(true, "DefinitionCommittedAuditPending", commit, audit);
            try
            {
                if (await audit.FinishAsync(cancellationToken).ConfigureAwait(false))
                    _confirmed = _confirmed with { Committed = pending.IsUnclaimedAbort ? false : _confirmed.Committed,
                                Code = _confirmed.Committed == true ? "DefinitionCommitted" : "DefinitionNotCommitted", AuditRecovery = null };
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException) { }
            return _confirmed;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or JsonException or System.Data.Common.DbException) { return _confirmed ?? Unknown(); }
        finally { _observations.Release(); }
    }
}

/// <summary>Same canonical SQL owner, actual original actor and one-use Home capability. SQL/Home are
/// separate durable boundaries. Ambiguous SQL commit observations never become a failure or replay permission.</summary>
public sealed class AutomationHomeDefinitionOperation(IAutomationOwnerRepository automations,
    IReusableTaskOwnerRepository tasks, AutomationLocalStoreAuthority authority, HomeResourceOperationBroker home)
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (string Digest, AutomationDefinitionOperationResult Result)> _issued = new();
    private enum AttemptPhase { Waiting, Executing, Aborting }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, AttemptPhase> _attempts = new();
    private readonly object _observationIssuance = new();
    private readonly Dictionary<Guid, (string Digest, HomeResourceExecutionCapability Capability, AutomationDefinitionCommitObservationRecovery Observation)> _observations = [];
    /// <summary>Retains one actual capability observation before any execution wait. Never claims or writes a row.</summary>
    public AutomationDefinitionCommitObservationRecovery PrepareObservation(AutomationDefinitionChange change, HomeResourceExecutionCapability capability)
    {
        ArgumentNullException.ThrowIfNull(change); ArgumentNullException.ThrowIfNull(capability);
        lock (_observationIssuance)
        {
            if (_observations.TryGetValue(change.OperationID, out var retained))
            {
                if (retained.Digest != change.PayloadSHA256 || !ReferenceEquals(retained.Capability, capability))
                    throw new InvalidOperationException("Operation observation identity conflicts with the original capability.");
                return retained.Observation;
            }
            if (_observations.Count >= 10000) throw new InvalidOperationException("Host operation observation capacity reached.");
            var observation = new AutomationDefinitionCommitObservationRecovery(automations, tasks, change, home, capability);
            _observations.Add(change.OperationID, (change.PayloadSHA256, capability, observation));
            return observation;
        }
    }
    public async Task<AutomationDefinitionOperationResult> ExecuteAsync(AutomationDefinitionChange change,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change); ArgumentNullException.ThrowIfNull(capability);
        var observation = PrepareObservation(change, capability);
        var unknown = new AutomationDefinitionOperationResult(null, "DefinitionOutcomeUnconfirmed", null, null) { ObservationRecovery = observation };
        if (!_issued.TryAdd(change.OperationID, (change.PayloadSHA256, unknown)))
        {
            var retained = _issued[change.OperationID];
            return retained.Digest == change.PayloadSHA256 ? retained.Result : new(null, "OperationIdentityConflict", null, null);
        }
        _attempts.TryAdd(change.OperationID, AttemptPhase.Waiting);
        try { await _operations.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!_attempts.TryUpdate(change.OperationID, AttemptPhase.Aborting, AttemptPhase.Waiting)) return unknown;
            return await AbortUnstartedCoreAsync(change, capability, observation).ConfigureAwait(false);
        }
        _attempts.TryUpdate(change.OperationID, AttemptPhase.Executing, AttemptPhase.Waiting);
        try
        {
            // This exact attempt was reserved before the cancellable gate; duplicates never enter execution.
            AutomationDefinitionOperationResult result;
            var sqlAttempted = false;
            var admitted = false;
            try
            {
                var admission = await authority.ClaimAsync(change, capability, cancellationToken).ConfigureAwait(false);
                admitted = admission is not null;
                if (admission is null)
                    result = await FinishKnownAsync(change, observation, capability, false, "OwnerAdmissionUnavailable", null,
                        claimWasUnavailable: true).ConfigureAwait(false);
                else
                {
                    observation.EnableAfterOwnerAdmission();
                    sqlAttempted = true;
                    var commit = change.EntityKind == AutomationOwnerEntityKind.Automation
                        ? await automations.CompareExchangeOwnedAsync(change, admission, cancellationToken).ConfigureAwait(false)
                        : await tasks.CompareExchangeOwnedTaskAsync(change, admission, cancellationToken).ConfigureAwait(false);
                    result = await FinishKnownAsync(change, observation, capability, commit.Committed, commit.Code, commit,
                        claimWasUnavailable: false).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
                or ArgumentException or NotSupportedException or OperationCanceledException or JsonException or OverflowException or System.Data.Common.DbException)
            {
                if (_issued[change.OperationID].Result is { Committed: not null } sealedResult)
                    result = sealedResult;
                else if (!sqlAttempted)
                    result = await FinishKnownAsync(change, observation, capability, false,
                        error is OperationCanceledException ? "CancelledBeforeDefinitionCommit" : "DefinitionAdmissionFailed",
                        null, claimWasUnavailable: !admitted).ConfigureAwait(false);
                else
                {
                    // A failed return path can follow a committed SQL transaction. Observe only this original issued operation.
                    result = await observation.ObserveAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            _issued[change.OperationID] = (change.PayloadSHA256, result);
            return result;
        }
        finally { _operations.Release(); }
    }
    /// <summary>Capacity-failed caller cleanup only. An existing issued attempt is observation-only and cannot be overwritten/aborted.</summary>
    public Task<AutomationDefinitionOperationResult> AbortUnstartedAsync(AutomationDefinitionChange change, HomeResourceExecutionCapability capability)
    {
        if (_issued.TryGetValue(change.OperationID, out var issued))
        {
            bool originalCapability;
            lock (_observationIssuance)
                originalCapability = _observations.TryGetValue(change.OperationID, out var retained) && ReferenceEquals(retained.Capability, capability);
            if (originalCapability)
                return Task.FromResult(issued.Digest == change.PayloadSHA256 ? issued.Result : new AutomationDefinitionOperationResult(null, "OperationIdentityConflict", null, null));
            // A second actual capability needs its own abort observation; it cannot borrow or replace the old commit.
            return AbortUnstartedCoreAsync(change, capability, null);
        }
        return AbortUnstartedCoreAsync(change, capability, null);
    }
    private async Task<AutomationDefinitionOperationResult> AbortUnstartedCoreAsync(AutomationDefinitionChange change,
        HomeResourceExecutionCapability capability, AutomationDefinitionCommitObservationRecovery? observation)
    {
        var audit = new AutomationDefinitionAuditRecovery(home, capability,
            new(HomePermissionRequestState.Failed, "CancelledBeforeDefinitionClaim", "The original unclaimed capability requires abort observation.", []),
            claimWasUnavailable: false, abortUnclaimed: true);
        var result = new AutomationDefinitionOperationResult(null, "DefinitionAbortOutcomeUnconfirmed", null, audit);
        try
        {
            // Only actual issuer acknowledgement proves this capability was unclaimed. NOT_OWNED is never a no-effect proof.
            if (await audit.FinishAsync(CancellationToken.None).ConfigureAwait(false))
                result = result with { Committed = false, Code = "CancelledBeforeDefinitionClaim", AuditRecovery = null };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or OperationCanceledException or JsonException) { }
        if (observation is not null && _attempts.TryGetValue(change.OperationID, out var phase) && phase == AttemptPhase.Aborting)
        {
            _issued[change.OperationID] = (change.PayloadSHA256, result);
            observation.SealKnown(result);
        }
        return result;
    }
    private async Task<AutomationDefinitionOperationResult> FinishKnownAsync(AutomationDefinitionChange change,
        AutomationDefinitionCommitObservationRecovery observation, HomeResourceExecutionCapability capability,
        bool committed, string code, AutomationDefinitionCommitResult? commit, bool claimWasUnavailable)
    {
        var outcome = new HomeExecutionOutcome(committed ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
            code, committed ? "The canonical definition revision committed." : "This definition operation did not reach a durable commit.", []);
        var recovery = new AutomationDefinitionAuditRecovery(home, capability, outcome, claimWasUnavailable);
        var known = new AutomationDefinitionOperationResult(committed,
            committed ? "DefinitionCommittedAuditPending" : "DefinitionPrecommitAuditPending", commit, recovery);
        // Seal the exact SQL return outcome BEFORE any cancellable/faulting audit transport.
        _issued[change.OperationID] = (change.PayloadSHA256, known);
        observation.SealKnown(known);
        try
        {
            if (await recovery.FinishAsync(CancellationToken.None).ConfigureAwait(false)) return new(committed, code, commit, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException) { }
        return known;
    }
}
