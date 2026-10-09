using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantCanonicalBridge : IAssistantOriginalColdTaskRecoveryOwner
{
    private readonly ConditionalWeakTable<AssistantColdTaskRecoveryPreview, OriginalColdTaskPreview> _coldTaskPreviews = new();
    private sealed class OriginalColdTaskPreview(AssistantConversationBinding binding,
        TaskExecutionSnapshot task, string? capsuleHash)
    {
        internal readonly AssistantConversationBinding Binding = binding;
        internal readonly TaskExecutionSnapshot Task = task;
        internal readonly string? CapsuleHash = capsuleHash;
        internal int Requested;
    }

    private async Task<AuthenticatedResourceActor> ReadOriginalTaskActorAsync(CancellationToken token)
    {
        var observed = await _originals.Source(() => _tasks.ObserveOriginalTaskActorWithinSourceAsync(
            MembershipScope, _originals.Retain, token)).ConfigureAwait(false);
        if (!_originals.Invoke(() => _tasks.IsIssuedOriginalTaskActorObservation(observed)))
            throw new InvalidOperationException("The actual Task owner did not issue its current actor observation.");
        if (!observed.IsConfigured || observed.Actor is not { } actor)
            throw new AssistantCommandRefusedException("The current Task identity source is unavailable. Your saved conversation has not been changed.");
        return actor;
    }

    // Call ONLY after actual current Home/Den membership READ and an observation
    // from the SAME configured Task authority. Home identity is independent.
    private static bool RequiresOriginalTaskOwnerRenewal(AssistantConversationBinding current,
        TaskExecutionSnapshot task, AuthenticatedResourceActor actor)
    {
        if (task.ContextId != current.Conversation.Id || task.OwnerBinding is not { } owner ||
            owner.TaskId != task.TaskId || owner.ContextId != task.ContextId || owner.ExecutionId != task.ExecutionId ||
            owner.ActorId != actor.ActorId || owner.ProfileId != actor.ProfileId ||
            owner.AccountId != actor.AccountId || owner.OrganisationId != actor.OrganisationId)
            throw new AssistantCommandRefusedException("This saved Task belongs to a different authenticated owner.");
        return owner.AuthenticationRevision != actor.AuthenticationRevision;
    }

    private async Task<TaskExecutionSnapshot> ReadOriginalRecoveryTaskAsync(AssistantConversationBinding binding,
        CancellationToken token)
    {
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        if (current.Conversation.Mode != HavenMode.Tasks || current.Conversation.Kind != ConversationKind.Task)
            throw new AssistantCommandRefusedException("Open the saved Task conversation before reviewing recovery.");
        var task = await _originals.Source(() => _tasks.GetByContextAsync(current.Conversation.Id, token)).ConfigureAwait(false)
            ?? throw new AssistantCommandRefusedException("This conversation has no saved Task.");
        var actor = await ReadOriginalTaskActorAsync(token).ConfigureAwait(false);
        current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        if (!RequiresOriginalTaskOwnerRenewal(current, task, actor))
            throw new AssistantCommandRefusedException("This Task already belongs to the current session. Refresh its available controls.");
        return task;
    }

    public Task<AssistantColdTaskRecoveryPreview> ReadOriginalColdTaskRecoveryAsync(
        AssistantConversationBinding binding, CancellationToken token = default) => _originals.Admit(async () =>
    {
        var task = await ReadOriginalRecoveryTaskAsync(binding, token).ConfigureAwait(false);
        TaskRunColdCapsule? capsule = null;
        var state = AssistantColdTaskRecoveryState.Unavailable;
        var reason = "Recovery is not configured for this host. Your saved Task and conversation remain available.";
        if (_tasks.HasOriginalColdResumeComposition(_chat))
        {
            capsule = await _originals.Source(() => _tasks.ObserveOriginalColdInputAsync(task.TaskId, task.ExecutionId, token)).ConfigureAwait(false);
            state = capsule is null ? AssistantColdTaskRecoveryState.NoClosedInput : AssistantColdTaskRecoveryState.ReviewAvailable;
            reason = capsule is null ? "No recoverable input was saved for this Task. Its history is still available."
                : "A saved input is available. Resume will recheck your current access, model and privacy settings before continuing this Task.";
            if (capsule is not null) DemandOriginalRecoveryCapsule(task, capsule);
        }
        var after = await ReadOriginalRecoveryTaskAsync(binding, token).ConfigureAwait(false);
        DemandSameOriginalRecoveryTask(task, after);
        var preview = new AssistantColdTaskRecoveryPreview(task.TaskId, task.ExecutionId, task.ContextId,
            task.PersistenceRevision, state, reason);
        _coldTaskPreviews.Add(preview, new(binding, after, capsule is null ? null : OriginalCapsuleHash(capsule)));
        return preview;
    });

    public Task<TaskRunOriginalResumeObservationLease> StartObservedOriginalColdTaskResumeAsync(
        AssistantConversationBinding binding, AssistantColdTaskRecoveryPreview samePreview,
        CancellationToken token = default) => _originals.Admit(async () =>
    {
        _originals.DemandObservationCapacity();
        if (samePreview is null || !_coldTaskPreviews.TryGetValue(samePreview, out var original) ||
            !ReferenceEquals(original.Binding, binding) || !samePreview.CanRequestResume || original.CapsuleHash is null ||
            Volatile.Read(ref original.Requested) != 0)
            throw new AssistantCommandRefusedException("Review this Task's current recovery input before resuming.");
        var task = await ReadOriginalRecoveryTaskAsync(binding, token).ConfigureAwait(false);
        DemandSameOriginalRecoveryTask(original.Task, task);
        if (!_tasks.HasOriginalColdResumeComposition(_chat))
            throw new AssistantCommandRefusedException("Recovery is no longer available from this host.");
        var capsule = await _originals.Source(() => _tasks.ObserveOriginalColdInputAsync(task.TaskId, task.ExecutionId, token)).ConfigureAwait(false)
            ?? throw new AssistantCommandRefusedException("The saved recovery input is no longer available. Review this Task again.");
        DemandOriginalRecoveryCapsule(task, capsule);
        if (OriginalCapsuleHash(capsule) != original.CapsuleHash)
            throw new AssistantCommandRefusedException("The saved recovery input changed. Review this Task again.");
        DemandSameOriginalRecoveryTask(task, await ReadOriginalRecoveryTaskAsync(binding, token).ConfigureAwait(false));
        token.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref original.Requested, 1, 0) != 0)
            throw new AssistantCommandRefusedException("This recovery request has already been submitted.");
        // The SAME protected canonical protocol alone renews the owner, verifies
        // model/privacy/resource authority, and admits the process-owned producer.
        var lease = await _originals.Source(() => _tasks.StartObservedColdOriginalRunResumeAsync(
            _chat, task.TaskId, task.ExecutionId, token)).ConfigureAwait(false);
        if (!_tasks.IsIssuedOriginalRunResumeObservation(lease))
            throw new InvalidOperationException("The actual Task owner did not issue this recovery observation.");
        _originals.Observe(lease.DetachAndDrainAsync);
        return lease;
    });

    private static void DemandSameOriginalRecoveryTask(TaskExecutionSnapshot expected, TaskExecutionSnapshot actual)
    {
        if (expected.TaskId != actual.TaskId || expected.ContextId != actual.ContextId ||
            expected.ExecutionId != actual.ExecutionId || expected.PersistenceRevision != actual.PersistenceRevision ||
            expected.OwnerBinding != actual.OwnerBinding)
            throw new AssistantCommandRefusedException("The saved Task changed. Review its current recovery state.");
    }
    private static void DemandOriginalRecoveryCapsule(TaskExecutionSnapshot task, TaskRunColdCapsule capsule)
    {
        if (capsule.CapsuleId == Guid.Empty || capsule.AcknowledgedTask.TaskId != task.TaskId ||
            capsule.AcknowledgedTask.ExecutionId != task.ExecutionId || capsule.AcknowledgedTask.ContextId != task.ContextId ||
            capsule.AcceptedConversation.Id != task.ContextId)
            throw new InvalidOperationException("The protected recovery source returned a different Task or conversation.");
    }
    private static string OriginalCapsuleHash(TaskRunColdCapsule actual) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(actual)));
}
