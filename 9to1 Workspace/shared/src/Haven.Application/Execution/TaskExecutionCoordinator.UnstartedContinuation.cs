using System.Runtime.ExceptionServices;
using Haven.Core;

namespace Haven.Application;

/// <summary>Private live transfer of one never-started invocation. Fields and IDs do not grant permission.</summary>
internal sealed class TaskRunUnstartedContinuationBinding(
    TaskExecutionCoordinator issuer, TaskRunInvocationCustody original, TaskRunInvocationCustody next,
    ChatSessionService sameChat)
{
    internal readonly TaskExecutionCoordinator Issuer = issuer;
    internal readonly TaskRunInvocationCustody Original = original;
    internal readonly TaskRunInvocationCustody Next = next;
    internal readonly ChatSessionService SameChat = sameChat;
    internal Task<TaskRunUnstartedContinuationBinding> Preparation = null!;
    internal readonly List<Task> ActualStages = [];
    internal Task<ITaskRunUnstartedContinuationPermissionLease>? OriginalPermissionAcquisition;
    internal ITaskRunUnstartedContinuationPermissionLease? OriginalPermissionLease;
    internal Task<IAsyncDisposable>? OriginalPinAcquisition;
    internal Task? OriginalPinClose;
    internal Task? OriginalPermissionClose;
    internal Task<TaskExecutionSnapshot>? OriginalContinuationWrite;
    internal TaskExecutionSnapshot? Acknowledged;
    internal TaskExecutionSnapshot? OriginalSuspendedObservation;
    internal TaskRunResolvedUnstartedContinuationReceipt? OriginalResolution;
    internal bool RetirementInvoked;
    internal ProviderExecutionContext? OriginalObservation;
    internal bool Claimed;
    internal bool Bound;
}

/// <summary>Private owning receipt retained on the completed actual invocation, not a global
/// healthy-binding archive. Its raw originals/cause references and acknowledged history remain
/// available to the actual owning invocation; copied IDs do not confer retirement authority.</summary>
internal sealed class TaskRunResolvedUnstartedContinuationReceipt(
    TaskExecutionCoordinator issuer, TaskRunUnstartedContinuationBinding originalBinding,
    Task<TaskRunUnstartedContinuationBinding> actualPreparation, Task<TaskExecutionSnapshot> actualCompletion,
    TaskRunOriginalRecoveryCapture priorOriginals, TaskExecutionSnapshot acknowledged)
{
    internal readonly TaskExecutionCoordinator Issuer = issuer;
    internal readonly TaskRunUnstartedContinuationBinding OriginalBinding = originalBinding;
    internal readonly Task<TaskRunUnstartedContinuationBinding> ActualPreparation = actualPreparation;
    internal readonly Task<TaskExecutionSnapshot> ActualCompletion = actualCompletion;
    internal readonly TaskRunOriginalRecoveryCapture PriorOriginals = priorOriginals;
    internal readonly TaskExecutionSnapshot Acknowledged = acknowledged;
}

