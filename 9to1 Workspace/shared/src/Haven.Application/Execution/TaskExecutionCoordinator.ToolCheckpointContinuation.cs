using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Private one-use transfer of the exact undelivered Chat tool-response step.
/// No public IDs, observed status or caller implementation can construct its issuance.</summary>
internal sealed class TaskRunToolCheckpointContinuationBinding(TaskExecutionCoordinator issuer,
    TaskRunInvocationCustody original, TaskRunInvocationCustody next, ChatOriginalToolCheckpointBoundary boundary)
{
    internal readonly TaskExecutionCoordinator Issuer = issuer;
    internal readonly TaskRunInvocationCustody Original = original;
    internal readonly TaskRunInvocationCustody Next = next;
    internal readonly ChatOriginalToolCheckpointBoundary Boundary = boundary;
    internal Task<TaskRunToolCheckpointContinuationBinding> Preparation = null!;
    internal TaskRunProcessStageCustody Stage = null!;
    internal TaskRunOriginalFinalRequestFailure? FinalFailure;
    internal TaskRunOriginalRequestFailure? SettledFailure;
    internal TaskRunOriginalToolResponseDispatchWitness? DispatchWitness;
    internal TaskRunOriginalToolCheckpointSelection? Selection;
    internal TaskExecutionSnapshot? PriorSuspended;
    internal Task<TaskExecutionSnapshot>? OriginalRecoveryWrite;
    internal TaskExecutionSnapshot? Acknowledged;
    internal Task<TaskRunAttemptAdmission>? OriginalResume;
    internal TaskRunAttemptAdmission? NewAdmission;
    internal readonly List<Task> ActualSources = [];
    internal bool Transferred;
    internal bool Claimed;
    internal bool Bound;
    internal TaskExecutionSnapshot? CompletedCandidate;
    internal Task<TaskExecutionSnapshot>? OriginalResolutionValidation;
    internal Task<TaskExecutionSnapshot>? OriginalResolutionDriver;
    internal HostedOriginalRunResume? OriginalHost;
    internal TaskRunResolvedToolCheckpointContinuation? Resolution;
}

internal sealed class TaskRunResolvedToolCheckpointContinuation(TaskRunToolCheckpointContinuationBinding binding,
    Task<TaskExecutionSnapshot> completion, TaskRunOriginalRecoveryCapture originalSources,
    TaskExecutionSnapshot acknowledged, Task actualBusinessDriver, Task actualResolutionValidation)
{
    internal readonly TaskRunToolCheckpointContinuationBinding Binding = binding;
    internal readonly Task<TaskExecutionSnapshot> Completion = completion;
    internal readonly TaskRunOriginalRecoveryCapture OriginalSources = originalSources;
    internal readonly TaskExecutionSnapshot Acknowledged = acknowledged;
    internal readonly Task ActualBusinessDriver = actualBusinessDriver;
    internal readonly Task ActualResolutionValidation = actualResolutionValidation;
}

internal sealed partial class TaskRunInvocationCustody
{
    private readonly List<Exception> _toolCheckpointPublicationRefusals = [];
    internal IReadOnlyList<Exception> OriginalToolCheckpointPublicationRefusals
    { get { lock (_gate) return _toolCheckpointPublicationRefusals.ToArray(); } }

    internal void DemandOriginalToolCheckpointPublicationCapacity()
    {
        lock (_gate)
            if (_toolCheckpointPublicationRefusals.Count >= TaskExecutionCoordinator.OriginalInvocationCapacity)
                throw new InvalidOperationException("Finite original checkpoint publication-refusal custody requires inspection.",
                    _toolCheckpointPublicationRefusals[^1]);
    }

    internal void RetainOriginalToolCheckpointPublicationRefusal(Exception actualRefusal)
    {
        lock (_gate) _toolCheckpointPublicationRefusals.Add(actualRefusal);
    }
}

public sealed partial class TaskExecutionCoordinator
{
    private readonly ConditionalWeakTable<ChatOriginalToolCheckpointBoundary, TaskRunToolCheckpointContinuationBinding> _issuedToolCheckpointContinuations = new();
    private readonly Dictionary<Guid, TaskRunToolCheckpointContinuationBinding> _toolCheckpointContinuations = [];

