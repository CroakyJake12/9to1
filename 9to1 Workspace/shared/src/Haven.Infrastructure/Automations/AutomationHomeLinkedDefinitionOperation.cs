using Haven.Application.Automations;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

public sealed record AutomationLinkedDefinitionOutcome(bool? Committed, string Code);

/// <summary>Private same-host pair attempt, issued before Home Begin. A known effect is sealed before audit IO.
/// Unknown SQL may only inspect BOTH original exact receipts; Finish never reclaims or retries the SQL writer.</summary>
public sealed class AutomationLinkedDefinitionExecution
{
    private readonly AutomationLinkedDefinitionChange _pair;
    private readonly AutomationRepository _definitions;
    private readonly WorkspaceStateRepository _tasks;
    private readonly AutomationLocalStoreAuthority _authority;
    private readonly HomeResourceOperationBroker _home;
    private readonly Func<CancellationToken, Task> _requireOriginal;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HomeResourceExecutionCapability? _taskCapability;
    private HomeResourceExecutionCapability? _scheduleCapability;
    private bool _executionAttempted;
    private bool _sqlAttempted;
    private AutomationLinkedDefinitionOutcome? _confirmed;
    private bool _taskAuditRecorded;
    private bool _scheduleAuditRecorded;

    internal AutomationLinkedDefinitionExecution(AutomationLinkedDefinitionChange pair, AutomationRepository definitions,
        WorkspaceStateRepository tasks, AutomationLocalStoreAuthority authority, HomeResourceOperationBroker home,
        Func<CancellationToken, Task> requireOriginal)
    { _pair = pair; _definitions = definitions; _tasks = tasks; _authority = authority; _home = home; _requireOriginal = requireOriginal; }

