namespace Haven.Desktop.Views.Pages.Chat;

internal sealed partial class ChatHavenScene
{
    private ChatComposerQueue? _composerQueue;
    private bool _queuedSendingEnabled;

    /// <summary>True while at least one typed message is parked in the in-memory send queue.</summary>
    internal bool HasQueuedComposerMessages => _composerQueue?.HasQueuedComposerMessages ?? false;

    /// <summary>The SAME unsent inputs still parked in this owner's in-memory queue, oldest first.</summary>
    internal IReadOnlyList<string> QueuedComposerMessages => _composerQueue?.QueuedComposerMessages ?? Array.Empty<string>();

    /// <summary>
    /// Replaces the streaming send-button handler with a queue-aware variant while preserving
    /// the existing empty-composer Stop behavior. The hook is installed once during Chat wiring.
    /// </summary>
    private void EnsureQueuedSendingEnabled()
    {
        if (_queuedSendingEnabled) return;
        _queuedSendingEnabled = true;
        _composerQueue = new ChatComposerQueue(
            () => Input.Text,
            value => Input.Text = value,
            () => _isSending,
            () => _safetyLocked,
            SetStatus,
            () => SendRequested?.Invoke(this, EventArgs.Empty),
            () => StopRequested?.Invoke(this, EventArgs.Empty));
    }
}