using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Canonical owner-facing Chat presentation adapter. It does not own state; it projects the
/// existing <see cref="NewChatPage"/> owner into typed presentation data and exposes the
/// typed composer input port.
/// </summary>
public sealed class ChatCuiPresentation
{
    private readonly NewChatPage _owner;
    public ChatCuiPresentation(NewChatPage owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public NewChatPage Owner => _owner;
    public IChatComposerInputPort Input => _owner.ComposerInput;

    public ChatPresentationState State => new(
        _owner.CurrentConversation.Id,
        _owner.CurrentConversation.Title,
        _owner.ProjectionState.StatusText,
        _owner.IsSending,
        _owner.IsSafetyLocked,
        _owner.ModelAvailability,
        _owner.ComposerInput.Text,
        _owner.PendingComposerAttachments);
}

public sealed record ChatPresentationState(
    Guid ConversationId,
    string ConversationTitle,
    string? StatusText,
    bool IsSending,
    bool IsSafetyLocked,
    ChatModelAvailability ModelAvailability,
    string ComposerDraft,
    IReadOnlyList<MessageAttachment> ComposerAttachments);
