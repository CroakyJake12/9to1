using Haven.Core;
using HavenOS.Apps.Spaces.Chat;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Live data source bridging the shared CUI composer view to the real ChatSpace controller.
/// It reads the controller's authoritative projection and forwards authored commands through the
/// same dispatch path the ChatSpace host owns. No composer state is cloned.
/// </summary>
internal sealed class ChatSpaceComposerSource : IChatComposerSource
{
    private readonly Func<ChatSpaceViewState> _state;
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;

    public ChatSpaceComposerSource(
        Func<ChatSpaceViewState> state,
        Func<string, object?, CancellationToken, ValueTask> dispatch)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
    }

    private ChatSpaceViewState Snapshot => _state();

    public string Draft
    {
        get => Snapshot.Composer.Draft;
        set => _ = _dispatch("chat.update-draft", value ?? string.Empty, CancellationToken.None);
    }

    public string Placeholder => "Ask Haven anything";
    public bool IsSending => Snapshot.Composer.IsSending;
    public bool IsSafetyLocked => Snapshot.IsSafetyLocked;
    public bool HasAdd => !Snapshot.IsSafetyLocked;
    public bool CanSend => !Snapshot.IsLoading && !Snapshot.IsSafetyLocked && Snapshot.Composer.CanSend;
    public bool IsEditing => Snapshot.Composer.IsEditing;

    public IReadOnlyList<ChatComposerChip> Chips =>
        Snapshot.Composer.Attachments
            .Select(attachment => new ChatComposerChip(attachment.Id.ToString(), attachment.OriginalName, Invokable: false))
            .ToArray();

    public void Send() => _ = _dispatch("chat.send", null, CancellationToken.None);
    public void Stop() { }
    public void Add() => _ = _dispatch("chat.attach", null, CancellationToken.None);
    public void OpenSettings() { }
    public void CloseSettings() { }
    public void SetActionMode(string mode) => _ = _dispatch("chat.set-action-mode", mode, CancellationToken.None);
    public void SetResponseMode(string mode) => _ = _dispatch("chat.set-response-mode", mode, CancellationToken.None);
    public void InvokeChip(string id) { }

    public void RemoveChip(string id)
    {
        if (Guid.TryParse(id, out var attachmentId))
            _ = _dispatch("chat.remove-attachment", attachmentId, CancellationToken.None);
    }

    public void FormatSelection(string command) { }
    public void Emoji() { }
    public void CancelEdit() => _ = _dispatch("chat.cancel-edit", null, CancellationToken.None);
}
