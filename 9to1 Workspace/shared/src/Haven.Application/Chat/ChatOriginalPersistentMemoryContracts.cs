using Haven.Core;

namespace Haven.Application;

/// <summary>Opaque input issued by the SAME configured scoped memory source. Persisted
/// configuration and serialized identifiers cannot create this live resource binding.</summary>
public interface IChatOriginalPersistentMemoryInput { }

/// <summary>Existing scoped memory source, injected into SAME maintained Chat owner.
/// Returned records feed maintained MemoryInjection; this port adds no memory engine.
/// Each productive original is acquired synchronously inside the supplied finite scope
/// and its raw Task is retained before any post-callback freshness check can reject it.</summary>
public interface IChatOriginalPersistentMemorySource
{
    bool IsIssuedOriginalInput(IChatOriginalPersistentMemoryInput actualInput);
    Task<IReadOnlyList<KnowledgeRecord>> ReadOriginalWithinSourceAsync(
        IChatOriginalPersistentMemoryInput actualInput, Conversation actualConversation,
        ProviderExecutionContext? actualCanonicalContext,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken token);
    Task ValidateOriginalWithinSourceAsync(
        IChatOriginalPersistentMemoryInput actualInput, Conversation actualConversation,
        ProviderExecutionContext? actualCanonicalContext,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken token);
}
