using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Haven.Core;

namespace Haven.Application;

/// <summary>Custody of the actual invocation's source tasks, never reconstructed from durable text or task status.</summary>
internal sealed class TaskRunInvocationCustody
{
    private readonly object _gate = new();
    private readonly List<Exception> _causes = [];
    private readonly HashSet<Exception> _faultedOrUnknownCancellationCauses = [];
    internal readonly object OriginalSelf;
    internal readonly TaskExecutionCoordinator Issuer;
    internal readonly Guid ObservationId = Guid.NewGuid();
    internal readonly List<Task> OriginalMoves = [];
    internal Task? OriginalDispose;
    internal bool DisposeInvoked;
    internal Exception? DisposeDirectFailure;
    internal ChatExecutionTracker? OriginalTracker;
    internal Task? OriginalTrackerDispose;
    internal bool TrackerDisposeInvoked;
    internal Exception? TrackerDisposeDirectFailure;
    internal List<IDisposable> OriginalResources = [];
    internal bool ResourcesDisposed;
    internal Task<TaskExecutionSnapshot>? OriginalCompletion;
    internal Task? OriginalSettlement;
    internal TaskExecutionSnapshot? OriginalBinding;
    internal TaskExecutionSnapshot? ProposedBinding;
    internal Task<TaskExecutionSnapshot>? OriginalBegin;
    internal TaskExecutionSnapshot? CompletionBasis;
    internal Task<TaskExecutionSnapshot>? OriginalOwningCompletion;
    internal bool CompletionCallAdmitted;
    internal Action? DeferredCompletionPublication;
    internal ChatStreamEvent? DeferredCompletedMessage;
    internal bool OwnedCleanupTerminal;
    internal bool SlotReserved;
    internal bool ReachedEnd;
    internal bool PublicationAcknowledged;
    internal bool BoundByActualBegin;
    internal bool AttemptAdmissionInvoked;

    internal TaskRunInvocationCustody(TaskExecutionCoordinator issuer) { Issuer = issuer; OriginalSelf = this; }
    internal IReadOnlyList<Exception> Causes { get { lock (_gate) return _causes.ToArray(); } }
    internal void RetainOriginalMove(Task original)
    {
        ArgumentNullException.ThrowIfNull(original);
        lock (_gate) OriginalMoves.Add(original);
    }
    internal TaskRunOriginalRecoveryCapture CaptureRecoveryOriginals()
    {
        lock (_gate)
        {
            var originals = new List<TaskRunOriginalSource>();
            void Capture(string stage, Task? actual)
            {
                if (actual is null) return;
                var status = actual.Status;
                originals.Add(new(stage, actual, status));
            }
            Capture("task.begin", OriginalBegin);
            for (var index = 0; index < OriginalMoves.Count; index++) Capture("body.move:" + index, OriginalMoves[index]);
            Capture("body.dispose", OriginalDispose);
            Capture("tracker.dispose", OriginalTrackerDispose);
            Capture("runtime.settlement", OriginalSettlement);
            Capture("task.completion", OriginalCompletion);
            Capture("task.owning-completion", OriginalOwningCompletion);
            var causes = _causes.ToList();
            // A raw source may have become Faulted before its owning catch runs. Its
            // captured status and actual Task.Exception supply this same observation.
            foreach (var source in originals)
                if (source.ObservedStatus == TaskStatus.Faulted && source.Actual.Exception is { } fault)
                    foreach (var direct in fault.InnerExceptions)
                        if (!causes.Any(existing => ReferenceEquals(existing, direct))) causes.Add(direct);
            return new TaskRunOriginalRecoveryCapture(originals.ToArray(), causes.ToArray());
        }
    }
    internal void Retain(Exception cause, Task? original = null)
    {
        lock (_gate)
        {
            IEnumerable<Exception> errors = original?.Exception is { } aggregate ? aggregate.InnerExceptions : new[] { cause };
            foreach (var error in errors)
            {
                if (!_causes.Any(existing => ReferenceEquals(existing, error))) _causes.Add(error);
                if (error is OperationCanceledException && (original is null || original.IsFaulted))
                    _faultedOrUnknownCancellationCauses.Add(error);
            }
        }
    }
    internal void ThrowRetained()
    {
        var errors = Causes;
        if (errors.Count == 1 && errors[0] is OperationCanceledException
            && _faultedOrUnknownCancellationCauses.Contains(errors[0]))
            throw new AggregateException("An original faulted or synchronous invocation cause is not canceled-task evidence.", errors[0]);
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("The actual invocation and its original cleanup/observation failed.", errors);
    }
}

