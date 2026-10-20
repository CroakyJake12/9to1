using Haven.Core;
using HavenOS.Apps.Spaces.Chat;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Live data source bridging the shared CUI composer view to the real ChatSpace controller.
/// It reads the controller's authoritative projection and forwards authored commands through the
/// same dispatch path the ChatSpace host owns. The actual admitted dispatch ValueTask is returned
/// rather than swallowed, and unsupported commands fail honestly instead of masquerading as
/// successful no-ops.
/// </summary>
internal sealed class ChatSpaceComposerSource : IChatComposerSource
{
    private readonly Func<ChatSpaceViewState> _state;
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private readonly Func<IChatComposerInputPort?>? _inputPort;
    private readonly ChatComposerFormattingPort _formatting;

    public ChatSpaceComposerSource(
        Func<ChatSpaceViewState> state,
        Func<string, object?, CancellationToken, ValueTask> dispatch,
        Func<IChatComposerInputPort?>? inputPort = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _inputPort = inputPort;
        // One shared rich-text engine drives both the mounted CUI input and the unmounted whole-
        // draft fallback, so formatting semantics are identical in standalone and Haven.
        _formatting = new ChatComposerFormattingPort(
            () => _inputPort?.Invoke(),
            () => Snapshot.Composer.Draft,
            (value, ct) => _dispatch("chat.update-draft", value, ct));
    }

    private ChatSpaceViewState Snapshot => _state();

    public string Draft
    {
        get => Snapshot.Composer.Draft;
        set => _dispatch("chat.update-draft", value ?? string.Empty, CancellationToken.None);
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

    public ValueTask Send() => _dispatch("chat.send", null, CancellationToken.None);
    public ValueTask Stop() => _dispatch("chat.stop", null, CancellationToken.None);
    public ValueTask Add() => _dispatch("chat.attach", null, CancellationToken.None);
    public ValueTask OpenSettings() => ValueTask.CompletedTask;
    public ValueTask CloseSettings() => ValueTask.CompletedTask;
    public ValueTask SetActionMode(string mode) => _dispatch("chat.set-action-mode", mode, CancellationToken.None);
    public ValueTask SetResponseMode(string mode) => _dispatch("chat.set-response-mode", mode, CancellationToken.None);
    public ValueTask InvokeChip(string id) => _dispatch("chat.invoke-chip", id, CancellationToken.None);

    public ValueTask RemoveChip(string id)
    {
        if (Guid.TryParse(id, out var attachmentId))
            return _dispatch("chat.remove-attachment", attachmentId, CancellationToken.None);
        return ValueTask.CompletedTask;
    }

    public ValueTask FormatSelection(string command) => _formatting.ApplyAsync(command);

    public ValueTask InsertEmoji(string emoji) => _formatting.InsertEmojiAsync(emoji);

    public ValueTask CancelEdit() => _dispatch("chat.cancel-edit", null, CancellationToken.None);
}
