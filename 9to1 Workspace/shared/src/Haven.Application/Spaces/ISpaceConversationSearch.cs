using Haven.Core;

namespace Haven.Application;

/// <summary>Optional read capability of the SAME canonical conversation repository.
/// Searches saved titles inside one Space before applying the bounded result limit.
/// It supplies no membership, mutation, model or provider authority.</summary>
public interface ISpaceConversationSearch
{
    Task<IReadOnlyList<Conversation>> FindSavedTitlesBySpaceAsync(
        Guid spaceId, string query, int limit, CancellationToken cancellationToken);
}