public sealed partial class TaskExecutionCoordinator
{
    // Both failed preparation and resolved originals retain exact tasks/causes. Existing
    // 128 original invocation admission bounds this custody; no persisted state reconstructs it.
    private readonly Dictionary<Guid, TaskRunUnstartedContinuationBinding> _unstartedContinuations = [];
    private readonly Dictionary<Guid, int> _unstartedPreparationCounts = [];
    private readonly AsyncLocal<ContinuationPhase?> _continuationPhase = new();
    [ThreadStatic] private static List<TaskExecutionCoordinator>? _physicalContinuationOwners;
    private sealed class ContinuationPhase(TaskExecutionCoordinator owner, ContinuationPhase? previous)
    {
        internal readonly TaskExecutionCoordinator Owner = owner;
        internal readonly ContinuationPhase? Previous = previous;
        internal int Active = 1;
    }
    private void RefuseOriginalContinuationSelfJoin()
    {
        if (_physicalContinuationOwners?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual continuation callback must return before another owning continuation can be awaited.");
        for (var phase = _continuationPhase.Value; phase is not null; phase = phase.Previous)
            if (ReferenceEquals(phase.Owner, this) && Volatile.Read(ref phase.Active) != 0)
                throw new InvalidOperationException("An actual continuation preparation cannot join itself.");
    }
    private T AcquireOriginalContinuationCall<T>(Func<T> callback)
    {
        var owners = _physicalContinuationOwners ??= [];
        owners.Add(this);
        try { return callback(); }
        finally { owners.RemoveAt(owners.Count - 1); }
    }

    internal Task<TaskRunUnstartedContinuationBinding> PrepareOriginalUnstartedContinuationAsync(
        TaskRunOriginalRecoveryInspection inspection, ChatSessionService sameChat, CancellationToken token)
    {
        RefuseOriginalContinuationSelfJoin();
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(inspection.Issuer, this) || inspection.OriginalCustody is not { } original
            || !ReferenceEquals(original.OriginalChatOwner, sameChat))
            throw new InvalidOperationException("Only this actual live invocation and its SAME Chat producer can prepare continuation.");
        RequireOriginalInvocation(original);
        TaskRunUnstartedContinuationBinding binding;
        TaskCompletionSource start;
        lock (_invocationAdmissionGate)
        {
            if (_unstartedContinuations.TryGetValue(inspection.ActualSnapshot.TaskId, out var existing))
            {
                if (!ReferenceEquals(existing.Original, original) || !ReferenceEquals(existing.SameChat, sameChat) || existing.Claimed)
                    throw new InvalidOperationException("The original continuation is already consumed or belongs to another invocation.");
                return existing.Preparation;
            }
            if (original.OriginalContinuationFactory is null || original.OriginalInputCurrentness is null)
                throw new InvalidOperationException("No actual original input factory/currentness producer is retained.");
            var count = _unstartedPreparationCounts.GetValueOrDefault(inspection.ActualSnapshot.TaskId);
            if (count >= OriginalInvocationCapacity)
                throw new InvalidOperationException("Retained original continuation preparation capacity is exhausted; real diagnostics cannot be discarded.");
            _unstartedPreparationCounts[inspection.ActualSnapshot.TaskId] = count + 1;
            var next = CreateOriginalInvocationCustody();
            ReserveOriginalInvocation(next);
            binding = new(this, original, next, sameChat);
            start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            // The actual full orchestration Task, not a substitute completion promise,
            // is published before its first callback; the start gate has no grant meaning.
            binding.Preparation = PreparePublishedOriginalUnstartedAsync(start.Task, binding, inspection, token);
            _unstartedContinuations.Add(inspection.ActualSnapshot.TaskId, binding);
            original.RetainAdditionalOriginal("continuation.preparation", binding.Preparation);
        }
        start.SetResult();
        return binding.Preparation;
    }

    private static void RequireOriginalNeverStarted(TaskRunInvocationCustody original, TaskExecutionSnapshot current)
    {
        if (!original.BoundByActualBegin || original.OriginalBegin is not { IsCompletedSuccessfully: true }
            || original.AttemptAdmissionInvoked || original.OriginalProviderInvocationInvoked || current.Attempts.Count != 0
            || !original.CanReturnPublishedPermissionRefusal(current)
            || current.RecoveryObservation is not { SettlementOutcome: TaskRunOriginalSettlementOutcome.NoAttemptAdmissionWasInvoked }
            || current.RecoveryObservation.ObservationId != original.ObservationId
            || current.Plan.Count != 0 || current.LastCheckpointActionId is not null || current.CheckpointId is not null)
            throw new InvalidOperationException("Only the live source-approved invocation with successful no-attempt/input/cleanup custody can continue. Unknown work is refused.");
        if (current.RecoveryHistory.Count >= OriginalInvocationCapacity)
            throw new InvalidOperationException("Original recovery history capacity is exhausted; diagnostics cannot be discarded for another continuation.");
    }

