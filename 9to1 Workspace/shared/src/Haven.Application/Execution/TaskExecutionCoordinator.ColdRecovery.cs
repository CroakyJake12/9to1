using Haven.Core;
using System.Runtime.CompilerServices;

namespace Haven.Application;

internal sealed class TaskRunColdContinuationBinding(TaskExecutionCoordinator owner, ChatSessionService chat,
    ITaskRunColdJournalEntry entry, ITaskRunColdJournalClaim claim,
    ITaskRunColdJournalAcknowledgment acknowledgment, TaskRunInvocationCustody invocation)
{
    internal readonly TaskExecutionCoordinator Owner = owner;
    internal readonly ChatSessionService Chat = chat;
    internal readonly ITaskRunColdJournalEntry Entry = entry;
    internal readonly ITaskRunColdJournalClaim Claim = claim;
    internal readonly ITaskRunColdJournalAcknowledgment Acknowledgment = acknowledgment;
    internal readonly TaskRunInvocationCustody Invocation = invocation;
    internal bool BodyBound;
    internal ITaskRunColdToolCheckpointSelection? Selection;
    internal TaskRunAttemptAdmission? NewAdmission;
    internal TaskExecutionSnapshot? PreparedTask;
    internal Task<TaskExecutionSnapshot>? OriginalRecoveryWrite;
    internal bool OriginalResponseReserved;
    internal bool JournalTerminalAcknowledged;
}

internal sealed partial class TaskRunInvocationCustody
{
    internal TaskRunColdContinuationBinding? OriginalColdContinuation;
}

public sealed partial class TaskExecutionCoordinator
{
    private ITaskRunColdRecoveryJournal? _coldRecoveryJournal;
    private ITaskRunColdContextAuthority? _coldRecoveryContext;
    private readonly ConditionalWeakTable<TaskRunInvocationCustody, TaskRunColdContinuationBinding> _coldContinuationBindings = new();
    internal ITaskRunColdRecoveryJournal? OriginalColdRecoveryJournal => _coldRecoveryJournal;

