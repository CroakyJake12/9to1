using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator
{
    /// <summary>Records only the actual owning preparation/result. An accepted operation is
    /// conserved even when its observed process exit or business outcome was unsuccessful.</summary>
    public Task<TaskExecutionSnapshot> RecordOriginalToolActionOutcomeAsync(
        ITaskRunToolActionPreparation originalPreparation, TaskRunToolActionResult originalResult,
        CancellationToken cancellationToken) =>
        StartOriginalProcessStage("record-original-tool-operation-outcome", cancellationToken, token =>
            RecordOriginalToolActionOutcomeBodyAsync(originalPreparation, originalResult, token));

    private async Task<TaskExecutionSnapshot> RecordOriginalToolActionOutcomeBodyAsync(
        ITaskRunToolActionPreparation preparation, TaskRunToolActionResult result, CancellationToken token)
    {
        var stage = RequireOriginalProcessStage();
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(result);
        var owner = _toolActionOwner ?? throw new InvalidOperationException("The actual tool-action owner is unavailable.");
        // The public result record, its activity status and ProcessResult alone establish nothing.
        await stage.Await(() => owner.ValidateOriginalResultAsync(preparation, result, token).AsTask()).ConfigureAwait(false);
        var attempt = stage.Invoke(() => preparation.OriginalAttempt);
        var claim = stage.Invoke(() => RequireOriginalActionAdmission(preparation, attempt));
        await stage.Await(() => ValidateOriginalActionAdmissionAsync(claim, preparation, attempt, token)).ConfigureAwait(false);
        var current = await RequireOriginalProcessSnapshotAsync(attempt.Snapshot.TaskId, token).ConfigureAwait(false);
        RequireOriginalToolOutcomeBasis(current, preparation, attempt);
        var receipt = result.OwnerReceiptReference;
        var accepted = preparation.InterruptionPolicy != TaskActionInterruptionPolicy.ReadOnlyCancellable
            && !string.IsNullOrWhiteSpace(receipt);
        if (accepted)
            await stage.Await(() => RequireAuthority().ValidateAcceptedActionAsync(current, attempt.AttemptId,
                preparation.ActionId, receipt!, token)).ConfigureAwait(false);
        // Reobserve the real row after awaited owner/receipt authority; the final actor check
        // follows that read. The pure same-source action fence also sees acknowledged steer CAS.
        var final = await RequireOriginalProcessSnapshotAsync(current.TaskId, token).ConfigureAwait(false);
        var node = RequireOriginalToolOutcomeBasis(final, preparation, attempt);
        await ValidateOriginalProcessCommandAsync(final, "task:record-original-tool-outcome", token).ConfigureAwait(false);
        stage.Invoke(() => { DemandOriginalActionAdmission(claim, preparation, attempt); return true; });
        var readComplete = preparation.InterruptionPolicy == TaskActionInterruptionPolicy.ReadOnlyCancellable
            && result.ReadOnlyObservationComplete;
        var completed = accepted || readComplete;
        var process = result.OriginalResult.OriginalProcessResult;
        var operation = new TaskActionOperationOutcome(
            completed ? true : result.KnownNoEffect ? false : null,
            process is not null ? !process.TimedOut && process.ExitCode == 0
                : completed ? result.OriginalResult.Activity.Succeeded : null,
            process?.ExitCode, process?.TimedOut, _time.GetUtcNow());
        var terminal = node with
        {
            State = completed ? TaskPlanNodeState.Completed
                : result.KnownNoEffect ? TaskPlanNodeState.Failed : TaskPlanNodeState.RequiresReexecution,
            Acceptance = accepted ? new TaskActionAcceptance(attempt.AttemptId, receipt!, _time.GetUtcNow()) : null,
            OriginalOperationOutcome = operation
        };
        // Detach the exact locally proposed node BEFORE the CAS can publish mutable
        // public collection observations to an observer.
        var originalOutcomeNode = terminal with
        { RequiredPermissionScopes = terminal.RequiredPermissionScopes is null ? null
            : Array.AsReadOnly(terminal.RequiredPermissionScopes.ToArray()) };
        var acknowledged = await stage.Await(() => PersistAsync(final with
        {
            Plan = final.Plan.Select(item => item.ActionId == terminal.ActionId ? terminal : item).ToArray(),
            LastCheckpointActionId = accepted ? terminal.ActionId : final.LastCheckpointActionId,
            State = completed ? final.State : TaskExecutionLifecycle.Suspended,
            UpdatedAt = _time.GetUtcNow()
        }, token)).ConfigureAwait(false);
        // Bind the actual result/write source, not a receipt reconstructed from this row.
        stage.Invoke(() =>
        {
            BindOriginalToolOutcomeCustody(preparation, result, claim, stage, acknowledged, originalOutcomeNode);
            return true;
        }, owningCleanup: true);
        if (accepted)
            stage.Invoke(() =>
            {
                ObserveEvent(acknowledged, new ExecutionEvent(Guid.NewGuid(), acknowledged.ExecutionId,
                    terminal.ActionId, terminal.ParentActionId, ExecutionOrigin.Haven, ExecutionActionType.ToolResult,
                    ExecutionActionStatus.Completed, operation.BusinessSucceeded == false
                        ? "Owning service accepted operation; observed outcome unsuccessful" : "Owning service accepted operation",
                    null, null, "task-coordination", _time.GetUtcNow(), TaskId: acknowledged.TaskId,
                    SafeMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["ownerAccepted"] = "true",
                        ["attemptId"] = attempt.AttemptId.ToString(),
                        ["businessSucceeded"] = operation.BusinessSucceeded?.ToString().ToLowerInvariant() ?? "unknown",
                        ["processExitCode"] = process?.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown",
                        ["persistenceRevision"] = acknowledged.PersistenceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }));
                return true;
            }, owningCleanup: true);
        return acknowledged;
    }

    private TaskPlanNode RequireOriginalToolOutcomeBasis(TaskExecutionSnapshot current,
        ITaskRunToolActionPreparation preparation, TaskRunAttemptAdmission attempt)
    {
        RequireRun(current, attempt.Snapshot.ExecutionId);
        RequireCurrentAttempt(current, attempt.AttemptId);
        if (current.ContextId != attempt.Snapshot.ContextId || current.OwnerBinding != attempt.Lease.Owner
            || !_issuedAdmissions.TryGetValue(attempt.AttemptId, out var issued) || !ReferenceEquals(issued, attempt))
            throw new InvalidOperationException("The owning result no longer has its SAME actual issued task/run/attempt.");
        var node = current.Plan.SingleOrDefault(item => item.ActionId == preparation.ActionId)
            ?? throw new InvalidOperationException("No actual acknowledged action exists for the owning result.");
        if (node.State is not (TaskPlanNodeState.Running or TaskPlanNodeState.WaitingSafeBoundary)
            || node.Acceptance is not null || node.OriginalToolIntent != preparation.OriginalToolIntent
            || node.InterruptionPolicy != preparation.InterruptionPolicy)
            throw new InvalidOperationException("Accepted, superseded or uncertain work cannot be replaced or replayed by a result observation.");
        return node;
    }
}