    internal Task<TaskRunToolCheckpointContinuationBinding> PrepareOriginalToolCheckpointContinuationAsync(
        TaskRunInvocationCustody sameOriginal, ChatSessionService sameChat, CancellationToken token)
    {
        CanonicalChatProcessProducer.DemandExternalOwnerJoin(this);
        TaskRunProcessProducerContext.DemandExternalJoin(this);
        token.ThrowIfCancellationRequested();
        RequireOriginalInvocation(sameOriginal);
        var boundary = sameOriginal.OriginalToolCheckpoint
            ?? throw new InvalidOperationException("No exact unfinished original tool-response step is retained.");
        if (!ReferenceEquals(boundary.Chat, sameChat) || !ReferenceEquals(boundary.Custody, sameOriginal)
            || sameOriginal.OriginalToolContinuationFactory is null)
            throw new InvalidOperationException("The original tool-input factory cannot be reconstructed from public Task IDs.");
        TaskRunToolCheckpointContinuationBinding binding;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_invocationAdmissionGate)
        {
            if (_toolCheckpointContinuations.TryGetValue(sameOriginal.OriginalBinding!.TaskId, out var prior))
            {
                if (!ReferenceEquals(prior.Original, sameOriginal) || !ReferenceEquals(prior.Boundary, boundary) || prior.Claimed)
                    throw new InvalidOperationException("Another or already consumed original checkpoint owns this task.");
                return prior.Preparation;
            }
            sameOriginal.DemandOriginalToolCheckpointPublicationCapacity();
            var next = CreateOriginalInvocationCustody();
            ReserveOriginalInvocation(next);
            binding = new(this, sameOriginal, next, boundary);
            try
            {
                binding.Preparation = StartOriginalProcessStage("task.prepare-original-tool-checkpoint", token,
                    stageToken => PrepareOriginalToolCheckpointBodyAsync(binding, sameChat, start.Task, stageToken), stage =>
                    {
                        binding.Stage = stage;
                        stage.OriginalResultClosed = () => binding.Resolution is not null;
                        stage.OriginalResultJoin = () => binding.Next.OriginalProcessProducer?.ActualClose
                            ?? binding.Next.OriginalProcessProducer?.CloseAndSuspendOriginalProducerAsync()
                            ?? throw new InvalidOperationException("An acknowledged checkpoint has no actually closed owning child.");
                    });
            }
            catch (Exception actualRefusal) when (binding.Stage is null)
            {
                // The source gate refused before publishing any driver or invoking any
                // body callback. Release only this never-started reservation. Keep the
                // exact synchronous refusal on the owning original without adding it to
                // the terminal provider's causes or manufacturing a completed Task.
                ReleaseHealthyOrUnstartedInvocation(next);
                sameOriginal.RetainOriginalToolCheckpointPublicationRefusal(actualRefusal);
                throw;
            }
            _toolCheckpointContinuations.Add(sameOriginal.OriginalBinding.TaskId, binding);
            _issuedToolCheckpointContinuations.Add(boundary, binding);
            sameOriginal.RetainAdditionalOriginal("checkpoint.preparation", binding.Preparation);
        }
        start.SetResult();
        return binding.Preparation;
    }

    private static bool SameOriginalCheckpointRow(TaskExecutionSnapshot original, TaskExecutionSnapshot current) =>
        JsonSerializer.Serialize(original) == JsonSerializer.Serialize(current);

    // Availability is only an observation. Preparation still performs real current actor,
    // local capability/route selection, permission, input, cleanup and expected-revision CAS.
    private bool HasOriginalToolCheckpointRunResume(TaskRunInvocationCustody original, TaskExecutionSnapshot current)
    {
        if (original.OriginalToolCheckpoint is not { } boundary || original.OriginalToolContinuationFactory is null
            || !original.OriginalProviderInvocationInvoked || original.OriginalInputCurrentness is null
            || original.OriginalChatOwner is not { } chat || current.State != TaskExecutionLifecycle.Suspended)
            return false;
        try { RequireOriginalToolCheckpointBoundary(original, boundary, current); }
        catch (InvalidOperationException) { return false; }
        (ITaskRunOriginalRequestFailureSource Failure, ITaskRunOriginalToolCheckpointSelectionSource Selection,
            ITaskRunOriginalToolResponseDispatchWitnessSource Dispatch) sources;
        try { sources = chat.RequireOriginalToolCheckpointSources(); }
        catch (InvalidOperationException) { return false; }
        return RequireOriginalProcessStage().Invoke(() =>
        {
            var outward = boundary.ActualOutwardFailure!;
            var failed = sources.Failure.TryGetOriginalFinalRequestFailure(boundary.OriginalRequest, outward);
            if (failed is null || !sources.Failure.IsIssuedOriginalFinalRequestFailure(failed, boundary.OriginalRequest, outward)) return false;
            var settled = sources.Failure.TryGetOriginalRequestFailure(boundary.OriginalRequest, failed.OriginalAdmission, outward);
            var dispatch = sources.Dispatch.TryGetOriginalToolResponseDispatchWitness(failed);
            return settled is not null && sources.Failure.IsIssuedOriginalRequestFailure(settled, boundary.OriginalRequest, failed.OriginalAdmission, outward)
                && dispatch is not null && sources.Dispatch.IsIssuedOriginalToolResponseDispatchWitness(dispatch, failed)
                && HasOriginalWholeCallNativeDispatchEvidence(dispatch, failed);
        });
    }

    private static bool HasOriginalWholeCallNativeDispatchEvidence(TaskRunOriginalToolResponseDispatchWitness witness,
        TaskRunOriginalFinalRequestFailure failure)
    {
        // The final method alone does not describe earlier in-call fallback invocations.
        // Every actual invocation needs the issuer's retained inspected-method and settled
        // frame/configuration evidence. Billing/network/remote effects stay Unknown.
        if (!ReferenceEquals(witness.OriginalFailure, failure)
            || witness.Scope != TaskRunOriginalToolResponseDispatchScope.NoIndependentHavenNativeDispatchInEveryInspectedInvocation
            || witness.OriginalInvocations.Count == 0) return false;
        foreach (var invocation in witness.OriginalInvocations)
        {
            var settled = invocation.OriginalFailedAttempt;
            if (invocation.Scope != TaskRunOriginalToolResponseDispatchScope.NoIndependentHavenNativeDispatchInInspectedMethod
                || !ReferenceEquals(invocation.OriginalProviderFrame, settled.OriginalFrame)
                || invocation.OriginalProviderTask is not { IsFaulted: true }
                || !invocation.OriginalProviderFrame.IsFaulted
                || settled.OriginalRegistration is not { IsCompletedSuccessfully: true }
                || settled.OriginalSettlement is not { IsCompletedSuccessfully: true }
                || settled.OriginalLeaseClose is not { IsCompletedSuccessfully: true }
                || settled.OriginalAdmission.Snapshot.TaskId != failure.OriginalAdmission.Snapshot.TaskId
                || settled.OriginalAdmission.Snapshot.ContextId != failure.OriginalAdmission.Snapshot.ContextId
                || settled.OriginalAdmission.Snapshot.ExecutionId != failure.OriginalAdmission.Snapshot.ExecutionId
                || settled.OriginalAdmission.Snapshot.OwnerBinding != failure.OriginalAdmission.Snapshot.OwnerBinding)
                return false;
        }
        var last = witness.OriginalInvocations[^1];
        return ReferenceEquals(last.OriginalProviderTask, witness.OriginalProviderTask)
            && ReferenceEquals(last.OriginalProviderFrame, witness.OriginalProviderFrame)
            && ReferenceEquals(last.OriginalFailedAttempt.OriginalObservation, failure.OriginalObservation)
            && ReferenceEquals(last.OriginalFailedAttempt.OriginalAdmission, failure.OriginalAdmission);
    }

    private void RequireOriginalToolCheckpointBoundary(TaskRunToolCheckpointContinuationBinding binding,
        TaskExecutionSnapshot current)
        => RequireOriginalToolCheckpointBoundary(binding.Original, binding.Boundary, current);

    private void RequireOriginalToolCheckpointBoundary(TaskRunInvocationCustody original,
        ChatOriginalToolCheckpointBoundary boundary, TaskExecutionSnapshot current)
    {
        var raw = boundary.ActualCall;
        var outward = boundary.ActualOutwardFailure;
        var captured = original.CaptureRecoveryOriginals();
        if (!ReferenceEquals(original.Issuer, this) || !ReferenceEquals(boundary.Custody, original)
            || !ReferenceEquals(original.OriginalChatOwner, boundary.Chat)
            || raw is not { IsFaulted: true } || outward is null || !boundary.IsExactFaultedUndeliveredCall(outward)
            || current.State != TaskExecutionLifecycle.Suspended || current.RecoveryObservation is not { } recovery
            || recovery.ObservationId != original.ObservationId
            || recovery.SettlementOutcome != TaskRunOriginalSettlementOutcome.Joined
            || current.RecoveryHistory.Count >= OriginalInvocationCapacity
            || !original.OwnedCleanupTerminal || !original.PublicationAcknowledged || original.ReachedEnd
            || original.OriginalProcessRetirementRequested || original.OriginalCompletion is not null
            || original.OriginalDispose is not { IsCompletedSuccessfully: true } || original.DisposeDirectFailure is not null
            || original.OriginalTracker is not null && original.OriginalTrackerDispose is not { IsCompletedSuccessfully: true }
            || original.TrackerDisposeDirectFailure is not null || !original.ResourcesDisposed
            || original.OriginalSettlement is not { IsCompletedSuccessfully: true }
            || original.OriginalProcessProducer is not { } outer
            || !outer.HasClosedOriginalWithExpectedProviderFailure(raw, outward)
            || boundary.CallsUsed >= boundary.ToolLimit
            || current.TaskId != original.OriginalBinding!.TaskId || current.ContextId != original.OriginalBinding.ContextId
            || current.ExecutionId != original.OriginalBinding.ExecutionId || current.OwnerBinding != original.OriginalBinding.OwnerBinding
            || ChatSessionService.OriginalToolCheckpointControlFingerprint(current) != boundary.OriginalControlFingerprint
            || current.ParentDelegation is not null // Saved child producer binding requires its owning Agent handoff.
            || current.Plan.Any(node => node.State is not (TaskPlanNodeState.Completed or TaskPlanNodeState.Superseded)
                && !HasSameFailedResponseBoundary(original, boundary, node))
            || current.Delegations.Any(intent => intent.State != TaskRunDelegationState.ChildCompleted))
            throw new InvalidOperationException("The actual unfinished tool step has no complete original cleanup/accepted-work boundary.");
        var known = new HashSet<Exception>(ReferenceEqualityComparer.Instance) { outward };
        void Keep(Exception cause)
        {
            known.Add(cause);
            if (cause is AggregateException group) foreach (var direct in group.InnerExceptions) Keep(direct);
        }
        foreach (var direct in raw.Exception!.InnerExceptions) Keep(direct);
        bool Expected(Exception cause) => known.Contains(cause)
            || cause is AggregateException { InnerExceptions.Count: > 0 } group && group.InnerExceptions.All(Expected);
        if (original.Causes.Count == 0 || original.Causes.Any(cause => !Expected(cause)))
            throw new InvalidOperationException("An unrelated original body/observer/cleanup failure prevents this checkpoint continuation.");
        foreach (var source in captured.Sources)
        {
            if (source.Stage == "checkpoint.preparation") continue; // SAME currently executing preparation, not terminal evidence.
            var actual = source.Actual;
            if (actual.IsCompletedSuccessfully) continue;
            var propagation = source.Stage.StartsWith("body.move:", StringComparison.Ordinal)
                || source.Stage is "provider.tools" or "provider.tools.capture" or "process.outer-move"
                    or "process.outer-raw-move" or "process.outer-raw-dispose" or "process.outer-dispose"
                    or "host.actual-business-driver";
            if (!propagation || !actual.IsFaulted || actual.Exception is not { InnerExceptions.Count: > 0 } fault
                || fault.InnerExceptions.Any(cause => !Expected(cause)))
                throw new InvalidOperationException("An original task is held, canceled or faulted outside the exact failed provider propagation.");
        }
        foreach (var node in current.Plan.Where(node => node.State == TaskPlanNodeState.Completed))
        {
            if (HasSameSuccessfulResponseNode(original, node)) continue;
            var outcome = boundary.ToolOutcomes.SingleOrDefault(value => value.OriginalNode.ActionId == node.ActionId);
            if (outcome is null || !HasSuccessfulOriginalToolCheckpointOutcome(outcome)
                || !SameOriginalToolCheckpointNode(outcome.OriginalNode, node))
                throw new InvalidOperationException("Accepted graph work lacks its SAME original outcome and successful owner-close evidence.");
        }
        RequireAcknowledgedDelegationCompletions(current);
    }

    private async Task<TaskRunToolCheckpointContinuationBinding> PrepareOriginalToolCheckpointBodyAsync(
        TaskRunToolCheckpointContinuationBinding binding, ChatSessionService sameChat, Task start, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        var stage = RequireOriginalProcessStage();
        var original = binding.Original;
        var boundary = binding.Boundary;
        async Task<T> Read<T>(string name, Func<Task<T>> source, bool cleanup = false)
        {
            Task<T>? actual = null;
            try
            {
                actual = stage.Invoke(source, cleanup);
                stage.RetainSource(actual);
                binding.ActualSources.Add(actual);
                original.RetainAdditionalOriginal(name, actual);
                return await actual.ConfigureAwait(false);
            }
            catch (Exception cause)
            {
                if (binding.OriginalRecoveryWrite is not null) original.Retain(cause, actual);
                if (actual is { IsFaulted: true, Exception: { } fault }) throw fault;
                throw;
            }
        }
        try
        {
            var current = await Read("checkpoint.initial-current", () => RequireOriginalProcessSnapshotAsync(original.OriginalBinding!.TaskId, token)).ConfigureAwait(false);
            await stage.Await(() => ValidateOriginalProcessCommandAsync(current, "task:continue-original-tool-checkpoint", token)).ConfigureAwait(false);
            RequireOriginalToolCheckpointBoundary(binding, current);
            var source = sameChat.RequireOriginalToolCheckpointSources();
            var outward = boundary.ActualOutwardFailure!;
            binding.FinalFailure = stage.Invoke(() => source.Failure.TryGetOriginalFinalRequestFailure(boundary.OriginalRequest, outward))
                ?? throw new InvalidOperationException("The SAME configured router has no authentic final failed request binding.");
            var final = binding.FinalFailure;
            if (!source.Failure.IsIssuedOriginalFinalRequestFailure(final, boundary.OriginalRequest, outward)
                || !ReferenceEquals(final.OriginalCallerRequest, boundary.OriginalRequest)
                || !ReferenceEquals(final.OriginalOutwardFailure, outward)
                || final.OriginalAdmission.Snapshot.TaskId != current.TaskId
                || final.OriginalAdmission.Snapshot.ContextId != current.ContextId
                || final.OriginalAdmission.Snapshot.ExecutionId != current.ExecutionId
                || final.OriginalAdmission.Snapshot.OwnerBinding != current.OwnerBinding
                || final.OriginalAdmission.AttemptId != current.Attempts.LastOrDefault()?.Id)
                throw new InvalidOperationException("The final provider failure is not the SAME actual task/run/last attempt.");
            binding.SettledFailure = stage.Invoke(() => source.Failure.TryGetOriginalRequestFailure(boundary.OriginalRequest,
                final.OriginalAdmission, outward))
                ?? throw new InvalidOperationException("The actual final frame, registration, settlement and lease close are unavailable.");
            if (!source.Failure.IsIssuedOriginalRequestFailure(binding.SettledFailure, boundary.OriginalRequest,
                final.OriginalAdmission, outward))
                throw new InvalidOperationException("A copied or unresolved failed-attempt receipt cannot prove settlement.");
            var settled = binding.SettledFailure.OriginalFailedAttempt;
            foreach (var actual in new[] { settled.OriginalRegistration, settled.OriginalSettlement, settled.OriginalLeaseClose })
            {
                stage.RetainSource(actual); binding.ActualSources.Add(actual);
                original.RetainAdditionalOriginal("checkpoint.final-settlement", actual);
                await stage.Await(() => actual, owningCleanup: true).ConfigureAwait(false);
            }
            binding.DispatchWitness = stage.Invoke(() => source.Dispatch.TryGetOriginalToolResponseDispatchWitness(final))
                ?? throw new InvalidOperationException("The exact provider method has no genuine native-dispatch witness.");
            if (!source.Dispatch.IsIssuedOriginalToolResponseDispatchWitness(binding.DispatchWitness, final)
                || !ReferenceEquals(binding.DispatchWitness.OriginalFailure, final)
                || !HasOriginalWholeCallNativeDispatchEvidence(binding.DispatchWitness, final))
                throw new InvalidOperationException("The provider method's own effect boundary is unknown.");
            await stage.Await(() => original.OriginalInputCurrentness!(token)).ConfigureAwait(false);
            current = await Read("checkpoint.selection-current", () => RequireOriginalProcessSnapshotAsync(current.TaskId, token)).ConfigureAwait(false);
            RequireOriginalToolCheckpointBoundary(binding, current);
            binding.Selection = await Read("checkpoint.local-selection", () =>
                source.Selection.SelectLocalForOriginalToolCheckpointAsync(final, current, token)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("No actual configured capable local provider can continue this request.");
            foreach (var actual in binding.Selection.OriginalSelectionSources.Append(binding.Selection.OriginalSelectionTask))
            {
                stage.RetainSource(actual); binding.ActualSources.Add(actual);
                original.RetainAdditionalOriginal("checkpoint.local-selection-source", actual);
                await stage.Await(() => actual, owningCleanup: true).ConfigureAwait(false);
            }
            var latest = await Read("checkpoint.final-current", () => RequireOriginalProcessSnapshotAsync(current.TaskId, token)).ConfigureAwait(false);
            if (!SameOriginalCheckpointRow(current, latest)
                || !source.Selection.IsIssuedOriginalToolCheckpointSelection(binding.Selection, final, latest))
                throw new InvalidOperationException("The actual task or genuine local selection changed before continuation admission.");
            await stage.Await(() => ValidateOriginalProcessCommandAsync(latest, "task:continue-original-tool-checkpoint", token)).ConfigureAwait(false);
            await stage.Await(() => ValidateOriginalToolCheckpointInputAsync(original, latest, token)).ConfigureAwait(false);
            RequireOriginalInvocation(original);
            RequireOriginalToolCheckpointBoundary(binding, latest);
            var recovery = latest.RecoveryObservation!;
            binding.PriorSuspended = latest;
            var proposed = latest with
            {
                RecoveryObservation = null,
                RecoveryHistory = Array.AsReadOnly(latest.RecoveryHistory.Append(recovery with
                    { Causes = Array.AsReadOnly(recovery.Causes.ToArray()) }).ToArray()),
                State = TaskExecutionLifecycle.Suspended, UpdatedAt = _time.GetUtcNow()
            };
            binding.OriginalRecoveryWrite = stage.Invoke(() => PersistOriginalWriteOnlyAsync(proposed, token,
                actual => { stage.RetainSource(actual); binding.ActualSources.Add(actual); original.RetainAdditionalOriginal("checkpoint.repository-cas", actual); }));
            stage.RetainSource(binding.OriginalRecoveryWrite); binding.ActualSources.Add(binding.OriginalRecoveryWrite);
            original.RetainAdditionalOriginal("checkpoint.recovery-cas", binding.OriginalRecoveryWrite);
            binding.Acknowledged = await binding.OriginalRecoveryWrite.ConfigureAwait(false);
            lock (_invocationAdmissionGate)
            {
                if (!_originalInvocations.TryUpdate(latest.TaskId, binding.Next, original))
                    throw new InvalidOperationException("The actual original changed after the continuation CAS.");
                binding.Transferred = true;
                var next = binding.Next;
                next.OriginalToolContinuation = binding;
                next.OriginalBinding = binding.Acknowledged;
                next.BoundByActualBegin = original.BoundByActualBegin;
                next.OriginalBegin = original.OriginalBegin;
                next.OriginalChatOwner = original.OriginalChatOwner;
                next.OriginalConversation = original.OriginalConversation;
                next.OriginalPersistedConversation = original.OriginalPersistedConversation;
                next.OriginalUserMessage = original.OriginalUserMessage;
                next.OriginalUserMessagePublished = original.OriginalUserMessagePublished;
                next.OriginalConversationWrite = original.OriginalConversationWrite;
                next.OriginalUserMessageWrite = original.OriginalUserMessageWrite;
                next.OriginalInputCurrentness = original.OriginalInputCurrentness;
                next.OriginalToolContinuationFactory = original.OriginalToolContinuationFactory;
                foreach (var outcome in boundary.ToolOutcomes) next.RetainOriginalToolOutcome(outcome);
                foreach (var outcome in original.CaptureOriginalResponseOutcomes()) next.BorrowOriginalResponseOutcome(binding, outcome);
            }
            PublishAcknowledgedSnapshot(binding.Acknowledged);
            // This is actual fresh issuer/lease/registration work, not selection metadata.
            binding.OriginalResume = stage.Invoke(() => ResumeAttemptAsync(latest.TaskId, latest.ExecutionId,
                final.OriginalAdmission.AttemptId, binding.Selection.ActualSelectedCandidate, token));
            stage.RetainSource(binding.OriginalResume); binding.ActualSources.Add(binding.OriginalResume);
            binding.Next.RetainAdditionalOriginal("checkpoint.actual-resume-attempt", binding.OriginalResume);
            binding.NewAdmission = await binding.OriginalResume.ConfigureAwait(false);
            return binding;
        }
        catch (Exception cause)
        {
            if (binding.OriginalRecoveryWrite is not null)
            {
                binding.Next.Retain(cause, binding.OriginalResume ?? (Task?)binding.OriginalRecoveryWrite);
                original.Retain(cause, binding.OriginalResume ?? (Task?)binding.OriginalRecoveryWrite);
                // Invoked/unknown CAS or cleanup is fail-stop. Both reservations and full
                // original sources survive; no new factory can be claimed on this binding.
            }
            else
            {
                ReleaseHealthyOrUnstartedInvocation(binding.Next);
                lock (_invocationAdmissionGate)
                {
                    if (_toolCheckpointContinuations.TryGetValue(original.OriginalBinding!.TaskId, out var same)
                        && ReferenceEquals(same, binding)) _toolCheckpointContinuations.Remove(original.OriginalBinding.TaskId);
                    _issuedToolCheckpointContinuations.Remove(boundary);
                }
                // This actual failed preparation task remains retained in original diagnostics;
                // it performed no CAS, scope acquisition, provider/tool dispatch or cleanup.
            }
            throw;
        }
    }

    internal void DemandOriginalToolCheckpointFactory(TaskRunToolCheckpointContinuationBinding binding, ChatSessionService chat)
    {
        if (!ReferenceEquals(binding.Issuer, this) || !ReferenceEquals(binding.Boundary.Chat, chat)
            || !_issuedToolCheckpointContinuations.TryGetValue(binding.Boundary, out var issued) || !ReferenceEquals(issued, binding)
            || !binding.Transferred || !binding.Claimed || binding.Preparation is not { IsCompletedSuccessfully: true }
            || !ReferenceEquals(binding.Preparation.Result, binding) || binding.NewAdmission is null)
            throw new InvalidOperationException("No actual privately issued, acknowledged and claimed checkpoint factory exists.");
        RequireOriginalInvocation(binding.Next);
    }

    internal IAsyncEnumerable<ChatStreamEvent> ClaimOriginalToolCheckpointContinuation(
        TaskRunToolCheckpointContinuationBinding binding, ChatSessionService chat, CancellationToken token)
    {
        lock (_invocationAdmissionGate)
        {
            if (!ReferenceEquals(binding.Issuer, this) || !ReferenceEquals(binding.Boundary.Chat, chat)
                || !_toolCheckpointContinuations.TryGetValue(binding.Acknowledged!.TaskId, out var actual)
                || !ReferenceEquals(actual, binding) || binding.Claimed || binding.Bound
                || binding.Preparation is not { IsCompletedSuccessfully: true } || !ReferenceEquals(binding.Preparation.Result, binding))
                throw new InvalidOperationException("The actual checkpoint factory is unavailable or already consumed.");
            binding.Claimed = true;
        }
        return binding.Original.OriginalToolContinuationFactory!(binding, token);
    }

    internal Task<TaskExecutionSnapshot> BindOriginalToolCheckpointBodyAsync(
        TaskRunToolCheckpointContinuationBinding binding, ChatSessionService chat, Guid contextId, CancellationToken token) =>
        StartOriginalProcessStage("bind-original-tool-checkpoint-body", token,
            stageToken => BindOriginalToolCheckpointBodyStageAsync(binding, chat, contextId, stageToken));

    // This is input/currentness custody only. It issues no continuation, candidate,
    // permission or dispatch witness, and public Task IDs cannot supply its original.
    internal Task<TaskExecutionSnapshot> ValidateOriginalToolCheckpointInputAsync(
        TaskRunInvocationCustody sameOriginal, TaskExecutionSnapshot expected, CancellationToken token) =>
        StartOriginalProcessStage("validate-original-tool-checkpoint-input", token, async stageToken =>
        {
            RequireOriginalInvocation(sameOriginal);
            if (sameOriginal.OriginalInputCurrentness is not { } input
                || sameOriginal.OriginalBinding is not { } bound
                || expected.TaskId != bound.TaskId || expected.ContextId != bound.ContextId
                || expected.ExecutionId != bound.ExecutionId || expected.OwnerBinding != bound.OwnerBinding)
                throw new InvalidOperationException("The genuine original checkpoint input binding is unavailable.");
            await RequireOriginalProcessStage().Await(() => input(stageToken)).ConfigureAwait(false);
            var actual = await RequireOriginalProcessSnapshotAsync(expected.TaskId, stageToken).ConfigureAwait(false);
            if (!SameOriginalCheckpointRow(expected, actual))
                throw new InvalidOperationException("The actual checkpoint row changed during original input revalidation.");
            // Input reads can await; real actor authority is checked after the last read.
            await ValidateOriginalProcessCommandAsync(actual, "task:validate-original-tool-checkpoint-input", stageToken).ConfigureAwait(false);
            RequireOriginalInvocation(sameOriginal);
            return actual;
        });

    private async Task<TaskExecutionSnapshot> BindOriginalToolCheckpointBodyStageAsync(
        TaskRunToolCheckpointContinuationBinding binding, ChatSessionService chat, Guid contextId, CancellationToken token)
    {
        var stage = RequireOriginalProcessStage();
        DemandOriginalToolCheckpointFactory(binding, chat);
        if (binding.Bound || binding.NewAdmission is not { } admission)
            throw new InvalidOperationException("The one-use checkpoint body already entered or lacks its actual admission.");
        var row = await RequireOriginalProcessSnapshotAsync(binding.Acknowledged!.TaskId, token).ConfigureAwait(false);
        if (row.ContextId != contextId || row.ContextId != binding.Acknowledged.ContextId
            || row.ExecutionId != binding.Acknowledged.ExecutionId || row.OwnerBinding != binding.Acknowledged.OwnerBinding
            || row.Attempts.LastOrDefault()?.Id != admission.AttemptId || row.RecoveryObservation is not null)
            throw new InvalidOperationException("The actual checkpoint task/run/owner changed before its first body stage.");
        await ValidateOriginalProcessCommandAsync(row, "task:bind-original-tool-checkpoint", token).ConfigureAwait(false);
        var actual = await stage.Await(() => GetIssuedAttemptAsync(row.TaskId, row.ExecutionId, admission.AttemptId, token)).ConfigureAwait(false);
        if (!ReferenceEquals(actual, admission)) throw new InvalidOperationException("The original resumed attempt was replaced.");
        await stage.Await(() => admission.Lease.RevalidateAsync(token).AsTask()).ConfigureAwait(false);
        var latest = await RequireOriginalProcessSnapshotAsync(row.TaskId, token).ConfigureAwait(false);
        if (!SameOriginalCheckpointRow(row, latest))
            throw new InvalidOperationException("The actual original checkpoint row changed during body authorization.");
        await ValidateOriginalProcessCommandAsync(latest, "task:bind-original-tool-checkpoint", token).ConfigureAwait(false);
        await stage.Await(() => ValidateOriginalToolCheckpointInputAsync(binding.Next, latest, token)).ConfigureAwait(false);
        binding.Bound = true;
        binding.Next.OriginalBinding = latest;
        return latest;
    }

    private void RememberAcknowledgedOriginalToolCheckpointCompletion(TaskRunInvocationCustody next, TaskExecutionSnapshot current)
    {
        if (next.OriginalToolContinuation is not { } binding) return;
        RequireSuccessfulOriginalToolCheckpointCompletion(binding, current);
        binding.CompletedCandidate = current;
    }

    private void RequireSuccessfulOriginalToolCheckpointCompletion(TaskRunToolCheckpointContinuationBinding binding,
        TaskExecutionSnapshot current)
    {
        var next = binding.Next;
        var completion = next.OriginalCompletion;
        var oldRecovery = binding.PriorSuspended?.RecoveryObservation;
        if (!ReferenceEquals(binding.Issuer, this) || !ReferenceEquals(binding.Next, next) || !binding.Claimed || !binding.Bound
            || binding.Preparation is not { IsCompletedSuccessfully: true } || completion is not { IsCompletedSuccessfully: true }
            || next.OriginalOwningCompletion is not { IsCompletedSuccessfully: true } || next.OriginalSettlement is not { IsCompletedSuccessfully: true }
            || oldRecovery is null || current.State != TaskExecutionLifecycle.Completed || current.RecoveryObservation is not null
            || !current.RecoveryHistory.Any(value => value.ObservationId == oldRecovery.ObservationId
                && (value with { Causes = oldRecovery.Causes }) == oldRecovery)
            || next.Causes.Count != 0 || !next.OwnedCleanupTerminal || !next.ResourcesDisposed
            || next.OriginalDispose is not { IsCompletedSuccessfully: true }
            || next.OriginalTracker is not null && next.OriginalTrackerDispose is not { IsCompletedSuccessfully: true }
            || binding.ActualSources.Any(actual => !actual.IsCompletedSuccessfully))
            throw new InvalidOperationException("Only actual completed continuation and all successful original preparation/cleanup can retire checkpoint custody.");
        RequireCompletionBasis(completion.Result, current);
        foreach (var outcome in next.CaptureOriginalToolOutcomes())
            if (!HasSuccessfulOriginalToolCheckpointOutcome(outcome))
                throw new InvalidOperationException("An actual accepted tool owner remains held or failed at checkpoint retirement.");
        RequireOriginalToolCheckpointBoundary(binding, binding.PriorSuspended!);
    }

    private Task<TaskExecutionSnapshot> ValidateOriginalToolCheckpointResolutionAsync(
        TaskRunToolCheckpointContinuationBinding binding, HostedOriginalRunResume host) =>
        StartOriginalProcessStage("resolve-completed-original-tool-checkpoint", CancellationToken.None, async token =>
        {
            var stage = RequireOriginalProcessStage();
            if (!ReferenceEquals(binding.OriginalHost, host) || !ReferenceEquals(host.ToolCheckpoint, binding)
                || host.Producer is not { IsCompletedSuccessfully: true } || !host.ProducerCustody.Healthy
                || !ReferenceEquals(host.ActualRun, binding.Next) || !ReferenceEquals(host.Source, binding.Next.OriginalProcessProducer)
                || !host.Source.HasHealthyClosedOriginal || binding.CompletedCandidate is not { } candidate)
                throw new InvalidOperationException("The SAME encompassing business producer and cleanup have not completed successfully.");
            var row = await stage.Await(() => repository.GetAsync(candidate.TaskId, token), owningCleanup: true).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The acknowledged completed checkpoint task is unavailable.");
            if (!SameOriginalCheckpointRow(candidate, row))
                throw new InvalidOperationException("The actual completed row changed before owning checkpoint resolution.");
            await stage.Await(() => ValidateTaskCommandAsync(row, "task:resolve-completed-original-tool-checkpoint", token), owningCleanup: true).ConfigureAwait(false);
            row = await stage.Await(() => repository.GetAsync(candidate.TaskId, token), owningCleanup: true).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The actual completed checkpoint row is unavailable.");
            if (!SameOriginalCheckpointRow(candidate, row))
                throw new InvalidOperationException("The completed checkpoint identity/history changed during resolution validation.");
            await stage.Await(() => ValidateTaskCommandAsync(row, "task:resolve-completed-original-tool-checkpoint", token), owningCleanup: true).ConfigureAwait(false);
            RequireSuccessfulOriginalToolCheckpointCompletion(binding, row);
            if (_observationFailures.Any(failure => failure.TaskId == row.TaskId && failure.ExecutionId == row.ExecutionId))
                throw new InvalidOperationException("An actual terminal publication/observer failure remains owned by this run.");
            return row;
        });

    private void CommitResolvedOriginalToolCheckpointContinuation(TaskRunToolCheckpointContinuationBinding binding,
        HostedOriginalRunResume host, TaskExecutionSnapshot current)
    {
        var next = binding.Next;
        var completion = next.OriginalCompletion!;
        var validation = binding.OriginalResolutionValidation;
        if (!ReferenceEquals(binding.OriginalHost, host) || !ReferenceEquals(host.ToolCheckpoint, binding)
            || host.Producer is not { IsCompletedSuccessfully: true } || !host.ProducerCustody.Healthy
            || validation is not { IsCompletedSuccessfully: true } || !ReferenceEquals(validation.Result, current)
            || !ReferenceEquals(host.ActualRun, next) || !host.Source.HasHealthyClosedOriginal)
            throw new InvalidOperationException("The genuine whole business/cleanup and resolution driver prerequisites are not successful.");
        RequireSuccessfulOriginalToolCheckpointCompletion(binding, current);
        if (_observationFailures.Any(failure => failure.TaskId == current.TaskId && failure.ExecutionId == current.ExecutionId))
            throw new InvalidOperationException("A matching original observer cause prevents healthy checkpoint resolution.");
        lock (_invocationAdmissionGate)
        {
            if (binding.Resolution is not null) return;
            if (!_toolCheckpointContinuations.TryGetValue(current.TaskId, out var actual) || !ReferenceEquals(actual, binding))
                throw new InvalidOperationException("Another original replaced the checkpoint retirement claim.");
            binding.Resolution = new(binding, completion, binding.Original.CaptureRecoveryOriginals(), current,
                host.Producer, validation);
            binding.Original.OriginalResolvedToolCheckpoint = binding.Resolution;
            ReleaseHealthyOrUnstartedInvocation(binding.Original);
            ReleaseHealthyOrUnstartedInvocation(next);
            _toolCheckpointContinuations.Remove(current.TaskId);
            _originalInvocations.TryRemove(new KeyValuePair<Guid, TaskRunInvocationCustody>(current.TaskId, next));
        }
    }
}
