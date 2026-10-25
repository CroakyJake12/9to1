using System.Runtime.CompilerServices;
using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator
{
    // Same live invocation identity, never reconstructed from a public status or task id.
    // Kept sealed after a failed/unknown terminal write; inspection must resolve that write.
    private readonly ConditionalWeakTable<TaskRunInvocationCustody, EndPassAdmission> _endingOriginalPasses = new();
    private sealed class EndPassAdmission(ProviderExecutionContext observed, Task<TaskRunOriginalRunControlResult> driver)
    {
        internal readonly ProviderExecutionContext Observed = observed;
        internal readonly Task<TaskRunOriginalRunControlResult> Driver = driver;
        internal Task<TaskExecutionSnapshot>? TerminalWrite;
    }
    private bool IsOriginalPassEnding(TaskRunInvocationCustody original)
    { lock (_invocationAdmissionGate) return _endingOriginalPasses.TryGetValue(original, out _); }
    private void DemandOriginalPassMayResume(TaskRunInvocationCustody original)
    {
        if (IsOriginalPassEnding(original))
            throw new InvalidOperationException("This original pass has admitted its terminal End Pass operation; it cannot resume.");
    }
    private void DemandOriginalPassMayReceiveSteer(TaskExecutionSnapshot snapshot)
    {
        if (snapshot.State is TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled or TaskExecutionLifecycle.Failed)
            throw new InvalidOperationException("A terminal pass cannot be restarted by Steer or queue promotion.");
        if (TryGetOriginalRunControlSource(snapshot) is { } original) DemandOriginalPassMayResume(original);
    }
    private bool HasPendingOriginalContinuation(TaskRunInvocationCustody original, Guid taskId) =>
        _unstartedContinuations.TryGetValue(taskId, out var unstarted) && ReferenceEquals(unstarted.Original, original) ||
        _toolCheckpointContinuations.TryGetValue(taskId, out var checkpoint) && ReferenceEquals(checkpoint.Original, original);

    private bool HasAcknowledgedOriginalPause(TaskRunInvocationCustody original)
    {
        lock (_runControlGate) return _originalRunControls.TryGetValue(original, out var controls) &&
            controls.Requests.TryGetValue(TaskRunOriginalRunControlKind.Pause, out var pause) &&
            ReferenceEquals(pause.Original, original) && ReferenceEquals(pause.Producer, original.OriginalProcessProducer) &&
            pause.Driver.IsCompletedSuccessfully && pause.Driver.Result.Disposition == TaskRunOriginalRunControlDisposition.Suspended &&
            controls.Requests.Values.All(request => request.Driver.IsCompletedSuccessfully);
    }
    private bool HasOriginalEndPassBoundary(TaskRunInvocationCustody original, TaskExecutionSnapshot current,
        out HashSet<Guid> cancelledNodes)
    {
        cancelledNodes = [];
        if (IsOriginalPassEnding(original) || !HasAcknowledgedOriginalPause(original) ||
            current.State != TaskExecutionLifecycle.Suspended || current.ParentDelegation is not null ||
            original.OriginalProcessProducer is not { HasHealthyClosedOriginal: true } || !original.OwnedCleanupTerminal ||
            !original.ResourcesDisposed || original.OriginalDispose is not { IsCompletedSuccessfully: true } ||
            original.OriginalTracker is not null && original.OriginalTrackerDispose is not { IsCompletedSuccessfully: true } ||
            original.OriginalTerminalObservation is not { State: TaskExecutionLifecycle.Suspended, RecoveryObservation: { } terminalRecovery } terminal ||
            current.RecoveryObservation is not { } recovery || recovery.ObservationId != original.ObservationId ||
            terminalRecovery.ObservationId != recovery.ObservationId || terminal.OwnerBinding != current.OwnerBinding ||
            terminal.TaskId != current.TaskId || terminal.ExecutionId != current.ExecutionId || terminal.ContextId != current.ContextId ||
            current.Attempts.Any(attempt => attempt.State is TaskRunAttemptState.Admitted or TaskRunAttemptState.Running) ||
            terminal.Plan.Count != current.Plan.Count || terminal.Plan.Where((node, index) =>
                !SameOriginalToolCheckpointNode(node, current.Plan[index])).Any()) return false;
        lock (_invocationAdmissionGate)
            if (HasPendingOriginalContinuation(original, current.TaskId)) return false;
        if (current.Attempts.Count == 0)
        {
            if (original.AttemptAdmissionInvoked || original.OriginalProviderInvocationInvoked ||
                recovery.SettlementOutcome != TaskRunOriginalSettlementOutcome.NoAttemptAdmissionWasInvoked) return false;
        }
        else if (recovery.SettlementOutcome != TaskRunOriginalSettlementOutcome.Joined ||
            original.OriginalSettlement is not { IsCompletedSuccessfully: true }) return false;
        // A child or an unknown accepted tool cannot be ended by a presentation command.
        if (current.Delegations.Any(intent => intent.State != TaskRunDelegationState.ChildCompleted)) return false;
        try { RequireAcknowledgedDelegationCompletions(current); }
        catch (InvalidOperationException) { return false; }
        var responses = original.CaptureOriginalResponses();
        var tools = original.CaptureOriginalToolOutcomes();
        foreach (var node in current.Plan)
        {
            if (node.State is TaskPlanNodeState.Cancelled or TaskPlanNodeState.Superseded) continue;
            if (node.OriginalToolIntent is not null)
            {
                var actual = tools.SingleOrDefault(outcome => outcome.OriginalNode.ActionId == node.ActionId);
                if (actual is null || !HasSuccessfulOriginalToolCheckpointOutcome(actual) ||
                    !SameOriginalToolCheckpointNode(actual.OriginalNode, node)) return false;
                continue;
            }
            if (HasSameSuccessfulResponseNode(original, node)) continue;
            var response = responses.SingleOrDefault(actual => actual.ActionId == node.ActionId);
            if (response is not null)
            {
                lock (response.Gate)
                {
                    if (!ReferenceEquals(response.Custody, original) ||
                        response.OriginalTerminal is not { IsCompletedSuccessfully: true } ||
                        response.Bindings.Count == 0 || response.Bindings.Any(binding =>
                            !binding.Registration.IsCompletedSuccessfully || binding.Sources.Any(source => !source.IsCompletedSuccessfully)) ||
                        response.ActualToolCall is { } call && (!call.IsCompleted || call.IsFaulted) ||
                        response.Moves.Any(move => !move.IsCompleted || move.IsFaulted) ||
                        response.Moves.Count != 0 && response.ActualStreamDispose is not { IsCompletedSuccessfully: true } ||
                        !HasSameRunningResponseNode(response, node)) return false;
                }
                cancelledNodes.Add(node.ActionId); // Closed unfinished response, never labelled successful.
                continue;
            }
            if (node.State != TaskPlanNodeState.Pending || node.Acceptance is not null) return false;
            cancelledNodes.Add(node.ActionId); // Still-uninvoked plan; its content/history is conserved.
        }
        return true;
    }

    /// <summary>End the SAME acknowledged paused pass after actual producer/tool settlement.
    /// This terminal transition consumes no draft or queued content and starts no follow-up.</summary>
    public Task<TaskRunOriginalRunControlResult> EndPausedOriginalRunAsync(
        ProviderExecutionContext observed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observed);
        CanonicalChatProcessProducer.DemandExternalOwnerJoin(this);
        TaskRunProcessProducerContext.DemandExternalJoin(this);
        return StartOriginalProcessStage("end-paused-original-run", cancellationToken,
            token => EndPausedOriginalRunBodyAsync(observed, token));
    }
    private async Task<TaskRunOriginalRunControlResult> EndPausedOriginalRunBodyAsync(
        ProviderExecutionContext observed, CancellationToken token)
    {
        var stage = RequireOriginalProcessStage();
        var current = await RequireOriginalProcessSnapshotAsync(observed.TaskId, token).ConfigureAwait(false);
        RequireRun(current, observed.ExecutionId);
        await ValidateOriginalProcessCommandAsync(current, "task:end-paused-original", token).ConfigureAwait(false);
        var original = TryGetOriginalRunControlSource(current)
            ?? throw new InvalidOperationException("The SAME original paused pass is unavailable.");
        EndPassAdmission? prior;
        lock (_invocationAdmissionGate) _endingOriginalPasses.TryGetValue(original, out prior);
        if (prior is not null)
        {
            if (prior.Observed != observed) throw new InvalidOperationException("Another revision owns the original End Pass operation.");
            return await stage.Await(() => prior.Driver, owningCleanup: true).ConfigureAwait(false);
        }
        if (OriginalRunControlContext(current) != observed || !HasOriginalEndPassBoundary(original, current, out var cancelledNodes))
            throw new InvalidOperationException("The same paused pass changed, is still settling, or requires inspection before End Pass.");
        var producer = original.OriginalProcessProducer!;
        producer.DemandExternalOriginalJoin();
        EndPassAdmission admitted;
        lock (_runControlGate)
        lock (_invocationAdmissionGate)
        {
            if (_endingOriginalPasses.TryGetValue(original, out _) ||
                !ReferenceEquals(TryGetOriginalRunControlSource(current), original) ||
                HasPendingOriginalContinuation(original, current.TaskId) || !HasAcknowledgedOriginalPause(original))
                throw new InvalidOperationException("The original paused pass admitted another control or continuation.");
            admitted = new(observed, stage.ActualDriver as Task<TaskRunOriginalRunControlResult>
                ?? throw new InvalidOperationException("The actual End Pass driver was not published."));
            _endingOriginalPasses.Add(original, admitted); // Atomically excludes private continuation admission/claim.
        }
        stage.OriginalResultClosed = () => admitted.TerminalWrite is null or { IsCompletedSuccessfully: true };
        stage.OriginalResultJoin = () => (Task?)admitted.TerminalWrite ?? Task.CompletedTask; // No write was acquired on a prewrite refusal.
        // Join the exact already requested cleanup independently before the final fresh actor/CAS.
        await stage.Await(producer.CloseAndSuspendOriginalRunProducerAsync, owningCleanup: true).ConfigureAwait(false);
        var refreshed = await stage.Await(() => repository.GetAsync(observed.TaskId, CancellationToken.None), owningCleanup: true).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original paused task is unavailable before terminal acknowledgement.");
        if (OriginalRunControlContext(refreshed) != observed || !ReferenceEquals(TryGetOriginalRunControlSource(refreshed), original))
            throw new InvalidOperationException("The original paused revision changed before its terminal write.");
        await stage.Await(() => ValidateTaskCommandAsync(refreshed, "task:end-paused-original", CancellationToken.None), owningCleanup: true).ConfigureAwait(false);
        admitted.TerminalWrite = stage.Invoke(() => PersistAsync(refreshed with
        {
            State = TaskExecutionLifecycle.Cancelled,
            RecoveryHistory = refreshed.RecoveryHistory.Append(refreshed.RecoveryObservation!).ToArray(),
            RecoveryObservation = refreshed.RecoveryObservation! with { ReasonCode = "ORIGINAL_PASS_ENDED_BY_USER" },
            Plan = refreshed.Plan.Select(node => cancelledNodes.Contains(node.ActionId)
                ? node with { State = TaskPlanNodeState.Cancelled } : node).ToArray(),
            UpdatedAt = _time.GetUtcNow()
        }, CancellationToken.None, owningProcessCleanup: true), owningCleanup: true);
        var acknowledged = await stage.Await(() => admitted.TerminalWrite
            ?? throw new InvalidOperationException("The actual End Pass write was not returned."), owningCleanup: true).ConfigureAwait(false);
        original.OriginalTerminalObservation = acknowledged;
        return new(TaskRunOriginalRunControlKind.EndPass, TaskRunOriginalRunControlDisposition.EndedAfterOriginalPause,
            OriginalRunControlContext(acknowledged), acknowledged.State, false, false);
    }
}