    /// <summary>Host composition only, before any producer admission. Neither configuration nor
    /// these interfaces establish recovered authority without their private issuer validators.</summary>
    public void ConfigureOriginalColdRecovery(ITaskRunColdRecoveryJournal journal, ITaskRunColdContextAuthority context)
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(context);
        lock (_processProducerGate)
        {
            if (_processProducerAdmissionSealed || _processChatProducers.Count != 0 || _originalProcessStages.Count != 0
                || _originalInvocations.Count != 0 || _coldRecoveryJournal is not null)
                throw new InvalidOperationException("Cold recovery composition must be fixed before original source admission.");
            _coldRecoveryJournal = journal; _coldRecoveryContext = context;
        }
    }

    /// <summary>Exact configured-instance identity only. No source read, claim, current
    /// authority, protected-store eligibility or restored business admission is established.</summary>
    public bool HasOriginalColdRecoveryComposition(ITaskRunColdRecoveryJournal sameJournal,
        ITaskRunColdContextAuthority sameContext)
    {
        lock (_processProducerGate) return _coldRecoveryJournal is not null &&
            ReferenceEquals(_coldRecoveryJournal, sameJournal) && ReferenceEquals(_coldRecoveryContext, sameContext);
    }

    /// <summary>Reads a source-authenticated closed input as observation only. It does not
    /// claim the capsule, restore an activation, prepare a provider, or establish readiness.</summary>
    public Task<TaskRunColdCapsule?> ObserveOriginalColdInputAsync(Guid taskId, Guid expectedRunId, CancellationToken token) =>
        StartOriginalProcessStage<TaskRunColdCapsule?>("observe-original-cold-input", token, async cancellation =>
        {
            var stage = RequireOriginalProcessStage();
            var journal = _coldRecoveryJournal ?? throw new InvalidOperationException("The actual cold journal is unconfigured.");
            var operation = new AgentRuntimeOriginalCustody { OriginalProcessOwner = this };
            var actual = operation.Start<TaskRunColdCapsule?>(async work =>
            {
                var entry = await work.AwaitAsync(() => journal.ReadOriginalAsync(taskId, expectedRunId,
                    new TaskRunColdOriginalSourceScope(work), cancellation)).ConfigureAwait(false);
                if (entry is null) return null;
                if (!journal.IsIssuedOriginalEntry(entry)) throw new UnauthorizedAccessException("No actual journal issued this observation.");
                return entry.Capsule;
            });
            stage.RetainSource(actual);
            return await stage.Await(() => actual).ConfigureAwait(false);
        });

    /// <summary>Genuine cold recovery of a source-attested closed input boundary. IDs select a row;
    /// only journal/domain/current authority authorize it. Returned business stream is process-owned.</summary>
    public IAsyncEnumerable<ChatStreamEvent> ContinueColdOriginalAsync(ChatSessionService actualChat,
        Guid taskId, Guid expectedRunId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actualChat);
        actualChat.DemandOriginalCanonicalProcessCoordinator(this);
        var original = new CanonicalContinuationProcessCustody(this);
        return RegisterOriginalContinuationProcessProducer(original,
            cancellation => ContinueColdOriginalBodyAsync(original, actualChat, taskId, expectedRunId, cancellation), token);
    }

    private async IAsyncEnumerable<ChatStreamEvent> ContinueColdOriginalBodyAsync(
        CanonicalContinuationProcessCustody outer, ChatSessionService chat, Guid taskId, Guid expectedRunId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var journal = _coldRecoveryJournal ?? throw new InvalidOperationException("The actual cold recovery journal is unconfigured.");
        var contextOwner = _coldRecoveryContext ?? throw new InvalidOperationException("The fresh Conversation owner is unconfigured.");
        var authority = _admissionAuthority as ITaskRunColdOwnerAuthority
            ?? throw new InvalidOperationException("The actual fresh cold owner authority is unconfigured.");
        if (!outer.Invoke(() => authority.HasOriginalColdRecoveryComposition(journal, contextOwner)))
            throw new InvalidOperationException("The actual fresh cold authority is not paired to this SAME protected journal/context.");
        var operation = new AgentRuntimeOriginalCustody { OriginalProcessOwner = this };
        TaskRunColdContinuationBinding? binding = null;
        var prepare = operation.Start(async work =>
        {
            var sources = new TaskRunColdOriginalSourceScope(work);
            var entry = await work.AwaitAsync(() => journal.ReadOriginalAsync(taskId, expectedRunId, sources, token)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("No authentic closed input capsule is available for this SAME Task/run.");
            if (!journal.IsIssuedOriginalEntry(entry)) throw new UnauthorizedAccessException("The configured journal did not issue this entry.");
            var expected = await work.AwaitAsync(() => repository.GetAsync(taskId, token)).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("The original Task no longer exists.");
            TaskRunColdRecoveryBoundary.DemandRestorableBoundary(entry.Capsule, expected);
            lock (_processProducerGate)
                if (_originalInvocations.ContainsKey(taskId))
                    throw new InvalidOperationException("A live or uncertain original still owns this Task.");
            var claim = await work.AwaitAsync(() => journal.ClaimOriginalAsync(entry, expected, token)).ConfigureAwait(false);
            ITaskRunColdContextLease? context = null;
            ITaskRunColdOwnerAdmission? admission = null;
            IAsyncDisposable? pin = null;
            ITaskRunColdJournalAcknowledgment? acknowledgment = null;
            try
            {
                context = await work.AwaitAsync(() => contextOwner.AuthorizeOriginalAsync(claim, expected, token).AsTask()).ConfigureAwait(false);
                if (!contextOwner.IsIssuedOriginal(context, claim)) throw new UnauthorizedAccessException("No SAME fresh domain lease was issued.");
                admission = await work.AwaitAsync(() => authority.PrepareOriginalColdOwnerAsync(claim, context, expected, token).AsTask()).ConfigureAwait(false);
                if (!authority.IsIssuedOriginalColdOwner(admission, claim, context))
                    throw new UnauthorizedAccessException("No same source-issued cold owner admission exists.");
                await work.AwaitAsync(() => journal.ValidateOriginalClaimAsync(claim, expected, token)).ConfigureAwait(false);
                await work.AwaitAsync(() => contextOwner.ValidateOriginalAsync(context, expected, token).AsTask()).ConfigureAwait(false);
                await work.AwaitAsync(() => admission.RevalidateAsync(token).AsTask()).ConfigureAwait(false);
                pin = await work.AwaitAsync(() => admission.AcquireOriginalCommitPinAsync(token).AsTask()).ConfigureAwait(false);
                var next = expected with { OwnerBinding = admission.NextOwner,
                    PersistenceRevision = checked(expected.PersistenceRevision + 1), UpdatedAt = _time.GetUtcNow() };
                acknowledgment = await work.AwaitAsync(() => journal.CommitOriginalAsync(claim, next, token)).ConfigureAwait(false);
                if (!journal.IsIssuedOriginalAcknowledgment(acknowledgment, claim))
                    throw new UnauthorizedAccessException("No authentic same-claim binding CAS acknowledgment was issued.");
            }
            finally
            {
                // Every actually acquired child is joined independently. Unknown CAS/cleanup
                // leaves the durable claim sticky and never releases/replays it.
                if (pin is not null) try { await work.AwaitAsync(() => pin.DisposeAsync().AsTask(), owningCleanup: true).ConfigureAwait(false); } catch (Exception) { }
                if (admission is not null) try { await work.AwaitAsync(() => admission.DisposeAsync().AsTask(), owningCleanup: true).ConfigureAwait(false); } catch (Exception) { }
                if (context is not null) try { await work.AwaitAsync(() => context.DisposeAsync().AsTask(), owningCleanup: true).ConfigureAwait(false); } catch (Exception) { }
                try { await work.AwaitAsync(() => claim.DisposeAsync().AsTask(), owningCleanup: true).ConfigureAwait(false); } catch (Exception) { }
            }
            if (work.Causes.Count != 0) throw new AggregateException("Cold binding cleanup requires original inspection.", work.Causes);
            await work.AwaitAsync(() => authority.ActivateAcknowledgedOriginalColdOwnerAsync(admission!, acknowledgment!, token).AsTask()).ConfigureAwait(false);
            await work.AwaitAsync(() => journal.ValidateOriginalAcknowledgmentAsync(acknowledgment!, token)).ConfigureAwait(false);
            var invocation = work.Invoke(CreateOriginalInvocationCustody);
            var owned = new TaskRunColdContinuationBinding(this, chat, entry, claim, acknowledgment!, invocation);
            lock (_processProducerGate)
            {
                if (_processProducerAdmissionSealed || _originalInvocations.ContainsKey(taskId))
                    throw new InvalidOperationException("Cold business admission is sealed, live or already claimed.");
                _coldContinuationBindings.Add(invocation, owned);
            }
            binding = owned;
            if (entry.Capsule.Boundary == TaskRunColdBoundaryKind.SettledUnfinishedToolResponse)
                await PrepareOriginalColdToolContinuationAsync(owned, work, outer, token).ConfigureAwait(false);
            return owned;
        });
        var actualBinding = await outer.Await("cold.original-prepare", () => prepare).ConfigureAwait(false);
        outer.OriginalColdPreparation = actualBinding;
        var child = outer.Invoke(() => actualBinding.Entry.Capsule.Boundary == TaskRunColdBoundaryKind.SettledUnfinishedToolResponse
            ? chat.CreateOriginalColdToolContinuation(actualBinding, token)
            : chat.CreateOriginalColdContinuation(actualBinding, token));
        outer.OriginalChild = child;
        var iterator = outer.Invoke(() => child.GetAsyncEnumerator(token));
        try
        {
            while (await outer.Await("cold.original-child-move", () => iterator.MoveNextAsync().AsTask()).ConfigureAwait(false))
                yield return outer.Invoke(() => iterator.Current);
        }
        finally { await outer.DisposeChildAsync(iterator).ConfigureAwait(false); }
        var terminal = binding!.Invocation.OriginalTerminalObservation;
        if (terminal is null || !binding.Invocation.OwnedCleanupTerminal || binding.Invocation.Causes.Count != 0
            && !binding.Invocation.CanReturnPublishedPermissionRefusal(terminal)
            || binding.Invocation.OriginalProcessProducer is not { HasHealthyClosedOriginal: true })
            throw new InvalidOperationException("The actual restored business/cleanup has no healthy original terminal acknowledgment.");
        await outer.Await("cold.original-terminal-journal", () => journal.RecordOriginalTerminalAsync(binding.Acknowledgment,
            terminal, CancellationToken.None)).ConfigureAwait(false);
        binding.JournalTerminalAcknowledged = true;
    }

    internal Task<TaskExecutionSnapshot> BindOriginalColdContinuationAsync(TaskRunColdContinuationBinding binding,
        TaskRunInvocationCustody custody, ProviderExecutionContext observation, Guid contextId, CancellationToken token) =>
        StartOriginalProcessStage("bind-original-cold-body", token,
            cancellation => BindOriginalColdContinuationBodyAsync(binding, custody, observation, contextId, cancellation));

    private async Task<TaskExecutionSnapshot> BindOriginalColdContinuationBodyAsync(TaskRunColdContinuationBinding binding,
        TaskRunInvocationCustody custody, ProviderExecutionContext observation, Guid contextId, CancellationToken token)
    {
        var stage = RequireOriginalProcessStage();
        lock (_processProducerGate)
            if (_processProducerAdmissionSealed || !ReferenceEquals(binding.Owner, this)
                || !ReferenceEquals(binding.Invocation, custody) || binding.BodyBound
                || !_coldContinuationBindings.TryGetValue(custody, out var actual) || !ReferenceEquals(actual, binding))
                throw new UnauthorizedAccessException("No privately issued one-use cold body binding remains.");
        await stage.Await(() => _coldRecoveryJournal!.ValidateOriginalAcknowledgmentAsync(binding.Acknowledgment, token)).ConfigureAwait(false);
        var current = await stage.Await(() => repository.GetAsync(observation.TaskId, token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original Task disappeared before restored body admission.");
        if (current.ContextId != contextId || current.ExecutionId != observation.ExecutionId
            || current.PersistenceRevision != observation.PersistenceRevision
            || current.OwnerBinding != binding.Acknowledgment.AcknowledgedTask.OwnerBinding)
            throw new InvalidOperationException("The cold binding's actual same Task/run checkpoint changed.");
        await stage.Await(() => ValidateTaskCommandAsync(current, "task.cold-body", token)).ConfigureAwait(false);
        // The command may await current authority. Re-observe the actual row/input after
        // that await, then perform the final current actor check before private body binding.
        await stage.Await(() => _coldRecoveryJournal!.ValidateOriginalAcknowledgmentAsync(binding.Acknowledgment, token)).ConfigureAwait(false);
        var final = await stage.Await(() => repository.GetAsync(observation.TaskId, token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original Task disappeared during fresh cold authority validation.");
        if (System.Text.Json.JsonSerializer.Serialize(final) != System.Text.Json.JsonSerializer.Serialize(current))
            throw new InvalidOperationException("The actual cold body checkpoint changed during authority validation.");
        await stage.Await(() => ValidateTaskCommandAsync(final, "task.cold-body-final", token)).ConfigureAwait(false);
        ReserveOriginalInvocation(custody);
        BindOriginalInvocation(custody, final);
        lock (_processProducerGate) binding.BodyBound = true;
        return current;
    }

    internal async Task ValidateOriginalColdInputAsync(TaskRunColdContinuationBinding binding,
        TaskRunInvocationCustody custody, CancellationToken token)
    {
        lock (_processProducerGate)
            if (!ReferenceEquals(binding.Owner, this) || !ReferenceEquals(binding.Invocation, custody)
                || !binding.BodyBound || !_coldContinuationBindings.TryGetValue(custody, out var actual)
                || !ReferenceEquals(actual, binding))
                throw new UnauthorizedAccessException("The actual admitted cold input binding is required.");
        var currentRead = InvokeOriginalProcessInputSource(() => repository.GetAsync(binding.Acknowledgment.AcknowledgedTask.TaskId, token));
        custody.RetainAdditionalOriginal("cold.current-input-basis", currentRead);
        TaskExecutionSnapshot current;
        try { current = await currentRead.ConfigureAwait(false) ?? throw new InvalidOperationException("The actual restored Task disappeared."); }
        catch (Exception cause)
        {
            custody.Retain(cause, currentRead);
            if (currentRead.IsFaulted && currentRead.Exception is { } envelope) throw envelope;
            throw; // Genuine canceled raw source retains its separate cancellation status.
        }
        var task = InvokeOriginalProcessInputSource(() => _coldRecoveryJournal!.ValidateOriginalRestoredInputAsync(
            binding.Acknowledgment, current, new TaskRunColdOriginalSourceScope(custody), token));
        custody.RetainAdditionalOriginal("cold.input-and-current-owner", task);
        try { await task.ConfigureAwait(false); }
        catch (Exception cause)
        {
            custody.Retain(cause, task);
            if (task.IsFaulted && task.Exception is { } envelope) throw envelope;
            throw; // Genuine canceled raw source retains its separate cancellation status.
        }
    }

    internal bool HasOriginalColdNeverStartedBinding(TaskRunInvocationCustody custody, TaskExecutionSnapshot current)
    {
        TaskRunColdContinuationBinding binding;
        lock (_processProducerGate)
        {
            if (!_coldContinuationBindings.TryGetValue(custody, out binding!) || !binding.BodyBound
                || !ReferenceEquals(binding.Invocation, custody) || !ReferenceEquals(binding.Owner, this)
                || !ReferenceEquals(custody.OriginalColdContinuation, binding)
                || _coldRecoveryJournal is null) return false;
        }
        if (!_coldRecoveryJournal.IsIssuedOriginalAcknowledgment(binding.Acknowledgment, binding.Claim)) return false;
        var acknowledged = binding.Acknowledgment.AcknowledgedTask;
        return current.TaskId == acknowledged.TaskId && current.ContextId == acknowledged.ContextId
            && current.ExecutionId == acknowledged.ExecutionId && current.OwnerBinding == acknowledged.OwnerBinding
            && current.Attempts.Count == 0 && current.Plan.Count == 0 && current.ParentDelegation is null;
    }

    /// <summary>Durable business host, independent of a view. The command token only governs
    /// source acquisition; the registered stream and its actual Move/Dispose use process lifetime.</summary>
    public Task<TaskRunOriginalResumeObservationLease> StartObservedColdOriginalRunResumeAsync(
        ChatSessionService actualChat, Guid taskId, Guid expectedRunId, CancellationToken commandToken)
    {
        ArgumentNullException.ThrowIfNull(actualChat);
        actualChat.DemandOriginalCanonicalProcessCoordinator(this);
        DemandExternalOriginalProcessJoin();
        return StartOriginalProcessStage("start-observed-cold-original-run", commandToken, async token =>
        {
            var stage = RequireOriginalProcessStage();
            if (_coldRecoveryJournal is null || _coldRecoveryContext is null || _admissionAuthority is not ITaskRunColdOwnerAuthority authority
                || !stage.Invoke(() => authority.HasOriginalColdRecoveryComposition(_coldRecoveryJournal, _coldRecoveryContext)))
                throw new InvalidOperationException("Genuine paired cold journal/domain/owner producers are required before any claim.");
            var current = await RequireOriginalProcessSnapshotAsync(taskId, token).ConfigureAwait(false);
            RequireRun(current, expectedRunId);
            var source = stage.Invoke(() => ContinueColdOriginalAsync(actualChat, taskId, expectedRunId, CancellationToken.None))
                as CanonicalChatProcessProducer ?? throw new InvalidOperationException("No actual cold business source was registered.");
            var hosted = RegisterHostedOriginalRunResume(source, OriginalRunControlContext(current), stage);
            return hosted.Lease;
        });
    }
}
