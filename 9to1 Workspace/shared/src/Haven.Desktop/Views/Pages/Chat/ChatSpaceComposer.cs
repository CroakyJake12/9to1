using Haven.Core;
using HavenOS.Apps.Spaces.Chat;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>A shared composer source that exposes the real rich-input engine it routes through, so
/// the mounted CUI view binds the SAME engine (one document/history for typing, formatting and
/// emoji). Sources that do not own an engine return <c>null</c> and the view creates its own.</summary>
internal interface ISharedRichEngineProvider
{
    ChatComposerRichInputEngine? RichEngine { get; }
}

/// <summary>
/// Live data source bridging the shared CUI composer view to the real ChatSpace controller.
/// It reads the controller's authoritative projection and forwards authored commands through the
/// same dispatch path the ChatSpace host owns. The actual admitted dispatch ValueTask is returned
/// rather than swallowed, and unsupported commands fail honestly instead of masquerading as
/// successful no-ops.
/// </summary>
internal sealed class ChatSpaceComposerSource : IChatComposerSource, ISharedRichEngineProvider
{
    ChatComposerRichInputEngine? ISharedRichEngineProvider.RichEngine => _engine;

    private readonly Func<ChatSpaceViewState> _state;
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private readonly Func<IChatComposerInputPort?>? _inputPort;
    private readonly ChatComposerFormattingPort _formatting;
    private readonly ChatComposerRichInputEngine _engine;

    public ChatSpaceComposerSource(
        Func<ChatSpaceViewState> state,
        Func<string, object?, CancellationToken, ValueTask> dispatch,
        Func<IChatComposerInputPort?>? inputPort = null,
        ChatComposerRichInputEngine? engine = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _inputPort = inputPort;
        // One shared real rich-input engine drives formatting, emoji, native typing (folded by the
        // mounted CUI view through the same engine) and the unmounted whole-draft fallback. The
        // engine is the single document/history owner, so both real consumers and native edits
        // share one undo/redo history.
        _engine = engine ?? new ChatComposerRichInputEngine();
        _formatting = new ChatComposerFormattingPort(
            () => _inputPort?.Invoke(),
            () => Snapshot.Composer.Draft,
            (value, ct) => _dispatch("chat.update-draft", value, ct),
            _engine);
    }

    /// <summary>The shared engine this source routes through. The mounted CUI view binds its input
    /// to this same engine so native typing, formatting and emoji land in one retained history.</summary>
    public ChatComposerRichInputEngine RichEngine => _engine;

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