public sealed partial class TaskExecutionCoordinator
{
    // Healthy originals are removed only after the actual iterator terminal/owner completion.
    // Failed/CAS-lost originals keep their exact direct causes and tasks for explicit recovery.
    private readonly ConcurrentDictionary<Guid, TaskRunInvocationCustody> _originalInvocations = new();
    public const int OriginalInvocationCapacity = 128;
    private readonly object _invocationAdmissionGate = new();
    private int _invocationAdmissions;
    public int LiveOriginalInvocationCount { get { lock (_invocationAdmissionGate) return _invocationAdmissions; } }
    internal TaskRunInvocationCustody CreateOriginalInvocationCustody() => new(this);

    private void ReserveOriginalInvocation(TaskRunInvocationCustody custody)
    {
        lock (_invocationAdmissionGate)
        {
            if (custody.SlotReserved) throw new InvalidOperationException("The original invocation already owns an admission slot.");
            if (_invocationAdmissions >= OriginalInvocationCapacity)
                throw new InvalidOperationException("Retained original invocation capacity is exhausted; unresolved originals require inspection.");
            _invocationAdmissions++;
            custody.SlotReserved = true;
        }
    }
    private void ReleaseHealthyOrUnstartedInvocation(TaskRunInvocationCustody custody)
    {
        lock (_invocationAdmissionGate)
            if (custody.SlotReserved) { custody.SlotReserved = false; _invocationAdmissions--; }
    }

