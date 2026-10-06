using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator
{
    private async Task PrepareOriginalColdToolContinuationAsync(TaskRunColdContinuationBinding binding,
        AgentRuntimeOriginalCustody operation, CanonicalContinuationProcessCustody outer, CancellationToken token)
    {
        var capsule = binding.Entry.Capsule;
        if (capsule.OriginalToolCheckpoint is null) return;
        var sources = new TaskRunColdOriginalSourceScope(operation);
        var journal = _coldRecoveryJournal!;
        var current = await TaskRunColdSourceIo.Read(sources,
            () => repository.GetAsync(capsule.AcknowledgedTask.TaskId, token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The actual restored Task disappeared before local selection.");
        await TaskRunColdSourceIo.Read(sources, () => journal.ValidateOriginalRestoredInputAsync(
            binding.Acknowledgment, current, sources, token)).ConfigureAwait(false);
        var selector = binding.Chat.RequireOriginalColdToolSelectionSource();
        Action<Action> callback = actual => outer.Invoke(() => { actual(); return true; });
        var selection = await TaskRunColdSourceIo.Read(sources, () => selector.SelectLocalForColdOriginalToolCheckpointAsync(
            binding.Acknowledgment, current, sources, callback, operation.RetainSource, token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No genuine configured capable local route can continue this authenticated boundary.");
        var rawSources = outer.Invoke(() => selection.OriginalSelectionSources.Append(selection.OriginalSelectionTask)
            .Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray());
        var failures = new List<Exception>();
        foreach (var actual in rawSources)
        {
            operation.RetainSource(actual);
            try { await actual.ConfigureAwait(false); }
            catch (Exception cause) { failures.Add(actual.Exception ?? cause); }
        }
        if (failures.Count != 0) throw new AggregateException("Every actual cold local-selection source must close.", failures);
        var latest = await TaskRunColdSourceIo.Read(sources,
            () => repository.GetAsync(current.TaskId, token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The actual restored Task disappeared during selection.");
        if (!SameOriginalCheckpointRow(current, latest) || !outer.Invoke(() => selector.IsIssuedOriginalColdToolCheckpointSelection(
                selection, binding.Acknowledgment, current)))
            throw new InvalidOperationException("The SAME private fresh selection or current restored Task changed.");
        await TaskRunColdSourceIo.Read(sources, () => journal.ValidateOriginalRestoredInputAsync(
            binding.Acknowledgment, latest, sources, token)).ConfigureAwait(false);
        await TaskRunColdSourceIo.Read(sources, () => ValidateTaskCommandAsync(latest, "task.cold-tool-selection", token)).ConfigureAwait(false);
        var recovery = latest.RecoveryObservation
            ?? throw new InvalidOperationException("The authenticated failed history is unavailable.");
        // Preserve the exact original observation in history. No old Task status or
        // failed attempt is relabeled successful, and the response stays Running.
        binding.Selection = selection;
        var selectedCandidate = outer.Invoke(() => selection.ActualSelectedCandidate);
        var recoveryAcknowledgment = await TaskRunColdSourceIo.Read(sources, () =>
            outer.Invoke(() => binding.OriginalRecoveryWrite = PersistOriginalWriteOnlyAsync(latest with
            {
                RecoveryObservation = null,
                RecoveryHistory = Array.AsReadOnly(latest.RecoveryHistory.Append(recovery).ToArray()),
                UpdatedAt = _time.GetUtcNow()
            }, token, operation.RetainSource))).ConfigureAwait(false);
        binding.PreparedTask = recoveryAcknowledgment;
        var admission = await operation.AwaitAsync(() => StartOriginalProcessStage("admit-cold-original-tool-attempt", token,
            cancellation => AdmitAttemptProcessBodyAsync(latest.TaskId, latest.ExecutionId,
                latest.Attempts.Last().Id, selectedCandidate, cancellation, binding))).ConfigureAwait(false);
        binding.NewAdmission = admission;
        binding.PreparedTask = admission.Snapshot;
    }

    private async Task DemandOriginalColdHistoricalSettlementAsync(TaskRunColdContinuationBinding binding,
        TaskExecutionSnapshot actual, Guid originalAttemptId, TaskRunProcessStageCustody stage, CancellationToken token)
    {
        lock (_processProducerGate)
            if (!ReferenceEquals(binding.Owner, this) || binding.BodyBound
                || !_coldContinuationBindings.TryGetValue(binding.Invocation, out var issued) || !ReferenceEquals(issued, binding)
                || binding.PreparedTask is null || !SameOriginalCheckpointRow(binding.PreparedTask, actual)
                || actual.Attempts.LastOrDefault()?.Id != originalAttemptId || binding.NewAdmission is not null)
                throw new UnauthorizedAccessException("No SAME private fresh cold boundary owns this historical settlement substitution.");
        var operation = new AgentRuntimeOriginalCustody { OriginalProcessOwner = this };
        var sources = new TaskRunColdOriginalSourceScope(operation);
        var validation = operation.Start(async work =>
        {
            await TaskRunColdSourceIo.Read(sources, () => _coldRecoveryJournal!.ValidateOriginalRestoredInputAsync(
                binding.Acknowledgment, actual, sources, token)).ConfigureAwait(false);
            return true;
        });
        stage.RetainSource(validation);
        await stage.Await(() => validation).ConfigureAwait(false);
        // This is fresh journal provenance for a settled historical boundary, not an
        // absent registry lookup, recreated old lease or synthetic settlement Task.
        TaskRunColdRecoveryBoundary.DemandRestorableBoundary(binding.Entry.Capsule,
            binding.Entry.Capsule.AcknowledgedTask);
    }

    internal Task<TaskExecutionSnapshot> BindOriginalColdToolBodyAsync(TaskRunColdContinuationBinding binding,
        ChatSessionService chat, Guid contextId, CancellationToken token) => StartOriginalProcessStage("bind-cold-original-tool-body", token,
            cancellation => BindOriginalColdToolBodyStageAsync(binding, chat, contextId, cancellation));

    private async Task<TaskExecutionSnapshot> BindOriginalColdToolBodyStageAsync(TaskRunColdContinuationBinding binding,
        ChatSessionService chat, Guid contextId, CancellationToken token)
    {
        var stage = RequireOriginalProcessStage();
        lock (_processProducerGate)
            if (!ReferenceEquals(binding.Owner, this) || !ReferenceEquals(binding.Chat, chat) || binding.BodyBound
                || binding.NewAdmission is null || binding.Selection is null || binding.PreparedTask is null
                || !_coldContinuationBindings.TryGetValue(binding.Invocation, out var issued) || !ReferenceEquals(issued, binding))
                throw new UnauthorizedAccessException("The genuine fresh cold tool factory is unavailable or already consumed.");
        var current = await stage.Await(() => repository.GetAsync(binding.PreparedTask.TaskId, token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The actual fresh cold Task disappeared before body admission.");
        if (!SameOriginalCheckpointRow(current, binding.PreparedTask) || current.ContextId != contextId
            || current.Attempts.LastOrDefault()?.Id != binding.NewAdmission.AttemptId)
            throw new InvalidOperationException("The actual fresh same-run admission changed before body entry.");
        var operation = new AgentRuntimeOriginalCustody { OriginalProcessOwner = this };
        var validation = operation.Start(async work =>
        {
            var sources = new TaskRunColdOriginalSourceScope(work);
            await TaskRunColdSourceIo.Read(sources, () => _coldRecoveryJournal!.ValidateOriginalRestoredInputAsync(
                binding.Acknowledgment, current, sources, token)).ConfigureAwait(false);
            return true;
        });
        stage.RetainSource(validation); await stage.Await(() => validation).ConfigureAwait(false);
        var latest = await stage.Await(() => repository.GetAsync(current.TaskId, token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The same-run Task disappeared during input validation.");
        if (!SameOriginalCheckpointRow(current, latest)) throw new InvalidOperationException("The fresh body row changed during input reads.");
        await stage.Await(() => ValidateTaskCommandAsync(latest, "task.cold-tool-body", token)).ConfigureAwait(false);
        ReserveOriginalInvocation(binding.Invocation); BindOriginalInvocation(binding.Invocation, latest);
        lock (_processProducerGate) binding.BodyBound = true;
        return latest;
    }


}

public sealed partial class TaskExecutionCoordinator
{
    internal void DemandOriginalColdToolFactory(TaskRunColdContinuationBinding binding, ChatSessionService chat)
    {
        lock (_processProducerGate)
            if (_processProducerAdmissionSealed || !ReferenceEquals(binding.Owner, this)
                || !ReferenceEquals(binding.Chat, chat) || binding.BodyBound
                || binding.Selection is null || binding.NewAdmission is null || binding.PreparedTask is null
                || !_coldContinuationBindings.TryGetValue(binding.Invocation, out var issued) || !ReferenceEquals(issued, binding))
                throw new UnauthorizedAccessException("No SAME fresh private cold tool factory is available.");
        if (_coldRecoveryJournal is null || !_coldRecoveryJournal.IsIssuedOriginalAcknowledgment(binding.Acknowledgment, binding.Claim))
            throw new UnauthorizedAccessException("No genuine protected journal acknowledgment owns this factory.");
    }

    internal TaskRunOriginalResponseOperation ReserveOriginalColdResponse(TaskRunColdContinuationBinding binding,
        ChatSessionService chat)
    {
        RequireOriginalInvocation(binding.Invocation);
        var producer = binding.Invocation.OriginalProcessProducer
            ?? throw new InvalidOperationException("No actual fresh cold producer owns the response.");
        TaskRunOriginalResponseOperation? result = null;
        producer.InvokeAdmittedOriginalCallback(() =>
        {
            if (!ReferenceEquals(binding.Owner, this) || !ReferenceEquals(binding.Chat, chat)
                || !ReferenceEquals(binding.Invocation.OriginalChatOwner, chat) || !binding.BodyBound
                || _coldRecoveryJournal is null || !_coldRecoveryJournal.IsIssuedOriginalAcknowledgment(binding.Acknowledgment, binding.Claim))
                throw new UnauthorizedAccessException("Only the fresh private cold body may reserve its conserved response.");
            var material = binding.Entry.Capsule.OriginalToolCheckpoint
                ?? throw new InvalidOperationException("The authenticated unfinished response material is unavailable.");
            var node = binding.Entry.Capsule.AcknowledgedTask.Plan.Single(value => value.ActionId == material.OriginalUnfinishedResponseActionId);
            lock (_processProducerGate)
            {
                if (binding.OriginalResponseReserved || !_coldContinuationBindings.TryGetValue(binding.Invocation, out var issued)
                    || !ReferenceEquals(issued, binding))
                    throw new InvalidOperationException("The authentic unfinished response was already reserved.");
                result = new(this, binding.Invocation, node.ActionId, node.ParentActionId, prior: null) { ColdPrior = binding };
                binding.Invocation.RetainOriginalResponse(result);
                binding.OriginalResponseReserved = true;
            }
        });
        return result!; // Reservation carries identity only; actual route bind still owns the CAS/permission.
    }

    private bool HasOriginalColdUnfinishedResponseNode(TaskRunColdContinuationBinding binding,
        TaskRunOriginalResponseOperation response, TaskPlanNode current)
    {
        if (!ReferenceEquals(binding.Owner, this) || !ReferenceEquals(response.Custody, binding.Invocation)
            || !binding.BodyBound || !binding.OriginalResponseReserved || _coldRecoveryJournal is null
            || !_coldRecoveryJournal.IsIssuedOriginalAcknowledgment(binding.Acknowledgment, binding.Claim)
            || !_coldContinuationBindings.TryGetValue(binding.Invocation, out var actual) || !ReferenceEquals(actual, binding)) return false;
        var original = binding.Entry.Capsule.AcknowledgedTask.Plan.SingleOrDefault(value =>
            value.ActionId == binding.Entry.Capsule.OriginalToolCheckpoint!.OriginalUnfinishedResponseActionId);
        return original is not null && original.ActionId == response.ActionId
            && current.State == TaskPlanNodeState.Running && SameOriginalToolCheckpointNode(original, current);
    }

    internal IReadOnlyList<TaskPlanNode> CaptureAuthenticatedColdAcceptedNodes(TaskRunColdContinuationBinding binding)
    {
        if (!ReferenceEquals(binding.Owner, this) || !binding.BodyBound
            || !ReferenceEquals(binding.Invocation.OriginalColdContinuation, binding)
            || _coldRecoveryJournal is null || !_coldRecoveryJournal.IsIssuedOriginalAcknowledgment(binding.Acknowledgment, binding.Claim))
            throw new UnauthorizedAccessException("No fresh genuine journal binding owns the accepted operations.");
        return Array.AsReadOnly(binding.Entry.Capsule.OriginalToolCheckpoint!.OriginalClosedActions
            .Where(value => value.Kind == TaskRunColdClosedActionKind.OriginalOwnedTool)
            .Select(value => value.OriginalNode).ToArray());
    }
}
