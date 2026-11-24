using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class UsageTrackingConversationRepository : IAssistantConversationArchiveStore
{
    public Task<bool> TrySetAssistantSecondaryArchiveAsync(Conversation original, bool archived, CancellationToken token) =>
        inner.TrySetAssistantSecondaryArchiveAsync(original, archived, token);
}
