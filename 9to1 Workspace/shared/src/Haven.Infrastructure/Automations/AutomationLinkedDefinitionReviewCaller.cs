using Haven.Application.Automations;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

/// <summary>Individual Home approvals for one immutable full pair, over the original definition-owner selection.
/// No ambient capture, enabled publication, raw linked writes, or ambiguous Begin/delivery replay.</summary>
public sealed class AutomationLinkedDefinitionReviewCaller(AutomationDefinitionReviewCaller originalCaller,
    AutomationRepository definitions, WorkspaceStateRepository tasks, AutomationLocalStoreAuthority authority,
    HomeResourceOperationBroker home, HomePermissionTrustService permissions)
{
    public async Task<AutomationLinkedDefinitionReview> ReviewAsync(IAutomationDefinitionCallerSelection originalSelection,
        ReusableTaskDefinition reusable, AutomationDefinition schedule, long expectedReusableRevision,
        long expectedScheduleRevision, AutomationDefinitionChangeKind kind, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(originalSelection);
        if (!originalCaller.HasRepositoryOrigin(originalSelection, definitions, tasks))
            throw new UnauthorizedAccessException("The linked pair does not retain the original caller repository graph.");
        var pair = AutomationDefinitionChange.CaptureLinkedPair(originalSelection.StoreId, originalSelection.Actor,
            reusable, expectedReusableRevision, schedule, expectedScheduleRevision, kind);
        await originalCaller.RequireCurrentAsync(originalSelection, token).ConfigureAwait(false);
        var task = await tasks.GetOwnedTaskAsync(pair.ReusableChange.EntityID, token).ConfigureAwait(false);
        var linked = await definitions.GetOwnedAsync(pair.ScheduleChange.EntityID, token).ConfigureAwait(false);
        if (task is null || linked is null || task.RequiresRecovery || linked.RequiresRecovery ||
            task.Value.Revision != expectedReusableRevision || linked.Value.Revision != expectedScheduleRevision ||
            task.Value.OwnerBinding is null || task.Value.OwnerBinding != linked.Value.OwnerBinding ||
            task.Value.OwnerBinding.StoreId != originalSelection.StoreId ||
            task.Value.OwnerBinding.ProfileId != originalSelection.Actor.ProfileId ||
            task.Value.ContainerId != linked.Value.ContainerId ||
            !ScheduledGraphAutomationPayloadCodec.TryDeserialize(linked.Value.Instruction, out var payload) ||
            payload.WorkflowId != task.Value.Id || payload.GraphJson != task.Value.GraphJson)
            throw new UnauthorizedAccessException("Original linked definitions are unavailable or stale.");
        await originalCaller.RequireCurrentAsync(originalSelection, token).ConfigureAwait(false);
        if (!originalCaller.HasRepositoryOrigin(originalSelection, definitions, tasks))
            throw new UnauthorizedAccessException("The linked execution does not retain the original caller repository graph.");
        var execution = new AutomationLinkedDefinitionExecution(pair, definitions, tasks, authority, home,
            ct => originalCaller.RequireCurrentAsync(originalSelection, ct));
        var review = new AutomationLinkedDefinitionReview(pair, execution, home, permissions,
            ct => originalCaller.RequireCurrentAsync(originalSelection, ct));
        // Actual issued object exists before the first durable review submission; loss never repeats submission.
        await review.SubmitOnceAsync(token).ConfigureAwait(false);
        return review;
    }
}

