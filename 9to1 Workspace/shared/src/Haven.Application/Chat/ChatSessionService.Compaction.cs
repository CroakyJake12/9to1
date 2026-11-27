using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    private async Task<ConversationPromptContext> ReadOriginalPromptContextAsync(Conversation conversation,
        IConversationBranchInvocation? branch, GenerationOptions? options, bool canonicalIntent, CancellationToken token)
    {
        if (conversation.IsTemporary && conversation.CompactedAt is not null && options?.OriginalTemporaryContext is null)
            throw new InvalidOperationException("This compacted Temporary Chat needs its original live history and retained summaries. No replacement context was reconstructed.");
        ConversationPromptContext context;
        if (options?.OriginalTemporaryContext is { } temporary)
        {
            if (canonicalIntent || branch is not null)
                throw new InvalidOperationException("A live Temporary context snapshot requires its original ordinary chat source.");
            context = temporary.ForConversation(conversation);
        }
        else if (branch is IConversationBranchPromptContextSource bound)
            context = await bound.GetPromptContextAsync(token).ConfigureAwait(false);
        else if (branch is not null)
        {
            // Preserve an older source's bound read; never substitute the currently selected
            // branch. Production sources implement the complete coverage projection above.
            var history = await branch.GetContextMessagesAsync(token).ConfigureAwait(false);
            context = RawPromptContext(conversation.Id, branch.BranchId, history);
        }
        else if (conversations is IConversationPromptContextRepository source)
            context = await source.GetPromptContextAsync(conversation.Id, token).ConfigureAwait(false);
        else
        {
            // Legacy compaction flags have no source coverage. Preserve all available raw
            // rows instead of treating those flags as evidence that a replacement exists.
            var history = await conversations.GetMessagesAsync(conversation.Id, token).ConfigureAwait(false);
            context = RawPromptContext(conversation.Id, null, history);
        }
        if (context.ConversationId != conversation.Id || branch is not null && context.BranchId != branch.BranchId)
            throw new InvalidOperationException("The prompt context does not match the original conversation branch.");
        return context;
    }

    private static ConversationPromptContext RawPromptContext(Guid conversationId, Guid? branchId, IReadOnlyList<ChatMessage> history) =>
        new(conversationId, branchId, history, history.Select(message => new ConversationSourceVersion(message.Id, null)).ToArray(), []);

    private static CompactionPromptSelection SelectOriginalPromptContext(ConversationPromptContext context,
        int characterBudget, bool sourceOnly, bool canonicalIntent)
    {
        if (sourceOnly) return new([], string.Empty, characterBudget);
        // Canonical Task inventory currently carries exact raw history, not summary lineage.
        // Keep that authority boundary and use real raw rows until its owner supports summaries.
        var selected = new List<ConversationCompaction>();
        var used = 0;
        if (!canonicalIntent)
            foreach (var compaction in context.Compactions.OrderByDescending(item => item.Summary.CreatedAt))
            {
                var cost = SummarySection(compaction.Summary).Length + 2;
                // Keep at least half the existing recent-message budget. An oversized
                // replacement cannot silently hide its sources: they fall back to raw below.
                if (cost > characterBudget / 2 - used) continue;
                selected.Add(compaction); used += cost;
            }
        var covered = selected.SelectMany(compaction => compaction.OriginalMessages).Select(message => message.Id).ToHashSet();
        var messages = context.OriginalMessages.Where(message => !covered.Contains(message.Id))
            .Select(message => message with { IsCompacted = false }).ToArray();
        var summary = string.Join("\n\n", selected.OrderBy(compaction => compaction.Summary.CreatedAt)
            .Select(compaction => SummarySection(compaction.Summary)));
        return new(messages, summary, characterBudget - used);
    }

    private static string SummarySection(ConversationContextEntry summary) =>
        "Retained conversation summary (source history remains available):\n" + summary.Content;

    private sealed record CompactionPromptSelection(IReadOnlyList<ChatMessage> Messages, string Summary, int MessageBudget);
}
