using System.Runtime.CompilerServices;
using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    private readonly ConditionalWeakTable<TaskRunColdContinuationBinding,
        ChatOriginalPersistentMemoryColdRequest> _originalColdMemoryRequests = new();

    public bool IsIssuedOriginalColdMemoryRequest(ChatOriginalPersistentMemoryColdRequest request,
        IChatOriginalPersistentMemoryColdSource actualSource) =>
        request is not null && ReferenceEquals(request.Issuer, this) &&
        ReferenceEquals(OriginalPersistentMemorySource, actualSource) &&
        _originalColdMemoryRequests.TryGetValue(request.Binding, out var issued) && ReferenceEquals(issued, request) &&
        request.Binding.Owner.IsIssuedOriginalColdMemoryPreparation(request.Binding, this);

    internal async Task PrepareOriginalColdMemoryWithinSourceAsync(TaskRunColdContinuationBinding binding,
        TaskRunColdOriginalSourceScope actualColdSources, Action<Task> retainOriginalTask, CancellationToken token)
    {
        var input = binding.Entry.Capsule.OriginalInput;
        var options = input.GenerationOptions;
        if (options?.RequestedContextConstraints?.AllowPersistentMemoryRead == false || !UsesOriginalPersistentMemory(options))
        {
            binding.PreparedOriginalInput = input with
            {
                GenerationOptions = options is null ? null : options with { OriginalPersistentMemoryInput = null }
            };
            return;
        }
        var source = new OriginalMemorySourceScope(actualColdSources, retainOriginalTask);
        if (OriginalPersistentMemorySource is not IChatOriginalPersistentMemoryColdSource actualOwner ||
            options?.RequestedPersistentMemoryLineage is not { Schema: 1 } expected)
            throw new InvalidOperationException("Cold memory work requires the SAME genuine fresh source and its original durable lineage.");
        var current = await source.Read(() => binding.Owner.ReadOriginalColdMemoryPreparationWithinSourceAsync(
            binding, actualColdSources, token)).ConfigureAwait(false);
        var request = source.Observe(() => new ChatOriginalPersistentMemoryColdRequest(this, binding, current));
        source.Run(() => _originalColdMemoryRequests.Add(binding, request));
        var outcome = await source.Read(() => actualOwner.PrepareOriginalColdWithinSourceAsync(request,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        var fresh = source.Observe(() => outcome.Input);
        if (fresh is null) throw new InvalidOperationException(source.Observe(() => outcome.Reason));
        if (!source.Observe(() => IsIssuedOriginalColdMemoryRequest(request, actualOwner) &&
            actualOwner.IsIssuedOriginalInput(fresh) && actualOwner.ObserveOriginalLineage(fresh) == expected))
            throw new UnauthorizedAccessException("The fresh memory source does not preserve this SAME original definition/membership/store lineage.");
        var context = new ProviderExecutionContext(current.TaskId, current.ContextId, current.ExecutionId,
            current.Attempts.LastOrDefault()?.Id, current.PersistenceRevision);
        await source.Read(() => actualOwner.ValidateOriginalWithinSourceAsync(fresh, input.Conversation,
            context, source.Run, source.Retain, token)).ConfigureAwait(false);
        var final = await source.Read(() => request.ValidateOriginalWithinSourceAsync(
            source.Run, source.Retain, token)).ConfigureAwait(false);
        if (System.Text.Json.JsonSerializer.Serialize(current) != System.Text.Json.JsonSerializer.Serialize(final))
            throw new InvalidOperationException("The actual canonical cold Task changed during fresh memory preparation.");
        source.Run(() => binding.PreparedOriginalInput = input with
        {
            GenerationOptions = options! with { OriginalPersistentMemoryInput = fresh }
        });
    }

    internal async Task<TaskExecutionSnapshot> ValidateOriginalColdMemoryRequestWithinSourceAsync(
        ChatOriginalPersistentMemoryColdRequest request, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
    {
        if (OriginalPersistentMemorySource is not IChatOriginalPersistentMemoryColdSource actualSource ||
            !IsIssuedOriginalColdMemoryRequest(request, actualSource))
            throw new UnauthorizedAccessException("Only this actual Chat/coordinator's privately issued cold memory request is accepted.");
        var parent = request.Binding.Owner.CreateOriginalColdMemorySourceScope(request.Binding);
        var scope = parent.WithinOriginalCaller(originalSynchronousScope, retainOriginalTask);
        return await request.Binding.Owner.ReadOriginalColdMemoryPreparationWithinSourceAsync(
            request.Binding, scope, token).ConfigureAwait(false);
    }

    private static GenerationOptions? RebindOriginalColdMemoryOptions(TaskRunColdContinuationBinding binding,
        GenerationOptions? originalRequestOptions)
    {
        var original = binding.Entry.Capsule.OriginalInput.GenerationOptions;
        var prepared = binding.CurrentOriginalInput.GenerationOptions;
        if (!UsesOriginalPersistentMemory(original)) return originalRequestOptions;
        if (originalRequestOptions is null || original is null || prepared is null ||
            originalRequestOptions.RequestedContextConstraints != original.RequestedContextConstraints ||
            originalRequestOptions.RequestedPersistentMemoryLineage != original.RequestedPersistentMemoryLineage)
            throw new InvalidOperationException("The authenticated unfinished request changed its original memory restriction or lineage.");
        // Preserve every historical request/model/tool value. Only a fresh actual live
        // input is attached; the JsonIgnored marker never rewrites journal history.
        return originalRequestOptions with { OriginalPersistentMemoryInput = prepared.OriginalPersistentMemoryInput };
    }
}

public sealed partial class TaskExecutionCoordinator
{
    internal bool IsIssuedOriginalColdMemoryPreparation(TaskRunColdContinuationBinding binding,
        ChatSessionService actualChat)
    {
        ITaskRunColdRecoveryJournal? journal;
        lock (_processProducerGate)
        {
            if (!ReferenceEquals(binding.Owner, this) || !ReferenceEquals(binding.Chat, actualChat) ||
                !_coldContinuationBindings.TryGetValue(binding.Invocation, out var issued) || !ReferenceEquals(issued, binding))
                return false;
            journal = _coldRecoveryJournal;
        }
        // The actual configured issuer callback runs outside the coordinator gate.
        return journal is not null && journal.IsIssuedOriginalAcknowledgment(binding.Acknowledgment, binding.Claim);
    }

    internal TaskRunColdOriginalSourceScope CreateOriginalColdMemorySourceScope(TaskRunColdContinuationBinding binding)
    {
        if (!IsIssuedOriginalColdMemoryPreparation(binding, binding.Chat))
            throw new UnauthorizedAccessException("No SAME privately issued canonical cold memory binding exists.");
        return binding.OriginalMemorySourceScope ??
            throw new InvalidOperationException("The actual cold preparation source custody is unavailable.");
    }

    internal async Task<TaskExecutionSnapshot> ReadOriginalColdMemoryPreparationWithinSourceAsync(
        TaskRunColdContinuationBinding binding, TaskRunColdOriginalSourceScope actualSources, CancellationToken token)
    {
        if (!IsIssuedOriginalColdMemoryPreparation(binding, binding.Chat))
            throw new UnauthorizedAccessException("No SAME protected journal/context acknowledgment owns this cold preparation.");
        var current = await TaskRunColdSourceIo.Read(actualSources,
            () => repository.GetAsync(binding.Acknowledgment.AcknowledgedTask.TaskId, token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The actual cold Task disappeared before memory preparation.");
        var acknowledged = binding.Acknowledgment.AcknowledgedTask;
        if (current.TaskId != acknowledged.TaskId || current.ContextId != acknowledged.ContextId ||
            current.ExecutionId != acknowledged.ExecutionId || current.OwnerBinding != acknowledged.OwnerBinding)
            throw new UnauthorizedAccessException("The actual cold Task/run/current owner changed before memory preparation.");
        await TaskRunColdSourceIo.Read(actualSources, () => _coldRecoveryJournal!.ValidateOriginalRestoredInputAsync(
            binding.Acknowledgment, current, actualSources, token)).ConfigureAwait(false);
        return current;
    }
}
