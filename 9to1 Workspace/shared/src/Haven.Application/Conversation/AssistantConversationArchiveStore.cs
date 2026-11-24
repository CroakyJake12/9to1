using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Existing conversation archive metadata only. Callers must resolve the authenticated owner
/// and authorize the supplied original before requesting a compare-and-set.
/// </summary>
public interface IAssistantConversationArchiveStore
{
    Task<bool> TrySetAssistantSecondaryArchiveAsync(Conversation original, bool archived, CancellationToken token);
}
