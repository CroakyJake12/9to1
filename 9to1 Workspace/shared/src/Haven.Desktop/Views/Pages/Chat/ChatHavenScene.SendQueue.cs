namespace Haven.Desktop.Views.Pages.Chat;

internal sealed partial class ChatHavenScene
{
    private ChatComposerQueue? _composerQueue;
    private bool _queuedSendingEnabled;

    /// <summary>
    /// Narrow typed async owner callback set by the owning page/factory. It returns the SAME
    /// admitted send task the owner runs for a direct send. The composer source and queue await
    /// and return it instead of discarding the owner's async work.
    /// </summary>
    internal Func<CancellationToken, ValueTask>? ComposerDirectSend;

    private IChatComposerTaskActions? ActiveTaskComposer =>
        _composerSource.TaskActions is { IsApplicable: true } actual ? actual : null;
    private ValueTask SubmitTaskComposerAsync(IChatComposerTaskActions actual) => actual.CanSubmit
        ? actual.Submit(alternate: false) : ValueTask.FromException(new InvalidOperationException("Choose an available task action. The task draft has been retained."));

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
            () => RaiseSendRequestedAsync(),
            () => StopRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Runs the direct send admission. When the owning page has wired an async owner it returns
    /// that SAME admitted send task; otherwise it falls back to the legacy synchronous event.
    /// </summary>
    internal ValueTask RaiseSendRequestedAsync()
    {
        if (ComposerDirectSend is not null) return ComposerDirectSend(default);
        SendRequested?.Invoke(this, EventArgs.Empty);
        return ValueTask.CompletedTask;
    }
}