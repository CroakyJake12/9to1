using Haven.Application;

namespace Haven.Infrastructure;

public sealed partial class UsageTrackingConversationRepository : IConversationCompactionRepository, IConversationPromptContextRepository
{
    public bool SupportsCompactionSummaries => inner.SupportsCompactionSummaries;

    public Task<ConversationPromptContext> GetPromptContextAsync(Guid conversationId, CancellationToken cancellationToken) =>
        inner.GetPromptContextAsync(conversationId, cancellationToken);

    // Retaining historical context must not replay provider calls or message usage accounting.
    public Task<ConversationCompactionReceipt> CommitCompactionAsync(Guid conversationId,
        ConversationCompaction compaction, CancellationToken cancellationToken) =>
        inner.CommitCompactionAsync(conversationId, compaction, cancellationToken);

    private sealed partial class TrackedBranchInvocation : IConversationBranchPromptContextSource
    {
        public Task<ConversationPromptContext> GetPromptContextAsync(CancellationToken cancellationToken) =>
            WithinLifetimeAsync(() => (original as IConversationBranchPromptContextSource
                ?? throw new InvalidOperationException("The original tracked branch cannot project verified prompt context."))
                .GetPromptContextAsync(cancellationToken), cancellationToken);
    }

}
