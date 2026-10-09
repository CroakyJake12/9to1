using Haven.Core;

namespace Haven.Application;

/// <summary>Durable observations issued by a scoped source alongside its live input.
/// These values preserve the originally selected lineage; they never grant a read,
/// manufacture a definition/membership, or recover a serialized permission.</summary>
public sealed record ChatOriginalPersistentMemoryLineage(int Schema, string DenId,
    string NamespaceId, string DefinitionId, long DefinitionRevision,
    string MembershipId, long MembershipRevision, Guid ConversationId,
    Guid MemoryStoreId, string ConfigurationSha256);

/// <summary>Optional fresh reconstruction implemented by the SAME trusted memory
/// source. Preparation uses the current canonical cold acknowledgment and genuine
/// Home/Den/store permissions before any restored provider selection or dispatch.</summary>
public interface IChatOriginalPersistentMemoryColdSource : IChatOriginalPersistentMemorySource
{
    ChatOriginalPersistentMemoryLineage ObserveOriginalLineage(IChatOriginalPersistentMemoryInput input);
    Task<ChatOriginalPersistentMemoryColdPreparation> PrepareOriginalColdWithinSourceAsync(
        ChatOriginalPersistentMemoryColdRequest sameRequest,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken token);
}

/// <summary>A source outcome, never an authorization token. SAME source issuance and
/// durable lineage equality still protect every accepted live Input.</summary>
public sealed record ChatOriginalPersistentMemoryColdPreparation(
    IChatOriginalPersistentMemoryInput? Input, string Reason);

/// <summary>Only canonical Chat can issue this request over its actual one-use cold
/// binding. Public IDs and serialized capsule fields cannot construct one. The module
/// validates through the SAME current journal/coordinator before using observations.</summary>
public sealed class ChatOriginalPersistentMemoryColdRequest
{
    internal ChatOriginalPersistentMemoryColdRequest(ChatSessionService issuer,
        TaskRunColdContinuationBinding binding, TaskExecutionSnapshot current)
    { Issuer = issuer; Binding = binding; ActualCurrentTask = current; }
    internal ChatSessionService Issuer { get; }
    internal TaskRunColdContinuationBinding Binding { get; }
    public ChatSessionService OriginalChatOwner => Issuer;
    public TaskExecutionCoordinator OriginalTaskOwner => Binding.Owner;
    public TaskRunColdCapsule OriginalCapsule => Binding.Entry.Capsule;
    public ITaskRunColdJournalAcknowledgment OriginalAcknowledgment => Binding.Acknowledgment;
    public TaskExecutionSnapshot ActualCurrentTask { get; }
    public Conversation ActualConversation => Binding.Entry.Capsule.AcceptedConversation;
    public ChatOriginalPersistentMemoryLineage? OriginalLineage =>
        Binding.Entry.Capsule.OriginalInput.GenerationOptions?.RequestedPersistentMemoryLineage;
    public Task<TaskExecutionSnapshot> ValidateOriginalWithinSourceAsync(Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token) =>
        Issuer.ValidateOriginalColdMemoryRequestWithinSourceAsync(this,
            originalSynchronousScope, retainOriginalTask, token);
}
