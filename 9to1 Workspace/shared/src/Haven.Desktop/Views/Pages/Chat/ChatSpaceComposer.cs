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
    private readonly Func<bool>? _canStop;

    public ChatSpaceComposerSource(
        Func<ChatSpaceViewState> state,
        Func<string, object?, CancellationToken, ValueTask> dispatch,
        Func<IChatComposerInputPort?>? inputPort = null,
        Func<bool>? canStop = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _inputPort = inputPort;
        _canStop = canStop;
    }

    private ChatSpaceViewState Snapshot => _state();

    /// <summary>The live shared composer input port, when the shared view is attached. Null when
    /// not yet mounted, in which case editing falls back to the controller's whole-draft state.</summary>
    private IChatComposerInputPort? LiveInput => _inputPort?.Invoke();

    public string Draft
    {
        get => Snapshot.Composer.Draft;
        set => _dispatch("chat.update-draft", value ?? string.Empty, CancellationToken.None);
    }

    public string Placeholder => "Ask Haven anything";
    public bool IsSending => Snapshot.Composer.IsSending;
    public bool CanStop => _canStop?.Invoke() == true;
    public bool HasPendingMessage => !string.IsNullOrWhiteSpace(Draft) || Snapshot.Composer.Attachments.Count > 0;
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

    public ValueTask FormatSelection(string command)
    {
        var input = LiveInput;
        if (input is not null && input.SelectionLength > 0)
        {
            var start = input.SelectionStart;
            var text = input.Text;
            var selected = text.Substring(start, input.SelectionLength);
            var wrapped = Wrap(selected, command);
            if (string.Equals(wrapped, selected, StringComparison.Ordinal)) return ValueTask.CompletedTask;
            input.Text = text.Remove(start, input.SelectionLength).Insert(start, wrapped);
            input.SetSelection(start, start + wrapped.Length);
            return ValueTask.CompletedTask;
        }
        return FormatDraftWith(command);
    }

    public ValueTask InsertEmoji(string emoji)
    {
        var input = LiveInput;
        if (input is not null)
        {
            var text = input.Text ?? string.Empty;
            var start = Math.Clamp(input.SelectionStart, 0, text.Length);
            var length = Math.Clamp(input.SelectionLength, 0, text.Length - start);
            input.Text = text.Remove(start, length).Insert(start, emoji);
            input.SetSelection(start + emoji.Length, start + emoji.Length);
            input.Focus();
            return ValueTask.CompletedTask;
        }
        return InsertEmojiIntoDraft(emoji);
    }

    public ValueTask CancelEdit() => _dispatch("chat.cancel-edit", null, CancellationToken.None);

    private static string Wrap(string text, string command) => command switch
    {
        "Bold" => $"**{text}**",
        "Italic" => $"_{text}_",
        "Code" => $"`{text}`",
        "Link" => $"[{text}](url)",
        "Quote" => $"> {text}",
        "Bullet" => $"- {text}",
        _ => text
    };

    private ValueTask FormatDraftWith(string command)
    {
        var draft = Snapshot.Composer.Draft ?? string.Empty;
        if (string.IsNullOrWhiteSpace(draft)) return ValueTask.CompletedTask;
        var wrapped = Wrap(draft, command);
        if (string.Equals(wrapped, draft, StringComparison.Ordinal)) return ValueTask.CompletedTask;
        return _dispatch("chat.update-draft", wrapped, CancellationToken.None);
    }

    private ValueTask InsertEmojiIntoDraft(string emoji)
    {
        if (string.IsNullOrEmpty(emoji)) return ValueTask.CompletedTask;
        var draft = Snapshot.Composer.Draft ?? string.Empty;
        return _dispatch("chat.update-draft", draft + emoji, CancellationToken.None);
    }
}
