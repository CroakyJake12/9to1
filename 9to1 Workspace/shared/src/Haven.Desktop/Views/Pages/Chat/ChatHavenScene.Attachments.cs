using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed record ChatAttachmentChip(string Id, string Label, string IconKey, bool Invokable = false);

internal sealed partial class ChatHavenScene
{
    private EventHandler<string>? _attachmentRemoveRequested;

    public event EventHandler<string>? AttachmentRemoveRequested
    {
        add
        {
            // NewChatPage subscribes while wiring the canonical Chat scene. Use that guaranteed
            // initialization point to install the composer QoL hooks without weakening or
            // duplicating the current ChatSessionService send pipeline.
            EnsureQueuedSendingEnabled();
            _attachmentRemoveRequested += value;
        }
        remove => _attachmentRemoveRequested -= value;
    }

    public event EventHandler<string>? AttachmentInvoked;

    /// <summary>
    /// Projects the owner's composer chips onto the shared CUI composer. Chips include
    /// capability/Agent/Skill/resource identities, not only persisted files; the shared
    /// component renders them and routes invoke/remove back through the real owner.
    /// </summary>
    public void SetAttachmentChips(IReadOnlyList<ChatAttachmentChip> chips)
    {
        ArgumentNullException.ThrowIfNull(chips);
        _composerSource.Chips = chips
            .Select(chip => new ChatComposerChip(chip.Id, chip.Label, chip.Invokable))
            .ToArray();
        ComposerView.RefreshSurface();
    }
}