    internal void RetainTaskCapability(HomeResourceExecutionCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (_taskCapability is not null && !ReferenceEquals(_taskCapability, capability)) throw new InvalidOperationException("Original task capability changed.");
        _taskCapability = capability;
    }
    internal void RetainScheduleCapability(HomeResourceExecutionCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (_scheduleCapability is not null && !ReferenceEquals(_scheduleCapability, capability)) throw new InvalidOperationException("Original schedule capability changed.");
        _scheduleCapability = capability;
    }
    internal async Task<AutomationLinkedDefinitionOutcome> ExecuteOnceAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_executionAttempted) return await ObserveCoreAsync(token).ConfigureAwait(false);
            if (_taskCapability is null || _scheduleCapability is null) return new(null, "LinkedBeginOutcomeUnconfirmed");
            _executionAttempted = true; // Before Claim/SQL; ambiguous execution transport never permits replay.
            try
            {
                var first = await _authority.ClaimAsync(_pair.ReusableChange, _taskCapability, token).ConfigureAwait(false);
                var second = await _authority.ClaimAsync(_pair.ScheduleChange, _scheduleCapability, token).ConfigureAwait(false);
                if (first is null || second is null)
                    Seal(false, "LinkedAdmissionUnavailable");
                else
                {
                    _sqlAttempted = true;
                    var committed = await _definitions.CompareExchangeLinkedPairAsync(_pair, _tasks, first, second, token).ConfigureAwait(false);
                    Seal(committed.Committed, committed.Code);
                }
            }
            catch (Exception)
            {
                // Before this private attempt entered SQL, neither pair row could have changed.
                // After SQL entry, absence/loss of a return is unknown, never a negative mutation result.
                if (!_sqlAttempted) Seal(false, "LinkedStoppedBeforeSql");
            }
            return await ObserveCoreAsync(token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    public async Task<AutomationLinkedDefinitionOutcome> FinishAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { return await ObserveCoreAsync(token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    internal async Task<AutomationLinkedDefinitionOutcome> StopBeforeExecutionAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // A retained partial Begin can be closed only while this private attempt never entered Claim/SQL.
            if (_executionAttempted) return await ObserveCoreAsync(token).ConfigureAwait(false);
            _executionAttempted = true;
            Seal(false, "LinkedStoppedBeforeClaim");
            return await ObserveCoreAsync(token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    private void Seal(bool committed, string code)
    { _confirmed ??= new(committed, code); }
    private async Task<AutomationLinkedDefinitionOutcome> ObserveCoreAsync(CancellationToken token)
    {
        if (_confirmed is null && _sqlAttempted)
        {
            try
            {
                await _requireOriginal(token).ConfigureAwait(false);
                if ((await _definitions.GetStoreIdentityAsync(token).ConfigureAwait(false)).StoreId != _pair.ReusableChange.StoreID)
                    return new(null, "LinkedOutcomeUnconfirmed");
                var task = await _tasks.ObserveTaskCommitAsync(_pair.ReusableChange.EntityID, _pair.ReusableChange.OperationID,
                    _pair.ReusableChange.PayloadSHA256, _pair.ReusableChange.ExpectedRevision, token).ConfigureAwait(false);
                var schedule = await _definitions.ObserveCommitAsync(_pair.ScheduleChange.EntityID, _pair.ScheduleChange.OperationID,
                    _pair.ScheduleChange.PayloadSHA256, _pair.ScheduleChange.ExpectedRevision, token).ConfigureAwait(false);
                await _requireOriginal(token).ConfigureAwait(false);
                if (Exact(task, _pair.ReusableChange) && Exact(schedule, _pair.ScheduleChange))
                    Seal(true, "LinkedDefinitionsCommitted"); // Before any cancellable/faulting audit.
            }
            catch (Exception) { /* Unknown or unavailable original receipt remains observation-only. */ }
        }
        if (_confirmed is null) return new(null, "LinkedOutcomeUnconfirmed");
        // Known audit recovery does not re-resolve the current actor/store or replay either mutation.
        if (_taskCapability is not null && !_taskAuditRecorded)
            _taskAuditRecorded = await FinishAuditAsync(_taskCapability, _confirmed.Committed == true, token).ConfigureAwait(false);
        if (_scheduleCapability is not null && !_scheduleAuditRecorded)
            _scheduleAuditRecorded = await FinishAuditAsync(_scheduleCapability, _confirmed.Committed == true, token).ConfigureAwait(false);
        var complete = _taskCapability is not null && _scheduleCapability is not null && _taskAuditRecorded && _scheduleAuditRecorded;
        return new(_confirmed.Committed, complete ? _confirmed.Committed == true ? "LinkedDefinitionsCommitted" : "LinkedDefinitionsNotCommitted"
            : _confirmed.Committed == true ? "LinkedDefinitionsCommittedAuditPending" : "LinkedDefinitionsNotCommittedAuditPending");
    }
    private async Task<bool> FinishAuditAsync(HomeResourceExecutionCapability capability, bool committed, CancellationToken token)
    {
        try
        {
            var outcome = new HomeExecutionOutcome(committed ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
                committed ? "LinkedDefinitionsCommitted" : "LinkedDefinitionsNotCommitted",
                committed ? "Both canonical linked definition revisions committed." : "This linked operation did not commit either definition.", []);
            if ((await _home.CompleteExecutionAsync(capability, outcome, token).ConfigureAwait(false)).Succeeded) return true;
            if (committed) return false; // Never abort/reject a known successful owner effect.
            if ((await _home.RetryRejectedClaimAuditAsync(capability, token).ConfigureAwait(false)).Succeeded) return true;
            // Only the sealed private no-SQL/no-commit attempt reaches here; this cannot race a later owner execution.
            if ((await _home.AbortUnclaimedExecutionAsync(capability, token).ConfigureAwait(false)).Succeeded) return true;
            var decision = await _home.GetExecutionDecisionAsync(capability, token).ConfigureAwait(false);
            return decision?.State is HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked or HomePermissionRequestState.Cancelled ||
                decision is { State: HomePermissionRequestState.Failed, Code: "HOME_RESOURCE_CLAIM_REJECTED" or "HOME_RESOURCE_EXECUTION_ABORTED" };
        }
        catch (Exception) { return false; } // Preserve sealed outcome and exact original handles.
    }
    private static bool Exact(AutomationCommitReceiptRead observed, AutomationDefinitionChange change)
    {
        var receipt = observed.Receipt;
        var actor = change.OriginalActor;
        return observed.Observation == AutomationCommitReceiptObservation.CurrentCommitted && receipt is not null &&
            receipt.SchemaVersion == 1 && receipt.StoreId == change.StoreID && receipt.EntityId == change.EntityID && receipt.OperationId == change.OperationID &&
            receipt.EntityKind == (change.EntityKind == AutomationOwnerEntityKind.Automation
                ? AutomationDefinitionEntityKind.Automation : AutomationDefinitionEntityKind.ReusableTask) &&
            receipt.PayloadSha256 == change.PayloadSHA256 && receipt.ExpectedRevision == change.ExpectedRevision &&
            receipt.CommittedRevision == change.ExpectedRevision + 1 && receipt.ObservedOwner.StoreId == change.StoreID &&
            receipt.ObservedOwner.ProfileId == actor.ProfileId && receipt.ObservedOwner.ActorId == actor.ActorId &&
            receipt.ObservedOwner.AuthenticationRevision == actor.AuthenticationRevision &&
            receipt.ObservedOwner.AccountId == actor.AccountId && receipt.ObservedOwner.OrganisationId == actor.OrganisationId;
    }
}
