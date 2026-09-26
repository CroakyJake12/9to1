using Haven.Core;

namespace Haven.Application;

/// <summary>Provides the canonical, typed conversation membership operations for Spaces.</summary>
public sealed class SpaceConversationService(
    SpaceRegistry spaces,
    IConversationRepository conversations)
{
    public async Task<IReadOnlyList<Conversation>> GetConversationsAsync(
        Guid spaceId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        RequireId(spaceId, nameof(spaceId));
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit), "The limit must be from 1 to 500.");
        await RequireActiveSpaceAsync(spaceId, cancellationToken).ConfigureAwait(false);
        var result = await conversations.GetBySpaceAsync(spaceId, limit, cancellationToken).ConfigureAwait(false);
        return result.Where(item => item.SpaceId == spaceId).Take(limit).ToArray();
    }

    public async Task<Conversation> AssignAsync(
        Guid conversationId,
        Guid spaceId,
        CancellationToken cancellationToken = default)
    {
        RequireId(conversationId, nameof(conversationId));
        RequireId(spaceId, nameof(spaceId));
        var space = await RequireActiveSpaceAsync(spaceId, cancellationToken).ConfigureAwait(false);
        var conversation = await conversations.GetAsync(conversationId, cancellationToken).ConfigureAwait(false)
            ?? throw new SpaceConversationException(SpaceConversationErrorCode.ConversationNotFound,
                $"Conversation '{conversationId}' was not found.");
        if (conversation.Kind is ConversationKind.Call or ConversationKind.AutomationRun or ConversationKind.Training)
            throw new SpaceConversationException(SpaceConversationErrorCode.ProtectedConversation,
                $"System-managed {conversation.Kind} conversations cannot be assigned to a Space.");
        if (conversation.IsArchived)
            throw new SpaceConversationException(SpaceConversationErrorCode.ArchivedConversation,
                "Restore the conversation before assigning it to a Space.");
        if (conversation.SpaceId == spaceId) return conversation;

        var updated = conversation with { SpaceId = space.Id, UpdatedAt = DateTimeOffset.UtcNow };
        await conversations.UpsertConversationAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<Conversation> RemoveAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        RequireId(conversationId, nameof(conversationId));
        var conversation = await conversations.GetAsync(conversationId, cancellationToken).ConfigureAwait(false)
            ?? throw new SpaceConversationException(SpaceConversationErrorCode.ConversationNotFound,
                $"Conversation '{conversationId}' was not found.");
        if (conversation.Kind is ConversationKind.Call or ConversationKind.AutomationRun or ConversationKind.Training)
            throw new SpaceConversationException(SpaceConversationErrorCode.ProtectedConversation,
                $"System-managed {conversation.Kind} conversations cannot be moved out of a Space.");
        if (conversation.SpaceId is null) return conversation;

        var updated = conversation with { SpaceId = null, UpdatedAt = DateTimeOffset.UtcNow };
        await conversations.UpsertConversationAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async Task<SpaceDefinition> RequireActiveSpaceAsync(Guid spaceId, CancellationToken cancellationToken)
    {
        var space = await spaces.GetAsync(spaceId, cancellationToken).ConfigureAwait(false);
        if (space is null || space.IsArchived)
            throw new SpaceConversationException(SpaceConversationErrorCode.SpaceUnavailable,
                $"Active Space '{spaceId}' was not found.");
        return space;
    }

    private static void RequireId(Guid id, string parameter)
    {
        if (id == Guid.Empty) throw new ArgumentException("A stable non-empty ID is required.", parameter);
    }
}

public enum SpaceConversationErrorCode
{
    ConversationNotFound,
    SpaceUnavailable,
    ArchivedConversation,
    ProtectedConversation
}

public sealed class SpaceConversationException(SpaceConversationErrorCode code, string message)
    : InvalidOperationException(message)
{
    public SpaceConversationErrorCode Code { get; } = code;
}
