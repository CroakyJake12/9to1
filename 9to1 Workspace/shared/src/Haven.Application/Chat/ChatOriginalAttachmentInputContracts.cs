using Haven.Core;

namespace Haven.Application;

/// <summary>Durable restriction and source observation only. It never reconstructs a
/// live selection, resource permission, model permission or accepted import.</summary>
public sealed record ChatOriginalAttachmentLineage(int Schema, Guid ConversationId,
    Guid? BranchId, string SnapshotSha256, IReadOnlyList<Guid> AttachmentIds);

public interface IChatOriginalAttachmentInput { }

/// <summary>Privately issued by the SAME Chat invocation. Public observations do
/// not issue a message, claim its write or authorize a different invocation.</summary>
public sealed class ChatOriginalAttachmentRequest
{
    public Conversation Conversation { get; }
    public string Prompt { get; }
    internal ChatOriginalAttachmentRequest(Conversation conversation, string prompt)
    { Conversation = conversation; Prompt = prompt; }
}
public sealed class ChatOriginalAttachmentAcceptance
{
    public ChatMessage OriginalMessage { get; }
    internal ChatOriginalAttachmentAcceptance(ChatMessage message) { OriginalMessage = message; }
}

/// <summary>The SAME configured producer reads only its privately prepared saved
/// selection. Empty IDs never mean all attachments. Every original source and
/// cleanup Task is retained before a caller's post-callback guard can refuse.</summary>
public interface IChatOriginalAttachmentInputSource
{
    bool IsIssuedOriginalAttachmentInput(IChatOriginalAttachmentInput input);
    ChatOriginalAttachmentLineage ObserveOriginalAttachmentLineage(IChatOriginalAttachmentInput input);
    Task<AttachmentPromptContext> ReadOriginalAttachmentInputWithinSourceAsync(
        IChatOriginalAttachmentInput input, ChatOriginalAttachmentRequest request,
        ProviderExecutionContext? context, Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task ValidateOriginalAttachmentInputWithinSourceAsync(
        IChatOriginalAttachmentInput input, ChatOriginalAttachmentRequest request,
        ProviderExecutionContext? context, Action<Action> scope, Action<Task> retain, CancellationToken token);
}
