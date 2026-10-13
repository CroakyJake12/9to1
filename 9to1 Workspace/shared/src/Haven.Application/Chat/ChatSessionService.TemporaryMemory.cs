using System.Runtime.CompilerServices;
using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    private readonly ConditionalWeakTable<TemporaryChatMemoryReadChoice, IssuedTemporaryMemoryReadChoice>
        _temporaryMemoryReadChoices = new();

    /// <summary>Records the actual UI's choice for this exact temporary send lifetime.
    /// The existing Memory source still owns every resource/actor permission check.</summary>
    public TemporaryChatMemoryReadChoice RecordTemporaryMemoryReadChoice(
        Conversation conversation, string prompt, CancellationToken originalSendCancellation, bool allowRead)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(prompt);
        if (!conversation.IsTemporary || conversation.Id == Guid.Empty ||
            conversation.Kind is ConversationKind.Task or ConversationKind.AutomationRun or ConversationKind.Training or ConversationKind.Call)
            throw new ArgumentException("A temporary ordinary conversation is required.", nameof(conversation));
        if (!originalSendCancellation.CanBeCanceled)
            throw new ArgumentException("The actual cancellable send lifetime is required.", nameof(originalSendCancellation));
        originalSendCancellation.ThrowIfCancellationRequested();
        var choice = new TemporaryChatMemoryReadChoice(allowRead);
        _temporaryMemoryReadChoices.Add(choice, new(conversation, prompt, originalSendCancellation));
        return choice;
    }

    private GenerationOptions? ApplyTemporaryMemoryReadChoice(Conversation conversation, string prompt,
        GenerationOptions? options, CancellationToken originalSendCancellation, TaskRunExecutionIntent executionIntent)
    {
        var choice = options?.TemporaryMemoryReadChoice;
        if (executionIntent != TaskRunExecutionIntent.OrdinaryConversation)
        {
            if (choice is not null)
                throw new InvalidOperationException("A temporary ordinary Memory choice cannot authorize a canonical Task request.");
            // Internal canonical Agent children use transient storage too. Their existing
            // governed Memory policy remains owned by the actual canonical source.
            return options;
        }
        if (!conversation.IsTemporary)
        {
            if (choice is not null)
                throw new InvalidOperationException("A temporary Memory choice cannot be used by a saved conversation.");
            return options;
        }

        var allowed = false;
        if (choice is not null)
        {
            originalSendCancellation.ThrowIfCancellationRequested();
            if (!_temporaryMemoryReadChoices.TryGetValue(choice, out var issued) ||
                !ReferenceEquals(issued.Conversation, conversation) || issued.Prompt != prompt ||
                issued.Token != originalSendCancellation ||
                Interlocked.CompareExchange(ref issued.Consumed, 1, 0) != 0)
                throw new InvalidOperationException("A fresh Memory choice from this exact temporary send is required.");
            allowed = choice.AllowRead;
        }

        var actual = options ?? new GenerationOptions();
        return actual with
        {
            // Do not expose or retain the live choice in provider options or continuations.
            TemporaryMemoryReadChoice = null,
            RequestedContextConstraints = (actual.RequestedContextConstraints ?? new()) with
            { AllowPersistentMemoryRead = allowed && actual.RequestedContextConstraints?.AllowPersistentMemoryRead != false }
        };
    }

    private sealed class IssuedTemporaryMemoryReadChoice(Conversation conversation, string prompt, CancellationToken token)
    {
        internal readonly Conversation Conversation = conversation;
        internal readonly string Prompt = prompt;
        internal readonly CancellationToken Token = token;
        internal int Consumed;
    }
}
