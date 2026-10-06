using System.Runtime.CompilerServices;
using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator
{
    private readonly object _runControlGate = new();
    private readonly ConditionalWeakTable<TaskRunInvocationCustody, OriginalRunControls> _originalRunControls = new();
    private sealed class OriginalRunControls
    {
        internal readonly Dictionary<TaskRunOriginalRunControlKind, OriginalRunControl> Requests = [];
    }
    private sealed class OriginalRunControl(TaskRunInvocationCustody original, CanonicalChatProcessProducer producer,
        TaskRunOriginalRunControlKind kind, TaskRunProcessStageCustody stage, Task<TaskRunOriginalRunControlResult> driver)
    {
        internal readonly TaskRunInvocationCustody Original = original;
        internal readonly CanonicalChatProcessProducer Producer = producer;
        internal readonly TaskRunOriginalRunControlKind Kind = kind;
        internal readonly TaskRunProcessStageCustody Stage = stage;
        internal readonly Task<TaskRunOriginalRunControlResult> Driver = driver;
        internal Task? OriginalClose;
        internal Task<TaskExecutionSnapshot>? OriginalCancellationWrite;
    }

    public Task<TaskRunOriginalRunControlAvailability> GetOriginalRunControlAvailabilityAsync(
        Guid taskId, Guid expectedExecutionId, CancellationToken cancellationToken) =>
        StartOriginalProcessStage("observe-original-run-controls", cancellationToken, async token =>
        {
            var current = await RequireOriginalProcessSnapshotAsync(taskId, token).ConfigureAwait(false);
            RequireRun(current, expectedExecutionId);
            await ValidateOriginalProcessCommandAsync(current, "task:observe-original-controls", token).ConfigureAwait(false);
            current = await RequireOriginalProcessSnapshotAsync(taskId, token).ConfigureAwait(false);
            RequireRun(current, expectedExecutionId);
            await ValidateOriginalProcessCommandAsync(current, "task:observe-original-controls", token).ConfigureAwait(false);
            var live = TryGetOriginalRunControlSource(current);
            var canWithdraw = live is not null && current.State is not (TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled);
            return new TaskRunOriginalRunControlAvailability(OriginalRunControlContext(current), canWithdraw, canWithdraw,
                live is not null && HasOriginalUnstartedRunResume(live, current));
        });

    public Task<TaskRunOriginalRunControlResult> PauseOriginalRunAsync(
        Guid taskId, Guid expectedExecutionId, CancellationToken cancellationToken) =>
        StartOriginalRunControl(taskId, expectedExecutionId, TaskRunOriginalRunControlKind.Pause, cancellationToken);

    public Task<TaskRunOriginalRunControlResult> StopOriginalRunAsync(
        Guid taskId, Guid expectedExecutionId, CancellationToken cancellationToken) =>
        StartOriginalRunControl(taskId, expectedExecutionId, TaskRunOriginalRunControlKind.Stop, cancellationToken);

    private Task<TaskRunOriginalRunControlResult> StartOriginalRunControl(Guid taskId, Guid expectedExecutionId,
        TaskRunOriginalRunControlKind kind, CancellationToken cancellationToken)
    {
        // Pure synchronous preflight before an awaitable command can reenter its own source.
        CanonicalChatProcessProducer.DemandExternalOwnerJoin(this);
        TaskRunProcessProducerContext.DemandExternalJoin(this);
        return StartOriginalProcessStage(kind == TaskRunOriginalRunControlKind.Pause ? "pause-original-run" : "stop-original-run",
            cancellationToken, token => ControlOriginalRunBodyAsync(taskId, expectedExecutionId, kind, token));
    }

    private async Task<TaskRunOriginalRunControlResult> ControlOriginalRunBodyAsync(Guid taskId, Guid expectedExecutionId,
        TaskRunOriginalRunControlKind kind, CancellationToken token)
    {
        var stage = RequireOriginalProcessStage();
        var current = await RequireOriginalProcessSnapshotAsync(taskId, token).ConfigureAwait(false);
        RequireRun(current, expectedExecutionId);
        await ValidateOriginalProcessCommandAsync(current, kind == TaskRunOriginalRunControlKind.Pause ? "task:pause-original" : "task:stop-original", token).ConfigureAwait(false);
        var original = TryGetOriginalRunControlSource(current)
            ?? throw new InvalidOperationException("The SAME live original task input/producer is unavailable; durable IDs cannot create a stop or recovery witness.");
        var producer = original.OriginalProcessProducer!;
        producer.DemandExternalOriginalJoin();
        if (current.Attempts.LastOrDefault() is { State: TaskRunAttemptState.Admitted or TaskRunAttemptState.Running } attempt)
        {
            var issued = await stage.Await(() => GetIssuedAttemptAsync(taskId, expectedExecutionId, attempt.Id, token)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The actual current original attempt owner is unavailable.");
            await stage.Await(() => issued.Lease.RevalidateAsync(token).AsTask()).ConfigureAwait(false);
        }
        current = await RequireOriginalProcessSnapshotAsync(taskId, token).ConfigureAwait(false);
        RequireRun(current, expectedExecutionId);
        if (!ReferenceEquals(TryGetOriginalRunControlSource(current), original))
            throw new InvalidOperationException("The current original task/run owner changed during command authorization.");
        await ValidateOriginalProcessCommandAsync(current, kind == TaskRunOriginalRunControlKind.Pause ? "task:pause-original" : "task:stop-original", token).ConfigureAwait(false);
        OriginalRunControl request;
        OriginalRunControl? prior = null;
        lock (_runControlGate)
        {
            var controls = _originalRunControls.GetValue(original, static _ => new OriginalRunControls());
            if (controls.Requests.TryGetValue(kind, out var retained)) prior = retained;
            if (prior is not null) request = prior;
            else
            {
                if (controls.Requests.Values.Any(actual => !actual.Driver.IsCompleted))
                    throw new InvalidOperationException("The actual original task withdrawal is already pending; join it before another command.");
                var actualDriver = stage.ActualDriver as Task<TaskRunOriginalRunControlResult>
                    ?? throw new InvalidOperationException("The actual original run command driver was not published.");
                request = new(original, producer, kind, stage, actualDriver);
                controls.Requests.Add(kind, request);
            }
        }
        if (prior is not null)
        {
            if (!ReferenceEquals(prior.Original, original) || !ReferenceEquals(prior.Producer, producer) || prior.Kind != kind)
                throw new InvalidOperationException("Another original producer cannot replace this retained command.");
            if (prior.Driver.IsCompletedSuccessfully && prior.Driver.Result.State != current.State)
                throw new InvalidOperationException("The acknowledged task state changed after this original withdrawal command.");
            return await stage.Await(() => prior.Driver, owningCleanup: true).ConfigureAwait(false);
        }
        original.RetainAdditionalOriginal(kind == TaskRunOriginalRunControlKind.Pause ? "command.pause" : "command.stop", request.Driver);
        stage.OriginalResultClosed = () => producer.HasHealthyClosedOriginal;
        stage.OriginalResultJoin = producer.CloseAndSuspendOriginalRunProducerAsync;
        stage.Invoke(() => { producer.RequestOriginalRunWithdrawal(); return true; });
        // A canceled caller may withdraw its wait; it cannot manufacture cleanup success or
        // abort the already requested producer's actual stop/disposal and terminal write.
        request.OriginalClose = stage.Invoke(producer.CloseAndSuspendOriginalRunProducerAsync, owningCleanup: true);
        original.RetainAdditionalOriginal("command.original-producer-close", request.OriginalClose);
        await stage.Await(() => request.OriginalClose
            ?? throw new InvalidOperationException("No actual original producer close was published."), owningCleanup: true).ConfigureAwait(false);
        current = await stage.Await(() => repository.GetAsync(taskId, CancellationToken.None), owningCleanup: true).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original terminal task acknowledgment is unavailable.");
        RequireRun(current, expectedExecutionId);
        if (current.ContextId != original.OriginalBinding!.ContextId || current.OwnerBinding != original.OriginalBinding.OwnerBinding
            || !original.OwnedCleanupTerminal || !producer.HasHealthyClosedOriginal)
            throw new InvalidOperationException("The exact original producer and its cleanup have no terminal acknowledgment.");
        if (original.OriginalCompletion is { IsCompletedSuccessfully: true } completed && current.State == TaskExecutionLifecycle.Completed)
        {
            RequireCompletionBasis(completed.Result, current);
            return OriginalRunControlResult(kind, TaskRunOriginalRunControlDisposition.AlreadyCompletedByOriginal, current, original);
        }
        if (current.State != TaskExecutionLifecycle.Suspended || original.OriginalTerminalObservation is not { } terminal
            || terminal.TaskId != current.TaskId || terminal.ExecutionId != current.ExecutionId
            || terminal.RecoveryObservation?.ObservationId != original.ObservationId
            || current.RecoveryObservation?.ObservationId != original.ObservationId)
            throw new InvalidOperationException("The SAME original run has not acknowledged suspension after its actual cleanup.");
        if (kind == TaskRunOriginalRunControlKind.Stop)
        {
            // Cancellation is a task outcome only at a genuine fully closed no-attempt boundary.
            // Invoked/accepted/unknown work stays suspended for source-owned recovery.
            if (!HasOriginalNeverStartedBinding(original, current) || original.AttemptAdmissionInvoked
                || original.OriginalProviderInvocationInvoked || current.Attempts.Count != 0 || current.Plan.Count != 0
                || current.CheckpointId is not null || current.LastCheckpointActionId is not null)
                throw new InvalidOperationException("The stop request completed, but invoked or accepted work needs original inspection before a cancelled task outcome.");
            await stage.Await(() => ValidateTaskCommandAsync(current, "task:cancel-after-original-close", CancellationToken.None), owningCleanup: true).ConfigureAwait(false);
            var refreshed = await stage.Await(() => repository.GetAsync(taskId, CancellationToken.None), owningCleanup: true).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The current suspended original is unavailable.");
            RequireRun(refreshed, expectedExecutionId);
            RequireCompletionBasis(current, refreshed);
            if (refreshed.State != TaskExecutionLifecycle.Suspended || refreshed.RecoveryObservation?.ObservationId != original.ObservationId)
                throw new InvalidOperationException("The original suspension changed before explicit stop acknowledgment.");
            await stage.Await(() => ValidateTaskCommandAsync(refreshed, "task:cancel-after-original-close", CancellationToken.None), owningCleanup: true).ConfigureAwait(false);
            request.OriginalCancellationWrite = stage.Invoke(() => PersistAsync(refreshed with
            { State = TaskExecutionLifecycle.Cancelled, UpdatedAt = _time.GetUtcNow() }, CancellationToken.None, owningProcessCleanup: true), owningCleanup: true);
            current = await stage.Await(() => request.OriginalCancellationWrite
                ?? throw new InvalidOperationException("No actual cancellation write was published."), owningCleanup: true).ConfigureAwait(false);
            original.OriginalTerminalObservation = current;
            return OriginalRunControlResult(kind, TaskRunOriginalRunControlDisposition.CancelledAfterOriginalClose, current, original);
        }
        return OriginalRunControlResult(kind, TaskRunOriginalRunControlDisposition.Suspended, current, original);
    }

    /// <summary>Acquires the existing same-input never-started continuation. Invoked or accepted
    /// work requires a different actual safe-boundary factory and is explicitly refused here.</summary>
    public Task<IAsyncEnumerable<ChatStreamEvent>> ResumeOriginalRunAsync(
        Guid taskId, Guid expectedExecutionId, CancellationToken cancellationToken)
    {
        CanonicalChatProcessProducer.DemandExternalOwnerJoin(this);
        TaskRunProcessProducerContext.DemandExternalJoin(this);
        return StartOriginalProcessStage("acquire-original-run-resume", cancellationToken,
            token => AcquireOriginalRunResumeBodyAsync(taskId, expectedExecutionId, token, cancellationToken));
    }

    private async Task<IAsyncEnumerable<ChatStreamEvent>> AcquireOriginalRunResumeBodyAsync(
        Guid taskId, Guid expectedExecutionId, CancellationToken token, CancellationToken originalCallerToken)
    {
        var stage = RequireOriginalProcessStage();
        var current = await RequireOriginalProcessSnapshotAsync(taskId, token).ConfigureAwait(false);
        RequireRun(current, expectedExecutionId);
        await ValidateOriginalProcessCommandAsync(current, "task:acquire-original-run-resume", token).ConfigureAwait(false);
        var original = TryGetOriginalRunControlSource(current)
            ?? throw new InvalidOperationException("The original task input producer is unavailable; this task cannot be resumed through a replacement chat.");
        RequireOriginalNeverStarted(original, current);
        if (!HasOriginalUnstartedRunResume(original, current))
            throw new InvalidOperationException("The SAME actual source permission response must complete successfully before original continuation.");
        current = await RequireOriginalProcessSnapshotAsync(taskId, token).ConfigureAwait(false);
        RequireRun(current, expectedExecutionId);
        if (!ReferenceEquals(TryGetOriginalRunControlSource(current), original))
            throw new InvalidOperationException("The genuine original input/owner changed before continuation acquisition.");
        await ValidateOriginalProcessCommandAsync(current, "task:acquire-original-run-resume", token).ConfigureAwait(false);
        RequireOriginalNeverStarted(original, current);
        if (original.OriginalDelegatedChildLink is not null)
            throw new InvalidOperationException("A saved delegated child must use its SAME retained Agent Retry producer.");
        var sameChat = original.OriginalChatOwner ?? throw new InvalidOperationException("The actual original Chat producer is unavailable.");
        var actual = stage.Invoke(() => sameChat.ContinueUnstartedOriginalAsync(taskId, expectedExecutionId, originalCallerToken));
        var outer = actual as CanonicalChatProcessProducer
            ?? throw new InvalidOperationException("The original continuation was not published to the configured process custody owner.");
        stage.OriginalResultClosed = () => outer.HasHealthyClosedOriginal;
        stage.OriginalResultJoin = () => outer.ActualClose ?? outer.CloseAndSuspendOriginalProducerAsync();
        return actual;
    }

    private TaskRunInvocationCustody? TryGetOriginalRunControlSource(TaskExecutionSnapshot current)
    {
        if (!_originalInvocations.TryGetValue(current.TaskId, out var original)
            || !ReferenceEquals(original.Issuer, this) || !ReferenceEquals(original.OriginalSelf, original)
            || original.OriginalBinding is not { } binding || original.OriginalProcessProducer is null
            || binding.TaskId != current.TaskId || binding.ContextId != current.ContextId || binding.ExecutionId != current.ExecutionId
            || binding.OwnerBinding != current.OwnerBinding) return null;
        return original;
    }

    private bool HasOriginalUnstartedRunResume(TaskRunInvocationCustody original, TaskExecutionSnapshot current)
    {
        if (current.State != TaskExecutionLifecycle.Suspended || original.OriginalContinuationFactory is null
            || original.OriginalInputCurrentness is null || original.OriginalChatOwner is null
            || original.OriginalDelegatedChildLink is not null || original.AttemptAdmissionInvoked || original.OriginalProviderInvocationInvoked
            || current.Attempts.Count != 0 || !original.CanReturnPublishedPermissionRefusal(current)) return false;
        var (ask, publication, source) = original.RequireOriginalPublishedPermission();
        return (_unstartedPermissionSource is null || ReferenceEquals(_unstartedPermissionSource, source))
            && source.HasOriginalAllowedUnstartedResponse(ask, publication);
    }

    private TaskRunOriginalRunControlResult OriginalRunControlResult(TaskRunOriginalRunControlKind kind,
        TaskRunOriginalRunControlDisposition disposition, TaskExecutionSnapshot current, TaskRunInvocationCustody original) =>
        new(kind, disposition, OriginalRunControlContext(current), current.State, current.RecoveryObservation is not null,
            HasOriginalUnstartedRunResume(original, current));
    private static ProviderExecutionContext OriginalRunControlContext(TaskExecutionSnapshot current) =>
        new(current.TaskId, current.ContextId, current.ExecutionId, current.Attempts.LastOrDefault()?.Id, current.PersistenceRevision);
}