    private async Task<TaskRunUnstartedContinuationBinding> PreparePublishedOriginalUnstartedAsync(Task start,
        TaskRunUnstartedContinuationBinding binding, TaskRunOriginalRecoveryInspection inspection, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        var phase = new ContinuationPhase(this, _continuationPhase.Value);
        _continuationPhase.Value = phase;
        var original = binding.Original;
        var failures = new List<Exception>();
        var faultedOrUnknownCancellationCauses = new HashSet<Exception>();
        Task? activeOriginal = null;
        IAsyncDisposable? actualPin = null;
        bool transferred = false;
        void Keep(Exception cause, Task? raw = null)
        {
            IEnumerable<Exception> direct = raw?.Exception is { } group ? group.InnerExceptions : new[] { cause };
            foreach (var value in direct)
            {
                if (!failures.Any(existing => ReferenceEquals(existing, value))) failures.Add(value);
                if (value is OperationCanceledException && (raw is null || !raw.IsCanceled))
                    faultedOrUnknownCancellationCauses.Add(value);
                // The complete failed actual preparation remains inspectable via its
                // original Task. Only invoked CAS/unknown close below poisons replay.
            }
        }
        T Track<T>(string stage, T actual) where T : Task
        {
            binding.ActualStages.Add(actual);
            activeOriginal = actual;
            original.RetainAdditionalOriginal(stage, actual);
            return actual;
        }
        try
        {
            var currentRead = Track("continuation.initial-task-read", AcquireOriginalContinuationCall(() => RequireAsync(inspection.ActualSnapshot.TaskId, token)));
            var current = await currentRead.ConfigureAwait(false);
            RequireOriginalInvocation(original);
            RequireRun(current, inspection.ActualSnapshot.ExecutionId);
            if (current.OwnerBinding != inspection.ActualSnapshot.OwnerBinding || current.ContextId != inspection.ActualSnapshot.ContextId)
                throw new UnauthorizedAccessException("The actual owner/context changed after inspection.");
            await Track("continuation.initial-command", AcquireOriginalContinuationCall(() =>
                ValidateTaskCommandAsync(current, "task:continue-never-started-original", token))).ConfigureAwait(false);
            RequireOriginalNeverStarted(original, current);
            var input = AcquireOriginalContinuationCall(() => original.OriginalInputCurrentness!(token));
            _ = Track("continuation.input-currentness", input);
            await input.ConfigureAwait(false);
            var (ask, publication, owner) = original.RequireOriginalPublishedPermission();
            if (_unstartedPermissionSource is not null && !ReferenceEquals(_unstartedPermissionSource, owner))
                throw new InvalidOperationException("The configured continuation issuer is not the SAME original permission owner.");
            // Deny-only private response observation, NOT a scope grant. Crucially, an
            // early preapproval refusal invokes no acquisition or temporary lifetime scope.
            if (!owner.HasOriginalAllowedUnstartedResponse(ask, publication))
                throw new InvalidOperationException("The SAME original permission response has not completed successfully; approve it explicitly before continuation.");
            binding.OriginalPermissionAcquisition = AcquireOriginalContinuationCall(() =>
                owner.AcquireOriginalUnstartedContinuationAsync(ask, publication, current, token).AsTask());
            _ = Track("continuation.permission-acquisition", binding.OriginalPermissionAcquisition);
            binding.OriginalPermissionLease = await binding.OriginalPermissionAcquisition.ConfigureAwait(false);
            var latestRead = Track("continuation.final-task-read", AcquireOriginalContinuationCall(() => RequireAsync(current.TaskId, token)));
            var latest = await latestRead.ConfigureAwait(false);
            RequireRun(latest, current.ExecutionId);
            if (latest.OwnerBinding != current.OwnerBinding || latest.ContextId != current.ContextId)
                throw new UnauthorizedAccessException("The original continuation owner/context changed during permission acquisition.");
            RequireOriginalNeverStarted(original, latest);
            await Track("continuation.final-command", AcquireOriginalContinuationCall(() =>
                ValidateTaskCommandAsync(latest, "task:continue-never-started-original", token))).ConfigureAwait(false);
            var revalidation = AcquireOriginalContinuationCall(() => binding.OriginalPermissionLease.RevalidateOriginalAsync(latest, token).AsTask());
            _ = Track("continuation.permission-revalidation", revalidation);
            await revalidation.ConfigureAwait(false);
            // Reads, actual actor/source policy and input checks all finish before the pure lifetime pin.
            binding.OriginalPinAcquisition = AcquireOriginalContinuationCall(() => binding.OriginalPermissionLease.AcquireOriginalCommitPinAsync(token).AsTask());
            _ = Track("continuation.pin-acquisition", binding.OriginalPinAcquisition);
            actualPin = await binding.OriginalPinAcquisition.ConfigureAwait(false);
            var recovery = latest.RecoveryObservation ?? throw new InvalidOperationException("No acknowledged original recovery observation exists.");
            binding.OriginalSuspendedObservation = latest;
            var proposed = latest with
            {
                RecoveryHistory = Array.AsReadOnly(latest.RecoveryHistory.Append(recovery with
                { Causes = Array.AsReadOnly(recovery.Causes.ToArray()) }).ToArray()),
                RecoveryObservation = null,
                // Waiting for explicit original entry is not an admitted provider Running state.
                State = TaskExecutionLifecycle.Suspended,
                UpdatedAt = _time.GetUtcNow()
            };
            binding.OriginalContinuationWrite = AcquireOriginalContinuationCall(() => PersistOriginalWriteOnlyAsync(proposed, token,
                actual => Track("continuation.repository-write", actual)));
            _ = Track("continuation.task-write", binding.OriginalContinuationWrite);
            binding.Acknowledged = await binding.OriginalContinuationWrite.ConfigureAwait(false);
        }
        catch (Exception failure)
        { Keep(failure, activeOriginal is { IsFaulted: true } or { IsCanceled: true } ? activeOriginal : null); }
        finally
        {
            // Independently materialize and join the SAME close originals; an acknowledged
            // CAS is never replayed because its pin/permission cleanup later failed.
            if (actualPin is not null)
                try
                {
                    binding.OriginalPinClose = AcquireOriginalContinuationCall(() => actualPin.DisposeAsync().AsTask());
                    _ = Track("continuation.pin-close", binding.OriginalPinClose);
                    await binding.OriginalPinClose.ConfigureAwait(false);
                }
                catch (Exception cause) { Keep(cause, binding.OriginalPinClose); }
            if (binding.OriginalPermissionLease is not null)
                try
                {
                    binding.OriginalPermissionClose = AcquireOriginalContinuationCall(() => binding.OriginalPermissionLease.DisposeAsync().AsTask());
                    _ = Track("continuation.permission-close", binding.OriginalPermissionClose);
                    await binding.OriginalPermissionClose.ConfigureAwait(false);
                }
                catch (Exception cause) { Keep(cause, binding.OriginalPermissionClose); }
        }
        // Preserve every direct fault from an admitted raw stage BEFORE classifying
        // this preparation's effects/cleanup or retaining a fail-stop observation.
        // Await exposes only its first cause; the original Task owns all siblings.
        foreach (var actual in binding.ActualStages)
            if (actual.IsFaulted && actual.Exception is { } fault)
                foreach (var cause in fault.InnerExceptions)
                {
                    if (!failures.Any(existing => ReferenceEquals(existing, cause))) failures.Add(cause);
                    if (cause is OperationCanceledException) faultedOrUnknownCancellationCauses.Add(cause);
                }
        try
        {
            if (failures.Count == 0 && binding.Acknowledged is { } acknowledged)
            {
                // No external callbacks or store reads under the process-local transfer gate.
                lock (_invocationAdmissionGate)
                {
                    if (!_originalInvocations.TryUpdate(acknowledged.TaskId, binding.Next, original))
                        throw new InvalidOperationException("Another original replaced this SAME invocation during continuation acknowledgment.");
                    transferred = true;
                    var next = binding.Next;
                    next.OriginalUnstartedContinuation = binding;
                    next.OriginalBinding = acknowledged;
                    next.BoundByActualBegin = true; // SAME retained actual successful Begin, not durable-state inference.
                    next.OriginalBegin = original.OriginalBegin;
                    next.OriginalChatOwner = original.OriginalChatOwner;
                    next.OriginalConversation = original.OriginalConversation;
                    next.OriginalPersistedConversation = original.OriginalPersistedConversation;
                    next.OriginalUserMessage = original.OriginalUserMessage;
                    next.OriginalUserMessagePublished = original.OriginalUserMessagePublished;
                    next.OriginalConversationWrite = original.OriginalConversationWrite;
                    next.OriginalUserMessageWrite = original.OriginalUserMessageWrite;
                    next.OriginalInputCurrentness = original.OriginalInputCurrentness;
                    next.OriginalContinuationFactory = original.OriginalContinuationFactory;
                    binding.OriginalObservation = new(acknowledged.TaskId, acknowledged.ContextId, acknowledged.ExecutionId,
                        null, acknowledged.PersistenceRevision);
                }
                // Observer callbacks occur only after pin and permission cleanup. Their failure
                // is separately recorded by the existing acknowledged-snapshot owner.
                PublishAcknowledgedSnapshot(acknowledged);
                // The actual orchestration returns only after all real cleanup and publication.
            }
            else if (failures.Count == 0) Keep(new InvalidOperationException("No actual continuation write acknowledgment exists."));
        }
        catch (Exception transferFailure) { Keep(transferFailure); }
        if (failures.Count > 0)
        {
            if (!transferred)
            {
                // This candidate ran no body/effects. Prior custody and its real faults remain.
                ReleaseHealthyOrUnstartedInvocation(binding.Next);
                var preCasSafeRefusal = binding.OriginalContinuationWrite is null
                    && (binding.OriginalPinAcquisition is null || binding.OriginalPinAcquisition.IsCompletedSuccessfully)
                    && (binding.OriginalPermissionAcquisition is null
                        || binding.OriginalPermissionAcquisition.IsCompletedSuccessfully
                        && binding.OriginalPermissionLease is not null
                        && binding.OriginalPermissionClose is { IsCompletedSuccessfully: true })
                    && (actualPin is null || binding.OriginalPinClose is { IsCompletedSuccessfully: true });
                if (preCasSafeRefusal)
                {
                    // Only this actual producer knows CAS/dispatch was never invoked.
                    // Keep the failed full Task and raw stages; a new explicit preparation
                    // gets fresh authority. No Task run/status/history is cleared here.
                    lock (_invocationAdmissionGate)
                        if (_unstartedContinuations.TryGetValue(inspection.ActualSnapshot.TaskId, out var same)
                            && ReferenceEquals(same, binding)) _unstartedContinuations.Remove(inspection.ActualSnapshot.TaskId);
                }
                else foreach (var failure in failures) original.Retain(failure);
                if (binding.Acknowledged is not null)
                    try
                    {
                        var actualObservation = Track("continuation.failed-terminal-observation", AcquireOriginalContinuationCall(() =>
                            ObserveOriginalInvocationTerminalAsync(original)));
                        await actualObservation.ConfigureAwait(false);
                    }
                    catch (Exception observationFailure) { Keep(observationFailure); }
            }
        }
        // Include every direct raw Task sibling (faulted OCE included), not just await's first cause.
        foreach (var actual in binding.ActualStages)
            if (actual.IsFaulted && actual.Exception is { } fault)
                foreach (var cause in fault.InnerExceptions)
                {
                    if (!failures.Any(existing => ReferenceEquals(existing, cause))) failures.Add(cause);
                    if (cause is OperationCanceledException) faultedOrUnknownCancellationCauses.Add(cause);
                }
        Volatile.Write(ref phase.Active, 0);
        _continuationPhase.Value = phase.Previous;
        if (failures.Count == 1 && (failures[0] is not OperationCanceledException
            || !faultedOrUnknownCancellationCauses.Contains(failures[0])))
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0)
            throw new AggregateException("The actual never-started continuation preparation failed; full originals are retained.", failures);
        return binding;
    }

    private void RetireResolvedOriginalUnstartedContinuation(TaskRunInvocationCustody completing,
        TaskExecutionSnapshot current)
    {
        if (completing.OriginalUnstartedContinuation is not { } binding) return;
        var completed = completing.OriginalCompletion;
        var suspended = binding.OriginalSuspendedObservation;
        var originalRecovery = suspended?.RecoveryObservation;
        if (!ReferenceEquals(binding.Issuer, this) || !ReferenceEquals(binding.Next, completing)
            || !binding.Claimed || !binding.Bound || binding.Preparation is not { IsCompletedSuccessfully: true }
            || !ReferenceEquals(binding.Preparation.Result, binding) || suspended is null || originalRecovery is null
            || !binding.Original.CanReturnPublishedPermissionRefusal(suspended)
            || binding.OriginalPermissionClose is not { IsCompletedSuccessfully: true }
            || binding.OriginalPinClose is not { IsCompletedSuccessfully: true }
            || completed is not { IsCompletedSuccessfully: true }
            || completing.OriginalOwningCompletion is not { IsCompletedSuccessfully: true }
            || completing.OriginalSettlement is not { IsCompletedSuccessfully: true }
            || completing.Causes.Count != 0 || !completing.OwnedCleanupTerminal || !completing.ResourcesDisposed
            || completing.OriginalMoves.Any(actual => !actual.IsCompletedSuccessfully)
            || completing.OriginalDispose is not { IsCompletedSuccessfully: true }
            || completing.OriginalTracker is not null && completing.OriginalTrackerDispose is not { IsCompletedSuccessfully: true }
            || current.TaskId != suspended.TaskId || current.ContextId != suspended.ContextId
            || current.ExecutionId != suspended.ExecutionId || current.OwnerBinding != suspended.OwnerBinding
            || current.State != TaskExecutionLifecycle.Completed || current.RecoveryObservation is not null
            || current.Attempts.LastOrDefault()?.State != TaskRunAttemptState.Completed
            || !current.RecoveryHistory.Any(history => history.ObservationId == originalRecovery.ObservationId
                && (history with { Causes = originalRecovery.Causes }) == originalRecovery))
            throw new InvalidOperationException("Only the SAME acknowledged resumed completion and all original cleanup can retire continuation custody.");
        RequireCompletionBasis(completed.Result, current);
        lock (_invocationAdmissionGate)
        {
            if (binding.OriginalResolution is not null) return;
            if (binding.RetirementInvoked || !_unstartedContinuations.TryGetValue(current.TaskId, out var actual)
                || !ReferenceEquals(actual, binding) || !_originalInvocations.TryGetValue(current.TaskId, out var next)
                || !ReferenceEquals(next, completing))
                throw new InvalidOperationException("No exact unconsumed successful continuation retirement exists.");
            binding.RetirementInvoked = true;
        }
        var (_, publication, owner) = binding.Original.RequireOriginalPublishedPermission();
        // The source owner checks all genuine scope/approval/decision/telemetry originals.
        // A failure is retained by the owning terminal observer; neither slot is released.
        owner.RetireResolvedOriginal(publication.Result.Id);
        var receipt = new TaskRunResolvedUnstartedContinuationReceipt(this, binding, binding.Preparation,
            completed, binding.Original.CaptureRecoveryOriginals(), current);
        lock (_invocationAdmissionGate)
        {
            if (!_unstartedContinuations.TryGetValue(current.TaskId, out var actual) || !ReferenceEquals(actual, binding)
                || !_originalInvocations.TryGetValue(current.TaskId, out var next) || !ReferenceEquals(next, completing))
                throw new InvalidOperationException("The actual continuation custody changed before healthy retirement acknowledgment.");
            binding.OriginalResolution = receipt; // Exact originals remain on SAME completed invocation/binding.
            ReleaseHealthyOrUnstartedInvocation(binding.Original);
            ReleaseHealthyOrUnstartedInvocation(completing);
            _unstartedContinuations.Remove(current.TaskId);
            _unstartedPreparationCounts.Remove(current.TaskId);
        }
        ObserveEvent(current, new ExecutionEvent(Guid.NewGuid(), current.ExecutionId, binding.Original.ObservationId,
            current.LastCheckpointActionId, ExecutionOrigin.Haven, ExecutionActionType.Resume,
            ExecutionActionStatus.Completed, "Original never-started continuation completed", null, null,
            "task-coordination", _time.GetUtcNow(), TaskId: current.TaskId,
            SafeMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["resolutionKind"] = "live-never-started-original",
                ["originalObservationId"] = binding.Original.ObservationId.ToString(),
                ["acknowledgedRevision"] = current.PersistenceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })); // Pure observation failures retain their cause separately; no grant or replay.
    }

    internal IAsyncEnumerable<ChatStreamEvent> ClaimOriginalUnstartedContinuation(
        TaskRunUnstartedContinuationBinding binding, ChatSessionService sameChat, CancellationToken token)
    {
        RefuseOriginalContinuationSelfJoin();
        token.ThrowIfCancellationRequested();
        lock (_invocationAdmissionGate)
        {
            if (!ReferenceEquals(binding.Issuer, this) || !ReferenceEquals(binding.SameChat, sameChat)
                || binding.Preparation is not { IsCompletedSuccessfully: true }
                || !ReferenceEquals(binding.Preparation.Result, binding) || binding.Claimed
                || binding.Acknowledged is null || binding.OriginalObservation is null
                || !_originalInvocations.TryGetValue(binding.Acknowledged.TaskId, out var actual) || !ReferenceEquals(actual, binding.Next))
                throw new InvalidOperationException("No unconsumed actual prepared original continuation exists.");
            binding.Claimed = true;
        }
        return AcquireOriginalContinuationCall(() => binding.Original.OriginalContinuationFactory!(binding.Next, binding.OriginalObservation!, token));
    }

    private async Task<TaskExecutionSnapshot> BindAcknowledgedUnstartedContinuationAsync(
        TaskRunUnstartedContinuationBinding binding, TaskRunInvocationCustody next, ProviderExecutionContext observation,
        Guid actualContextId, CancellationToken token)
    {
        RequireOriginalInvocation(next);
        if (!ReferenceEquals(binding.Issuer, this) || !ReferenceEquals(binding.Next, next)
            || !ReferenceEquals(binding.OriginalObservation, observation) || !binding.Claimed || binding.Bound
            || binding.Preparation is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("Only the original privately prepared one-use continuation can bind.");
        var current = await RequireAsync(observation.TaskId, token).ConfigureAwait(false);
        if (current.ContextId != actualContextId || current.ContextId != observation.ContextId || current.ExecutionId != observation.ExecutionId
            || current.PersistenceRevision != observation.PersistenceRevision || current.OwnerBinding != binding.Acknowledged!.OwnerBinding
            || current.RecoveryObservation is not null || current.Attempts.Count != 0
            || current.State is TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled or TaskExecutionLifecycle.Failed)
            throw new InvalidOperationException("The prepared SAME never-started task/run was changed before explicit entry.");
        await ValidateTaskCommandAsync(current, "task:enter-never-started-original", token).ConfigureAwait(false);
        await binding.Original.OriginalInputCurrentness!(token).ConfigureAwait(false);
        var afterInput = await RequireAsync(observation.TaskId, token).ConfigureAwait(false);
        if (afterInput.OwnerBinding != current.OwnerBinding || afterInput.ContextId != current.ContextId
            || afterInput.ExecutionId != current.ExecutionId || afterInput.PersistenceRevision != observation.PersistenceRevision
            || afterInput.RecoveryObservation is not null || afterInput.Attempts.Count != 0)
            throw new InvalidOperationException("The prepared Task/Run changed during accepted-input verification.");
        await ValidateTaskCommandAsync(afterInput, "task:enter-never-started-original", token).ConfigureAwait(false);
        lock (_invocationAdmissionGate)
        {
            if (binding.Bound) throw new InvalidOperationException("The original continuation was already bound.");
            binding.Bound = true;
        }
        return afterInput;
    }
}