    private void RequireNoUnresolvedOriginalInvocation(Guid taskId, TaskRunInvocationCustody? completingOriginal = null)
    {
        if (!_originalInvocations.TryGetValue(taskId, out var original) || !original.DisposeInvoked) return;
        if (completingOriginal is not null && ReferenceEquals(original, completingOriginal)
            && ReferenceEquals(completingOriginal.Issuer, this) && ReferenceEquals(completingOriginal.OriginalSelf, completingOriginal)
            && completingOriginal.CompletionCallAdmitted && completingOriginal.OwnedCleanupTerminal && completingOriginal.Causes.Count == 0)
            return;
        if (original.Causes.Count > 0 || original.OriginalCompletion is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("The actual terminal invocation needs inspection before dispatch or completion; recorded status cannot replace it.");
    }

    internal async Task<TaskExecutionSnapshot> CompleteOriginalInvocationAsync(TaskRunInvocationCustody custody)
    {
        RequireOriginalInvocation(custody);
        var basis = custody.CompletionBasis ?? throw new InvalidOperationException("The actual original produced no completion basis.");
        if (!custody.ReachedEnd || !custody.OwnedCleanupTerminal || custody.Causes.Count != 0
            || custody.OriginalMoves.Any(original => !original.IsCompletedSuccessfully)
            || !custody.DisposeInvoked || custody.OriginalDispose is not { IsCompletedSuccessfully: true }
            || custody.OriginalTracker is not null && custody.OriginalTrackerDispose is not { IsCompletedSuccessfully: true }
            || !custody.ResourcesDisposed || custody.DeferredCompletedMessage is null || custody.OriginalCompletion is not null)
            throw new InvalidOperationException("Original body, iterator and resource cleanup must succeed before canonical completion.");
        var current = await RequireAsync(basis.TaskId, CancellationToken.None).ConfigureAwait(false);
        RequireCompletionBasis(basis, current);
        var attempt = current.Attempts.LastOrDefault() ?? throw new InvalidOperationException("No original attempt is available for completion.");
        await ValidateTaskCommandAsync(current, "complete-original-invocation-after-cleanup", CancellationToken.None).ConfigureAwait(false);
        custody.CompletionCallAdmitted = true;
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<TaskExecutionSnapshot> CompletePublishedOriginalAsync()
        {
            await published.Task.ConfigureAwait(false);
            custody.OriginalOwningCompletion = CompleteAttemptOriginalAsync(current.TaskId, current.ExecutionId,
                attempt.Id, CancellationToken.None, custody);
            return await custody.OriginalOwningCompletion.ConfigureAwait(false);
        }
        // Publish the whole original call before any owning completion callback can re-enter.
        custody.OriginalCompletion = CompletePublishedOriginalAsync();
        published.SetResult();
        return await custody.OriginalCompletion.ConfigureAwait(false);
    }

    internal void ReportAcknowledgedCompletionObservationFailure(TaskRunInvocationCustody custody, Exception cause)
    {
        if (custody.OriginalCompletion is not { IsCompletedSuccessfully: true } completed)
            throw new InvalidOperationException("No genuine completion acknowledgment exists for this observer failure.");
        _observationFailures.Enqueue(new TaskObservationFailure(completed.Result.TaskId, completed.Result.ExecutionId,
            completed.Result.PersistenceRevision, "acknowledged-completion-observer", cause));
    }

    internal void RetainOriginalInvocationObservationFailure(TaskRunInvocationCustody custody, Exception cause)
    {
        custody.Retain(cause);
        if (custody.OriginalBinding is { } binding)
            _observationFailures.Enqueue(new TaskObservationFailure(binding.TaskId, binding.ExecutionId,
                binding.PersistenceRevision, "original-invocation-terminal-observation", cause));
    }

    internal async Task<TaskExecutionSnapshot> BeginOriginalInvocationAsync(
        TaskRunInvocationCustody custody, Guid contextId, Guid executionId, string summary,
        TaskExecutionDurability durability, IReadOnlyCollection<string>? requestedScopes, CancellationToken token)
    {
        RequireOriginalInvocation(custody, boundRequired: false);
        ReserveOriginalInvocation(custody);
        return await BeginAuthorizedOriginalAsync(contextId, executionId, summary, durability, requestedScopes, token, custody).ConfigureAwait(false);
    }

    internal async Task<TaskExecutionSnapshot> BindOriginalContinuationAsync(
        TaskRunInvocationCustody custody, ProviderExecutionContext observation, Guid actualContextId, CancellationToken token)
    {
        RequireOriginalInvocation(custody, boundRequired: false);
        var current = await RequireAsync(observation.TaskId, token).ConfigureAwait(false);
        if (current.ContextId != actualContextId || current.ContextId != observation.ContextId
            || current.ExecutionId != observation.ExecutionId || current.PersistenceRevision != observation.PersistenceRevision
            || observation.AttemptId is { } id && current.Attempts.LastOrDefault()?.Id != id)
            throw new InvalidOperationException("The continuation does not bind the actual current task, context and run.");
        ValidateOwner(current, current.OwnerBinding);
        await ValidateTaskCommandAsync(current, "task.invoke-original", token).ConfigureAwait(false);
        if (current.Attempts.LastOrDefault() is { } attempt)
        {
            var actual = await GetIssuedAttemptAsync(current.TaskId, current.ExecutionId, attempt.Id, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The actual original continuation attempt issuer is unavailable; durable state cannot recreate its lease.");
            await actual.Lease.RevalidateAsync(token).ConfigureAwait(false);
        }
        ReserveOriginalInvocation(custody);
        BindOriginalInvocation(custody, current);
        return current;
    }

    private void BindOriginalInvocation(TaskRunInvocationCustody custody, TaskExecutionSnapshot acknowledged, bool alreadyRegistered = false)
    {
        ValidateOwner(acknowledged, acknowledged.OwnerBinding);
        if (alreadyRegistered ? !_originalInvocations.TryGetValue(acknowledged.TaskId, out var registered) || !ReferenceEquals(registered, custody)
            : !_originalInvocations.TryAdd(acknowledged.TaskId, custody))
            throw new InvalidOperationException("Another original invocation still owns this canonical task's active or unresolved call.");
        custody.OriginalBinding = acknowledged;
    }

    private void RequireOriginalInvocation(TaskRunInvocationCustody custody, bool boundRequired = true)
    {
        ArgumentNullException.ThrowIfNull(custody);
        if (!ReferenceEquals(custody.Issuer, this) || !ReferenceEquals(custody.OriginalSelf, custody))
            throw new InvalidOperationException("This is not the original invocation custody issued by this task owner.");
        if (boundRequired && (custody.OriginalBinding is not { } binding
            || !_originalInvocations.TryGetValue(binding.TaskId, out var actual) || !ReferenceEquals(actual, custody)))
            throw new InvalidOperationException("No actual original invocation is bound to this task.");
    }

    private static IEnumerable<Exception> OriginalDiagnosticCauses(Exception actual)
    {
        yield return actual;
        if (actual is AggregateException envelope)
            foreach (var direct in envelope.InnerExceptions)
                foreach (var original in OriginalDiagnosticCauses(direct)) yield return original;
    }

    /// <summary>Called only after the actual original iterator/Dispose terminal. This does not acknowledge provider faults.</summary>
    internal async Task<TaskExecutionSnapshot?> ObserveOriginalInvocationTerminalAsync(TaskRunInvocationCustody custody)
    {
        if (custody.OriginalBinding is null)
        {
            // No successful Begin may be invented. An invoked/faulted original Task CAS keeps
            // its proposed identity and admission slot; pre-CAS refusals have no task effect.
            if (custody.OriginalBegin is null) ReleaseHealthyOrUnstartedInvocation(custody);
            return null;
        }
        RequireOriginalInvocation(custody);
        if (custody.OriginalMoves.Any(original => !original.IsCompleted) || !custody.DisposeInvoked
            || custody.OriginalDispose is not { IsCompleted: true } && custody.DisposeDirectFailure is null
            || custody.OriginalTracker is not null && (!custody.TrackerDisposeInvoked
                || custody.OriginalTrackerDispose is not { IsCompleted: true } && custody.TrackerDisposeDirectFailure is null)
            || !custody.ResourcesDisposed)
            throw new InvalidOperationException("The original iterator and Dispose have not reached terminal state.");
        var binding = custody.OriginalBinding;
        var current = await RequireAsync(binding.TaskId, CancellationToken.None).ConfigureAwait(false);
        RequireRun(current, binding.ExecutionId);
        if (current.ContextId != binding.ContextId || current.OwnerBinding != binding.OwnerBinding)
            throw new InvalidOperationException("The durable task no longer has the original invocation's owner/run binding.");

        if (custody.Causes.Count == 0 && custody.OriginalCompletion is { IsCompletedSuccessfully: true } completed
            && completed.Result.TaskId == current.TaskId && completed.Result.ExecutionId == current.ExecutionId
            && current.State == TaskExecutionLifecycle.Completed)
        {
            _originalInvocations.TryRemove(new KeyValuePair<Guid, TaskRunInvocationCustody>(current.TaskId, custody));
            ReleaseHealthyOrUnstartedInvocation(custody);
            return current;
        }

        // Old actor revocation cannot manufacture a fresh grant: this original issuer records only
        // the outcome it actually owns. All dispatch/replay stays refused by RecoveryObservation.
        var iteratorOutcome = custody.Causes.Count > 0 ? TaskRunIteratorTerminalOutcome.Failed
            : custody.ReachedEnd ? TaskRunIteratorTerminalOutcome.ReachedEnd : TaskRunIteratorTerminalOutcome.ClosedBeforeEnd;
        var settlementOutcome = TaskRunOriginalSettlementOutcome.Pending;
        var attempt = current.Attempts.LastOrDefault();
        TaskRunAttemptAdmission? originalAdmission = null;
        if (attempt is null)
            // Only this original Begin+coordinator producer can observe that admission was never
            // invoked. Empty durable history after restart alone is not an absence witness.
            settlementOutcome = custody.BoundByActualBegin && !custody.AttemptAdmissionInvoked
                ? TaskRunOriginalSettlementOutcome.NoAttemptAdmissionWasInvoked
                : TaskRunOriginalSettlementOutcome.OriginalUnavailable;
        else if (custody.OriginalCompletion is { IsCompletedSuccessfully: true })
            settlementOutcome = TaskRunOriginalSettlementOutcome.JoinedByOriginalCompletion;
        else if (_issuedAdmissions.TryGetValue(attempt.Id, out var original)
            && original.Snapshot.TaskId == current.TaskId && original.Snapshot.ExecutionId == current.ExecutionId
            && original.Snapshot.OwnerBinding == binding.OwnerBinding)
            originalAdmission = original;
        else
            settlementOutcome = TaskRunOriginalSettlementOutcome.OriginalUnavailable;

        TaskRunRecoveryObservation Projection(TaskRunOriginalSettlementOutcome outcome) => new(
            custody.ObservationId, "ORIGINAL_INVOCATION_REQUIRES_INSPECTION", _time.GetUtcNow(), iteratorOutcome, outcome,
            Array.AsReadOnly(custody.Causes.SelectMany(OriginalDiagnosticCauses)
                .Select(error => new ExecutionFailure("ORIGINAL_INVOCATION_CAUSE",
                SensitiveTextRedactor.Redact(error.GetType().Name, 128), SensitiveTextRedactor.Redact(error.Message, 2000),
                AffectedComponent: "task-orchestration")).ToArray()));

        current = await PersistAsync(current with
        {
            State = TaskExecutionLifecycle.Suspended,
            Attempts = current.Attempts.Select(value => value.Id == attempt?.Id
                && value.State is TaskRunAttemptState.Admitted or TaskRunAttemptState.Running
                ? value with { State = TaskRunAttemptState.Suspended, UpdatedAt = _time.GetUtcNow() } : value).ToArray(),
            RecoveryObservation = Projection(settlementOutcome), UpdatedAt = _time.GetUtcNow()
        }, CancellationToken.None).ConfigureAwait(false);
        custody.PublicationAcknowledged = true;

        if (originalAdmission is not null)
        {
            try
            {
                var owner = _runtimeSettlement ?? throw new InvalidOperationException("The actual original runtime settlement owner is unavailable.");
                custody.OriginalSettlement = owner.AwaitSettlementAsync(current.TaskId, current.ExecutionId,
                    originalAdmission.AttemptId, CancellationToken.None);
                await custody.OriginalSettlement.ConfigureAwait(false);
                settlementOutcome = TaskRunOriginalSettlementOutcome.Joined;
            }
            catch (Exception settlementFailure)
            {
                custody.Retain(settlementFailure, custody.OriginalSettlement);
                settlementOutcome = TaskRunOriginalSettlementOutcome.Failed;
            }
            // Re-read after joining all actual originals. Queue/accepted work stays conserved.
            current = await RequireAsync(binding.TaskId, CancellationToken.None).ConfigureAwait(false);
            RequireRun(current, binding.ExecutionId);
            if (current.OwnerBinding != binding.OwnerBinding || current.RecoveryObservation?.ObservationId != custody.ObservationId)
                throw new InvalidOperationException("The original recovery observation was replaced during actual settlement.");
            current = await PersistAsync(current with { RecoveryObservation = Projection(settlementOutcome), UpdatedAt = _time.GetUtcNow() },
                CancellationToken.None).ConfigureAwait(false);
        }
        return current;
    }
}
