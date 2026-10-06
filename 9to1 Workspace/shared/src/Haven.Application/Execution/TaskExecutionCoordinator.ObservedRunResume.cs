using Haven.Core;

namespace Haven.Application;

/// <summary>One actual same-run continuation and independent presentation originals.
/// The process source cohort, not a view, retains and joins the business driver.</summary>
internal sealed class HostedOriginalRunResume(TaskExecutionCoordinator issuer, object marker,
    CanonicalChatProcessProducer source, ProviderExecutionContext observedContext,
    TaskRunToolCheckpointContinuationBinding? toolCheckpoint = null)
{
    internal readonly object Gate = new();
    internal readonly TaskExecutionCoordinator Issuer = issuer;
    internal readonly object Marker = marker;
    internal readonly CanonicalChatProcessProducer Source = source;
    internal readonly ProviderExecutionContext ObservedContext = observedContext;
    internal readonly TaskRunToolCheckpointContinuationBinding? ToolCheckpoint = toolCheckpoint;
    internal readonly AgentRuntimeOriginalCustody ProducerCustody = new();
    internal readonly AgentRuntimeOriginalCustody WaitCustody = new();
    internal readonly AgentRuntimeOriginalCustody DetachCustody = new();
    internal readonly AgentRuntimeOriginalCustody ResolutionCustody = new();
    internal readonly TaskCompletionSource Detached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskRunOriginalResumeObservationLease Lease = null!;
    internal Task<TaskRunOriginalResumeObservationResult>? Producer;
    internal Task<TaskRunOriginalResumeObservationResult>? Wait;
    internal Task? Detach;
    internal Task<TaskExecutionSnapshot>? Resolution;
    internal readonly TaskCompletionSource DetachRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskRunInvocationCustody? ActualRun;
    internal bool RetirementRequested;
    internal bool Healthy
    {
        get
        {
            lock (Gate) return ProducerCustody.Healthy && WaitCustody.Healthy
                && Detach is not null && DetachCustody.Healthy && ActualRun is not null
                && (ToolCheckpoint is not null
                    ? ReferenceEquals(ToolCheckpoint.Next, ActualRun) && ReferenceEquals(ActualRun.OriginalProcessProducer, Source)
                        && Resolution is { IsCompletedSuccessfully: true } && ResolutionCustody.Healthy
                    : ReferenceEquals(ActualRun.OriginalProcessProducer, Source.OriginalContinuationCustody?.OriginalChild))
                && Source.HasHealthyClosedOriginal;
        }
    }
}

public sealed partial class TaskExecutionCoordinator
{
    private readonly object _originalRunObservationIssuer = new();
    private readonly List<HostedOriginalRunResume> _hostedOriginalRunResumes = [];
    private HostedOriginalRunResume[]? _sealedHostedOriginalRunResumes;

