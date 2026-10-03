using Haven.Core;

namespace Haven.Application;

/// <summary>Owning membership transaction over exact displayed rows and Space revisions.
/// Implementations return the immutable row acknowledged by CAS, never a post-commit reread.</summary>
public interface ISpaceConversationWriter
{
    Task<Conversation> AssignAsync(Conversation expected, SpaceDefinition? source, SpaceDefinition? destination, CancellationToken cancellationToken);
    Task<Conversation> CreateAsync(Conversation proposed, SpaceDefinition destination, CancellationToken cancellationToken);
}

public sealed class SpaceConversationWriteException(ConversationSpaceCommitStatus status)
    : InvalidOperationException(status == ConversationSpaceCommitStatus.RevisionConflict
        ? "This chat changed. Refresh before changing its Space." : "Current ownership does not permit this Space assignment.")
{
    public ConversationSpaceCommitStatus Status { get; } = status;
}
