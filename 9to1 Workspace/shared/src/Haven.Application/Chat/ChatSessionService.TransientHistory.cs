using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    private static IReadOnlyList<ChatMessage>? SnapshotTransientHistory(
        Conversation conversation,
        IReadOnlyList<ChatMessage>? transientHistory,
        ProviderExecutionContext? executionContext,
        TaskRunExecutionIntent taskExecutionIntent)
    {
        if (transientHistory is null) return null;
        if (!conversation.IsTemporary || taskExecutionIntent != TaskRunExecutionIntent.OrdinaryConversation
            || executionContext is not null
            || conversation.Kind is ConversationKind.Task or ConversationKind.AutomationRun or ConversationKind.Training or ConversationKind.Call)
            throw new ArgumentException("Transient history is available only to temporary ordinary conversations.", nameof(transientHistory));
        if (conversation.Id == Guid.Empty)
            throw new ArgumentException("Transient history requires an identified conversation.", nameof(conversation));

        var snapshot = transientHistory.ToArray();
        var messageIds = new HashSet<Guid>();
        foreach (var message in snapshot)
        {
            if (message is null || message.ConversationId != conversation.Id
                || message.Id == Guid.Empty || !messageIds.Add(message.Id))
                throw new ArgumentException("Transient history must contain unique messages from this conversation.", nameof(transientHistory));
            if (message.Role is not (MessageRole.User or MessageRole.Assistant))
                throw new ArgumentException("Transient history cannot introduce system instructions or tool authority.", nameof(transientHistory));
        }
        return Array.AsReadOnly(snapshot);
    }
}