    /// <summary>Starts the retained same-input continuation under this durable process owner.
    /// The command token withdraws preparation only; the resulting business iterator has its
    /// own process lifetime. The returned lease observes, and cannot cancel, that producer.</summary>
    public Task<TaskRunOriginalResumeObservationLease> StartObservedOriginalRunResumeAsync(
        Guid taskId, Guid expectedExecutionId, CancellationToken cancellationToken)
    {
        CanonicalChatProcessProducer.DemandExternalOwnerJoin(this);
        TaskRunProcessProducerContext.DemandExternalJoin(this);
        return StartOriginalProcessStage("start-observed-original-run-resume", cancellationToken, async token =>
        {
            var stage = RequireOriginalProcessStage();
            var before = await RequireOriginalProcessSnapshotAsync(taskId, token).ConfigureAwait(false);
            RequireRun(before, expectedExecutionId);
            await ValidateOriginalProcessCommandAsync(before, "task:start-observed-original-run-resume", token).ConfigureAwait(false);
            var previous = TryGetOriginalRunControlSource(before)
                ?? throw new InvalidOperationException("No genuine original task input remains available.");
            if (previous.OriginalToolCheckpoint is not null && previous.OriginalProviderInvocationInvoked)
            {
                var chat = previous.OriginalChatOwner ?? throw new InvalidOperationException("The actual original Chat owner is unavailable.");
                var prepared = await stage.Await(() => PrepareOriginalToolCheckpointContinuationAsync(previous, chat, token)).ConfigureAwait(false);
                var actualSource = stage.Invoke(() => ClaimOriginalToolCheckpointContinuation(prepared, chat, CancellationToken.None))
                    as CanonicalChatProcessProducer ?? throw new InvalidOperationException("No actual checkpoint continuation producer was registered.");
                var resumed = await RequireOriginalProcessSnapshotAsync(taskId, token).ConfigureAwait(false);
                RequireRun(resumed, expectedExecutionId);
                if (!ReferenceEquals(TryGetOriginalRunControlSource(resumed), prepared.Next)
                    || resumed.ContextId != before.ContextId || resumed.OwnerBinding != before.OwnerBinding
                    || resumed.Attempts.LastOrDefault()?.Id != prepared.NewAdmission?.AttemptId)
                    throw new InvalidOperationException("The actual acknowledged checkpoint owner/run/attempt changed before host publication.");
                await ValidateOriginalProcessCommandAsync(resumed, "task:start-observed-original-tool-checkpoint", token).ConfigureAwait(false);
                return RegisterHostedOriginalRunResume(actualSource, OriginalRunControlContext(resumed), stage, prepared).Lease;
            }
            // Never pass a view/command token to the actual resumed business stream.
            var source = await AcquireOriginalRunResumeBodyAsync(taskId, expectedExecutionId, token, CancellationToken.None).ConfigureAwait(false)
                as CanonicalChatProcessProducer ?? throw new InvalidOperationException("No actual retained canonical continuation source exists.");
            var current = await RequireOriginalProcessSnapshotAsync(taskId, token).ConfigureAwait(false);
            RequireRun(current, expectedExecutionId);
            var original = TryGetOriginalRunControlSource(current)
                ?? throw new InvalidOperationException("The actual original task input changed before hosted continuation publication.");
            RequireOriginalNeverStarted(original, current);
            await ValidateOriginalProcessCommandAsync(current, "task:start-observed-original-run-resume", token).ConfigureAwait(false);
            var hosted = RegisterHostedOriginalRunResume(source, OriginalRunControlContext(current), stage);
            return hosted.Lease;
        });
    }

    public bool IsIssuedOriginalRunResumeObservation(TaskRunOriginalResumeObservationLease lease) =>
        lease is not null && ReferenceEquals(lease.Issuer, this)
        && lease.Original is HostedOriginalRunResume original
        && ReferenceEquals(original.Issuer, this) && ReferenceEquals(original.Marker, _originalRunObservationIssuer)
        && ReferenceEquals(original.Lease, lease);

