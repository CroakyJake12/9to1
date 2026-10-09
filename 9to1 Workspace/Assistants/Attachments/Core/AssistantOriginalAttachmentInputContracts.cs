using Haven.Application;

namespace HavenOS.Apps.Assistants.Contracts;

/// <summary>Optional saved-input source over the SAME actual attachment owner.
/// Preparation retains current membership and protected import provenance; it
/// grants no model invocation or cloud disclosure.</summary>
public interface IAssistantOriginalAttachmentInputOwner : IChatOriginalAttachmentInputSource
{
    bool HasOriginalInputComposition(ChatSessionService chat, TaskExecutionCoordinator tasks);
    Task<IChatOriginalAttachmentInput> PrepareOriginalAttachmentInputWithinSourceAsync(
        AssistantConversationBinding binding, string prompt, IReadOnlyList<Guid> attachmentIds,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}
