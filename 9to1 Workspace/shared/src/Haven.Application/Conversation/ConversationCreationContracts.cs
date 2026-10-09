using Haven.Core;

namespace Haven.Application;

/// <summary>Atomic create-only operation of the actual canonical conversation store.
/// True acknowledges this original insertion; false observes an existing ID and never adopts or overwrites it.</summary>
public interface IConversationCreateOnlyRepository
{
    Task<bool> TryCreateConversationAsync(Conversation original, CancellationToken cancellationToken);
}