    private HostedOriginalRunResume RegisterHostedOriginalRunResume(CanonicalChatProcessProducer source,
        ProviderExecutionContext observedContext, TaskRunProcessStageCustody stage,
        TaskRunToolCheckpointContinuationBinding? toolCheckpoint = null)
    {
        var original = new HostedOriginalRunResume(this, _originalRunObservationIssuer, source, observedContext, toolCheckpoint);
        original.ProducerCustody.OriginalProcessOwner = this;
        // Observation callbacks join only their own Wait/Detach cohort. Inherited business
        // ancestry cannot create a dependency on a producer that this detacher never joins.
        original.WaitCustody.OriginalProcessOwner = original;
        original.DetachCustody.OriginalProcessOwner = original;
        original.ResolutionCustody.OriginalProcessOwner = this;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        original.Lease = new(this, original, observedContext,
            () => original.Wait ?? throw new InvalidOperationException("No original hosted observation wait was published."),
            () => RequestHostedObservationRetirement(original), () => JoinHostedObservationRetirement(original));
        lock (_processProducerGate)
        {
            if (_processProducerAdmissionSealed)
                throw new InvalidOperationException("Hosted canonical continuation admission is sealed.");
            _hostedOriginalRunResumes.RemoveAll(static prior => prior.Healthy);
            if (_hostedOriginalRunResumes.Count >= OriginalInvocationCapacity)
                throw new InvalidOperationException("Retained hosted continuation originals require inspection.");
            // Publish the SAME real driver and observation before any source factory callback.
            original.Producer = original.ProducerCustody.Start(async operation =>
            {
                await start.Task.ConfigureAwait(false);
                return await RunHostedOriginalContinuationAsync(original, operation).ConfigureAwait(false);
            });
            if (toolCheckpoint is not null)
            {
                if (toolCheckpoint.OriginalHost is not null)
                    throw new InvalidOperationException("The SAME checkpoint has already acquired an actual host.");
                toolCheckpoint.OriginalHost = original;
                // Prepublish the real independent resolution driver before business callbacks.
                // Detaching a view cannot stop this business/cleanup receipt owner.
                original.Resolution = original.ResolutionCustody.Start(async operation =>
                {
                    await start.Task.ConfigureAwait(false);
                    _ = await operation.AwaitAsync(() => original.Producer!, owningCleanup: true).ConfigureAwait(false);
                    toolCheckpoint.OriginalResolutionValidation = operation.Invoke(() =>
                        ValidateOriginalToolCheckpointResolutionAsync(toolCheckpoint, original));
                    var acknowledged = await operation.AwaitAsync(() => toolCheckpoint.OriginalResolutionValidation,
                        owningCleanup: true).ConfigureAwait(false);
                    CommitResolvedOriginalToolCheckpointContinuation(toolCheckpoint, original, acknowledged);
                    return acknowledged;
                });
                toolCheckpoint.OriginalResolutionDriver = original.Resolution;
                stage.RetainSource(original.Resolution);
            }
            original.Wait = original.WaitCustody.Start(async operation =>
            {
                await start.Task.ConfigureAwait(false);
                Task producer = original.Resolution ?? (Task?)original.Producer
                    ?? throw new InvalidOperationException("No real hosted producer was published.");
                var selected = await Task.WhenAny(producer, original.Detached.Task).ConfigureAwait(false);
                lock (original.Gate)
                    if (original.RetirementRequested)
                        return new TaskRunOriginalResumeObservationResult(TaskRunOriginalResumeObservationDisposition.ObservationDetached, null, null, false);
                if (ReferenceEquals(selected, original.Detached.Task))
                    return new(TaskRunOriginalResumeObservationDisposition.ObservationDetached, null, null, false);
                if (original.Resolution is { } resolution)
                    _ = await operation.AwaitAsync(() => resolution, owningCleanup: true).ConfigureAwait(false);
                return await operation.AwaitAsync(() => original.Producer!, owningCleanup: true).ConfigureAwait(false);
            });
            // This actual detacher is issued with the observer, before any callback can reenter.
            // Requesting retirement later only opens its gate; it creates no post-seal original.
            original.Detach = original.DetachCustody.Start(async operation =>
            {
                await start.Task.ConfigureAwait(false);
                await original.DetachRequested.Task.ConfigureAwait(false);
                original.Detached.TrySetResult();
                var wait = original.Wait ?? throw new InvalidOperationException("No original hosted wait exists.");
                _ = await operation.AwaitAsync(() => wait, owningCleanup: true).ConfigureAwait(false);
                return true;
            });
            _hostedOriginalRunResumes.Add(original);
            stage.RetainSource(original.Producer);
            stage.RetainSource(original.Wait);
            stage.RetainSource(original.Detach);
            stage.OriginalResultClosed = () => original.Healthy;
            // The registered outer is requested by the SAME request-all process cohort before
            // this source stage joins its encompassing actual business driver.
            stage.OriginalResultJoin = () => source.ActualClose ?? source.CloseAndSuspendOriginalProducerAsync();
        }
        start.SetResult();
        return original;
    }

    private async Task<TaskRunOriginalResumeObservationResult> RunHostedOriginalContinuationAsync(
        HostedOriginalRunResume original, AgentRuntimeOriginalCustody operation)
    {
        IAsyncEnumerator<ChatStreamEvent>? iterator = null;
        try
        {
            iterator = operation.Invoke(() =>
            {
                DemandHostedOriginalSourceAdmission();
                return original.Source.GetAsyncEnumerator(CancellationToken.None);
            });
            while (await operation.AwaitAsync(() =>
            {
                DemandHostedOriginalSourceAdmission();
                return iterator.MoveNextAsync().AsTask();
            }).ConfigureAwait(false))
            {
                _ = operation.Invoke(() => iterator.Current); // Capture Current within its actual source callback.
                AttachHostedActualRun(original);
            }
            AttachHostedActualRun(original);
        }
        finally
        {
            if (iterator is not null)
                await operation.AwaitAsync(() => iterator.DisposeAsync().AsTask(), owningCleanup: true).ConfigureAwait(false);
        }
        var actual = original.ActualRun ?? throw new InvalidOperationException("The hosted continuation never bound its genuine same-run input/creation receipt.");
        var terminal = actual.OriginalTerminalObservation
            ?? throw new InvalidOperationException("No actual hosted producer terminal acknowledgment exists.");
        if (terminal.TaskId != original.ObservedContext.TaskId || terminal.ContextId != original.ObservedContext.ContextId
            || terminal.ExecutionId != original.ObservedContext.ExecutionId || !actual.OwnedCleanupTerminal
            || !original.Source.HasHealthyClosedOriginal)
            throw new InvalidOperationException("The SAME hosted original has no fully closed acknowledged terminal observation.");
        return new(TaskRunOriginalResumeObservationDisposition.ProducerTerminal, OriginalRunControlContext(terminal),
            terminal.State, terminal.RecoveryObservation is not null);
    }