public sealed class AutomationLinkedDefinitionReview
{
    private readonly AutomationLinkedDefinitionChange _pair;
    private readonly AutomationLinkedDefinitionExecution _execution;
    private readonly HomeResourceOperationBroker _home;
    private readonly HomePermissionTrustService _permissions;
    private readonly Func<CancellationToken, Task> _requireOriginal;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _submissionAttempted;
    private bool _submissionCompleted;
    private bool _beginAttempted;
    private bool _executed;
    private string? _taskRequest;
    private string? _scheduleRequest;
    internal AutomationLinkedDefinitionReview(AutomationLinkedDefinitionChange pair, AutomationLinkedDefinitionExecution execution,
        HomeResourceOperationBroker home, HomePermissionTrustService permissions, Func<CancellationToken, Task> requireOriginal)
    { _pair = pair; _execution = execution; _home = home; _permissions = permissions; _requireOriginal = requireOriginal; }
    public Guid PairID => _pair.PairID;
    public IReadOnlyList<string> RequestIDs => Array.AsReadOnly(new[] { _taskRequest, _scheduleRequest }.OfType<string>().ToArray());
    public bool ReviewDeliveryConfirmed => _submissionCompleted;
    internal async Task SubmitOnceAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_submissionAttempted) return;
            _submissionAttempted = true;
            try
            {
                await _requireOriginal(token).ConfigureAwait(false);
                var first = _pair.ReusableChange;
                _taskRequest = (await _home.AuthorizeForActorAsync(first.OriginalActor, AutomationDefinitionChange.TargetAppID,
                    first.ActionID, first.Scopes, first.Arguments, "Review this exact linked definition pair", null,
                    "automation-linked-owner", token).ConfigureAwait(false)).RequestId;
                await _requireOriginal(token).ConfigureAwait(false);
                var second = _pair.ScheduleChange;
                _scheduleRequest = (await _home.AuthorizeForActorAsync(second.OriginalActor, AutomationDefinitionChange.TargetAppID,
                    second.ActionID, second.Scopes, second.Arguments, "Review this same exact linked definition pair", null,
                    "automation-linked-owner", token).ConfigureAwait(false)).RequestId;
                _submissionCompleted = true;
            }
            catch (Exception) { /* Retain original known IDs and ambiguous submission; never submit again. */ }
        }
        finally { _gate.Release(); }
    }
    public async Task<AutomationLinkedDefinitionOutcome> FinishAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_executed) return await _execution.FinishAsync(token).ConfigureAwait(false);
            if (!_submissionCompleted || _taskRequest is null || _scheduleRequest is null)
                return new(null, "LinkedReviewDeliveryUnconfirmed");
            if (_beginAttempted) return await _execution.FinishAsync(token).ConfigureAwait(false);
            await _requireOriginal(token).ConfigureAwait(false);
            var taskDecision = await _permissions.GetAuthorizationAsync(_taskRequest, token).ConfigureAwait(false);
            var scheduleDecision = await _permissions.GetAuthorizationAsync(_scheduleRequest, token).ConfigureAwait(false);
            if (!taskDecision.IsAllowed || !scheduleDecision.IsAllowed) return new(null, "LinkedReviewPendingOrDeclined");
            await _requireOriginal(token).ConfigureAwait(false);
            _beginAttempted = true; // Never repeat either ambiguous Begin transport.
            try
            {
                var taskCapability = await _home.BeginExecutionCapabilityAsync(_taskRequest,
                    _pair.ReusableChange.Arguments, token).ConfigureAwait(false);
                if (taskCapability is null) return new(null, "LinkedBeginOutcomeUnconfirmed");
                _execution.RetainTaskCapability(taskCapability); // Before the next actor/Begin await.
                await _requireOriginal(token).ConfigureAwait(false);
                var scheduleCapability = await _home.BeginExecutionCapabilityAsync(_scheduleRequest,
                    _pair.ScheduleChange.Arguments, token).ConfigureAwait(false);
                if (scheduleCapability is null)
                    return await _execution.StopBeforeExecutionAsync(CancellationToken.None).ConfigureAwait(false);
                _execution.RetainScheduleCapability(scheduleCapability);
                _executed = true;
                return await _execution.ExecuteOnceAsync(token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The private attempt can only stop before execution or observe its reserved actual SQL attempt.
                _executed = true;
                return await _execution.StopBeforeExecutionAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
    }
}
