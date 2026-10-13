using Haven.Core;

namespace Haven.Application;

/// <summary>
/// The canonical local store's atomic Temporary → Normal conversion. This persists
/// the original conversation and messages together without invoking models, tools,
/// memory reinforcement, usage recording or remote replication. It grants no authority;
/// the caller must retain its current profile and explicit user save authorization.
/// </summary>
public interface ITemporaryConversationSaveRepository
{
    /// <summary>
    /// Saves the same IDs or acknowledges an exact prior save. Conflicting existing
    /// identities are refused, never overwritten. Failure leaves no partial history.
    /// The returned durable value is published by the caller only after acknowledgement.
    /// </summary>
    Task<Conversation> SaveTemporaryLocallyAsync(Conversation temporary,
        IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);

    Task<Conversation> SaveTemporaryLocallyAsync(Conversation temporary,
        IReadOnlyList<ChatMessage> messages, IReadOnlyList<ConversationContextEntry> contextEntries,
        CancellationToken cancellationToken);
}