    private void DemandHostedOriginalSourceAdmission()
    {
        if (_processProducerAdmissionSealed)
            throw new InvalidOperationException("Hosted canonical source callback admission is sealed.");
    }

    private void AttachHostedActualRun(HostedOriginalRunResume observation)
    {
        if (observation.ToolCheckpoint is { } checkpoint)
        {
            if (!ReferenceEquals(checkpoint.Issuer, this) || !checkpoint.Claimed || !checkpoint.Bound
                || !ReferenceEquals(checkpoint.OriginalHost, observation)
                || !ReferenceEquals(checkpoint.Next.OriginalProcessProducer, observation.Source))
                throw new InvalidOperationException("The actual hosted checkpoint did not bind its SAME issued input/run.");
            AttachHostedActualRun(observation, checkpoint.Next);
            return;
        }
        if (observation.Source.OriginalContinuationCustody?.OriginalColdPreparation is { BodyBound: true } cold)
        {
            if (!ReferenceEquals(cold.Owner, this)
                || !ReferenceEquals(cold.Invocation.OriginalProcessProducer, observation.Source.OriginalContinuationCustody.OriginalChild))
                throw new InvalidOperationException("The hosted cold source did not bind the same private reconstructed input.");
            AttachHostedActualRun(observation, cold.Invocation);
            return;
        }
        var binding = observation.Source.OriginalContinuationCustody?.OriginalPreparation;
        if (binding is not { Claimed: true, Bound: true } || !ReferenceEquals(binding.Issuer, this)
            || !ReferenceEquals(binding.Next.OriginalProcessProducer, observation.Source.OriginalContinuationCustody?.OriginalChild))
            throw new InvalidOperationException("No actual same-input hosted continuation binding was acknowledged.");
        AttachHostedActualRun(observation, binding.Next);
    }

    private static void AttachHostedActualRun(HostedOriginalRunResume observation, TaskRunInvocationCustody next)
    {
        lock (observation.Gate)
        {
            if (observation.ActualRun is not null)
            {
                if (!ReferenceEquals(observation.ActualRun, next))
                    throw new InvalidOperationException("Another invocation cannot replace the hosted producer.");
                return;
            }
            observation.ActualRun = next;
            next.RetainAdditionalOriginal("host.actual-business-driver", observation.Producer!);
            next.RetainAdditionalOriginal("host.actual-observation-wait", observation.Wait!);
            next.RetainAdditionalOriginal("host.actual-observation-detach", observation.Detach!);
            if (observation.Resolution is { } resolution)
                next.RetainAdditionalOriginal("host.actual-checkpoint-resolution", resolution);
        }
    }

    private void RequestHostedObservationRetirement(HostedOriginalRunResume original) =>
        _ = StartHostedObservationRetirement(original); // Actual coalesced driver is retained; no business stop/cancellation.

    private Task JoinHostedObservationRetirement(HostedOriginalRunResume original)
    {
        // Refuse an actual live/physical observer self-join before returning even the
        // existing Task. Business and global process joins retain their broader guards.
        TaskRunProcessProducerContext.DemandExternalJoin(original);
        return StartHostedObservationRetirement(original);
    }

    private Task StartHostedObservationRetirement(HostedOriginalRunResume original)
    {
        Task actual;
        lock (original.Gate)
        {
            original.RetirementRequested = true;
            actual = original.Detach ?? throw new InvalidOperationException("No actual source-issued observer retirement driver exists.");
        }
        original.DetachRequested.TrySetResult();
        return actual;
    }
}
