using Avalonia.Threading;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Nonvisual owner of the in-memory FIFO composer send queue. The HUI/CUI surface only
/// supplies a typed text port and semantic callbacks; queue limits, empty-composer Stop,
/// no-clobber of later typing and pump shutdown live here instead of in presentation markup.
/// </summary>
internal sealed class ChatComposerQueue : IDisposable
{
    private const int QueuedComposerMessageLimit = 8;
    private readonly Func<string> _getText;
    private readonly Action<string> _setText;
    private readonly Func<bool> _isSending;
    private readonly Func<bool> _isSafetyLocked;
    private readonly Action<string> _setStatus;
    private readonly Func<ValueTask> _sendRequested;
    private readonly Action _stopRequested;
    private readonly Queue<string> _queuedComposerMessages = new();
    private DispatcherTimer? _queuedSendPump;
    private bool _disposed;

    public ChatComposerQueue(
        Func<string> getText,
        Action<string> setText,
        Func<bool> isSending,
        Func<bool> isSafetyLocked,
        Action<string> setStatus,
        Func<ValueTask> sendRequested,
        Action stopRequested)
    {
        _getText = getText ?? throw new ArgumentNullException(nameof(getText));
        _setText = setText ?? throw new ArgumentNullException(nameof(setText));
        _isSending = isSending ?? throw new ArgumentNullException(nameof(isSending));
        _isSafetyLocked = isSafetyLocked ?? throw new ArgumentNullException(nameof(isSafetyLocked));
        _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
        _sendRequested = sendRequested ?? throw new ArgumentNullException(nameof(sendRequested));
        _stopRequested = stopRequested ?? throw new ArgumentNullException(nameof(stopRequested));
    }

    public bool HasQueuedComposerMessages => _queuedComposerMessages.Count > 0;

    public IReadOnlyList<string> QueuedComposerMessages => _queuedComposerMessages.ToArray();

    /// <summary>Admits one composer send. A direct (not-yet-sending) send returns the SAME
    /// admitted send task the owner runs; an empty-composer Stop or a queued admission resolves
    /// synchronously with the queue/dispatch state fully applied. No send task is swallowed.</summary>
    public ValueTask HandleSendInvokedAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        if (!_isSending())
            return _sendRequested();

        var draft = _getText().Trim();
        if (string.IsNullOrWhiteSpace(draft))
        {
            _stopRequested();
            return ValueTask.CompletedTask;
        }

        if (_queuedComposerMessages.Count >= QueuedComposerMessageLimit)
        {
            _setStatus($"Message queue is full ({QueuedComposerMessageLimit}). Wait for a queued message to send or stop the current response.");
            return ValueTask.CompletedTask;
        }

        _queuedComposerMessages.Enqueue(draft);
        _setText(string.Empty);
        _setStatus(_queuedComposerMessages.Count == 1
            ? "Message queued. Clear the composer and press Stop to cancel the active response."
            : $"{_queuedComposerMessages.Count} messages queued.");
        StartQueuedSendPump();
        return ValueTask.CompletedTask;
    }

    private void StartQueuedSendPump()
    {
        if (_disposed) return;
        if (_queuedSendPump is not null)
        {
            if (!_queuedSendPump.IsEnabled) _queuedSendPump.Start();
            return;
        }

        _queuedSendPump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _queuedSendPump.Tick += OnQueuedSendPumpTick;
        _queuedSendPump.Start();
    }

    private void OnQueuedSendPumpTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            _queuedSendPump?.Stop();
            return;
        }
        if (_isSending() || _isSafetyLocked()) return;
        if (_queuedComposerMessages.Count == 0)
        {
            _queuedSendPump?.Stop();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_getText())) return;
        if (_disposed) { _queuedSendPump?.Stop(); return; }

        var next = _queuedComposerMessages.Dequeue();
        _setText(next);
        _setStatus(_queuedComposerMessages.Count == 0
            ? "Sending queued message…"
            : $"Sending queued message… {_queuedComposerMessages.Count} still queued.");
        if (_disposed) { _queuedSendPump?.Stop(); return; }
        // Timer-generated sends remain parent-owned; the pump does not wait for their settlement.
        _ = _sendRequested();

        if (_disposed) { _queuedSendPump?.Stop(); return; }
        if (_queuedComposerMessages.Count == 0) _queuedSendPump?.Stop();
    }

    public void Dispose()
    {
        _disposed = true;
        _queuedSendPump?.Stop();
        _queuedSendPump = null;
    }
}