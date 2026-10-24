using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed partial class ChatHavenScene
{
    /// <summary>
    /// Live data source bridging the shared CUI composer view to the real Haven Chat scene
    /// (queue-aware send, catalogue popup, chips, typed input). No state is cloned; the scene
    /// remains the single authority. Action methods execute the real operation and report the
    /// admitted lifetime through the returned ValueTask.
    /// </summary>
    internal sealed class HavenComposerSource : IChatComposerSource
    {
        private readonly ChatHavenScene _scene;
        public HavenComposerSource(ChatHavenScene scene) => _scene = scene;

        public string Draft { get => _scene.Input.Text; set => _scene.Input.Text = value ?? string.Empty; }
        public string Placeholder { get; set; } = "Ask Haven anything";
        public bool IsSending => _scene._isSending;
        public bool CanStop => _scene._isSending;
        public bool IsSafetyLocked => _scene._safetyLocked;
        public bool HasAdd { get; set; } = true;
        public bool CanSend => !_scene._safetyLocked &&
            (_scene._isSending || (_scene._modelAvailable &&
                (!string.IsNullOrWhiteSpace(_scene.Input.Text) || _scene.HasQueuedComposerMessages)));
        public bool IsEditing => false;
        public IReadOnlyList<ChatComposerChip> Chips { get; set; } = [];

        public ValueTask Send() => _scene.HandleComposerSendAsync();
        public ValueTask Stop() { _scene.StopRequested?.Invoke(_scene, EventArgs.Empty); return ValueTask.CompletedTask; }
        public ValueTask Add() { _scene.ShowAddMenu(); return ValueTask.CompletedTask; }
        public ValueTask OpenSettings() { _scene.OpenComposerSettings(); return ValueTask.CompletedTask; }
        public ValueTask CloseSettings() => ValueTask.CompletedTask;
        public ValueTask SetActionMode(string mode) { _scene.SelectComposerActionMode(mode); return ValueTask.CompletedTask; }
        public ValueTask SetResponseMode(string mode) { _scene.SelectComposerResponseMode(mode); return ValueTask.CompletedTask; }
        public ValueTask InvokeChip(string id) { _scene.AttachmentInvoked?.Invoke(_scene, id); return ValueTask.CompletedTask; }
        public ValueTask RemoveChip(string id) { _scene._attachmentRemoveRequested?.Invoke(_scene, id); return ValueTask.CompletedTask; }
        public ValueTask FormatSelection(string command) { _scene.FormatComposerSelection(command); return ValueTask.CompletedTask; }
        public ValueTask InsertEmoji(string emoji) { _scene.InsertComposerEmoji(emoji); return ValueTask.CompletedTask; }
        public ValueTask CancelEdit() => ValueTask.CompletedTask;
    }

    /// <summary>Formatting edits the current selection in the real CUI input using the actual
    /// selection endpoints, preserving forward/backward selection and the caret.</summary>
    public void FormatComposerSelection(string command)
    {
        if (Input.SelectionLength <= 0) return;
        var start = Input.SelectionStart;
        var text = Input.Text;
        var selected = text.Substring(start, Input.SelectionLength);
        var wrapped = command switch
        {
            "Bold" => $"**{selected}**",
            "Italic" => $"_{selected}_",
            "Code" => $"`{selected}`",
            "Link" => $"[{selected}](url)",
            "Quote" => $"> {selected}",
            "Bullet" => $"- {selected}",
            "Strikethrough" => $"~~{selected}~~",
            _ => selected
        };
        if (string.Equals(wrapped, selected, StringComparison.Ordinal)) return;
        Input.Text = text.Remove(start, Input.SelectionLength).Insert(start, wrapped);
        Input.SetSelection(start, start + wrapped.Length);
    }

    /// <summary>Inserts a chosen emoji at the real caret, replacing any active selection and
    /// keeping focus in the composer input.</summary>
    public void InsertComposerEmoji(string emoji)
    {
        if (string.IsNullOrEmpty(emoji)) return;
        var text = Input.Text ?? string.Empty;
        var start = Math.Clamp(Input.SelectionStart, 0, text.Length);
        var length = Math.Clamp(Input.SelectionLength, 0, text.Length - start);
        var next = text.Remove(start, length).Insert(start, emoji);
        Input.Text = next;
        Input.SetSelection(start + emoji.Length, start + emoji.Length);
        Input.Focus();
    }

    /// <summary>Emoji is inserted through the typed port (see <see cref="InsertComposerEmoji"/>);
    /// this legacy entry point remains only to keep the retired HUI surface out of presentation.</summary>
    public void OpenComposerEmoji() => Input.Focus();
}
