using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed partial class ChatHavenScene
{
    /// <summary>
    /// Live data source bridging the shared CUI composer view to the real Haven Chat scene
    /// (queue-aware send, catalogue popup, chips, typed input). No state is cloned; the scene
    /// remains the single authority.
    /// </summary>
    internal sealed class HavenComposerSource : IChatComposerSource
    {
        private readonly ChatHavenScene _scene;
        public HavenComposerSource(ChatHavenScene scene) => _scene = scene;

        public string Draft { get => _scene.Input.Text; set => _scene.Input.Text = value ?? string.Empty; }
        public string Placeholder { get; set; } = "Ask Haven anything";
        public bool IsSending => _scene._isSending;
        public bool IsSafetyLocked => _scene._safetyLocked;
        public bool HasAdd { get; set; } = true;
        public bool CanSend => !_scene._safetyLocked && (_scene._isSending || !string.IsNullOrWhiteSpace(_scene.Input.Text));
        public bool IsEditing => false;
        public IReadOnlyList<ChatComposerChip> Chips { get; set; } = [];

        public void Send() => _scene.HandleComposerSend();
        public void Stop() => _scene.StopRequested?.Invoke(_scene, EventArgs.Empty);
        public void Add() => _scene.ShowAddMenu();
        public void OpenSettings() => _scene.OpenComposerSettings();
        public void CloseSettings() { }
        public void SetActionMode(string mode) => _scene.SelectComposerActionMode(mode);
        public void SetResponseMode(string mode) => _scene.SelectComposerResponseMode(mode);
        public void InvokeChip(string id) => _scene.AttachmentInvoked?.Invoke(_scene, id);
        public void RemoveChip(string id) => _scene._attachmentRemoveRequested?.Invoke(_scene, id);
        public void FormatSelection(string command) => _scene.FormatComposerSelection(command);
        public void Emoji() => _scene.OpenComposerEmoji();
        public void CancelEdit() { }
    }

    /// <summary>Formatting edits the current selection in the real CUI input.</summary>
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
            "Strikethrough" => $"~~{selected}~~",
            _ => selected
        };
        if (string.Equals(wrapped, selected, StringComparison.Ordinal)) return;
        Input.Text = text.Remove(start, Input.SelectionLength).Insert(start, wrapped);
        Input.SetSelection(start, start + wrapped.Length);
    }

    /// <summary>Emoji opens its real menu while keeping composer focus; insertion stays external/unmigrated.</summary>
    public void OpenComposerEmoji() => Input.Focus();
}
