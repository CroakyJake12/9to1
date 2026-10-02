using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Haven.Application.Call;

namespace Haven.Desktop.Views.Pages.Call;

/// <summary>Controls the already issued original utterance. The containing host must have
/// independently admitted narration, voice and output before creating its coordinator.
/// This surface never starts synthesis, reads source references, selects another utterance,
/// or stops an ambient speech provider.</summary>
public sealed class MonologueOriginalPlaybackHost : StackPanel, IDisposable
{
    private readonly MonologueOriginalPlayback _original;
    private readonly Func<bool> _originalHostCurrent;
    private readonly Button _pause = new() { Content = "Pause original narration" };
    private readonly Button _resume = new() { Content = "Resume original narration" };
    private readonly Button _persist = new() { Content = "Retry saved playback checkpoint" };
    private readonly TextBlock _status = new();
    private bool _retired;
    private bool _disposed;
    private bool _busy;
    private bool _paused;
    private Task _pending = Task.CompletedTask;
    private Task _cleanup = Task.CompletedTask;
    private Task _actionObservation = Task.CompletedTask;
    private Task _cleanupObservation = Task.CompletedTask;

    public MonologueOriginalPlaybackHost(MonologueOriginalPlayback original, Func<bool> originalHostCurrent)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(originalHostCurrent);
        if (!original.CanStopOriginal) throw new NotSupportedException("Original playback cleanup is unavailable.");
        _original = original;
        _originalHostCurrent = originalHostCurrent;
        Children.Add(new TextBlock { Text = $"Original voice: {original.VoiceId}" });
        Children.Add(_status); Children.Add(_pause); Children.Add(_resume); Children.Add(_persist);
        _pause.Click += Pause; _resume.Click += Resume; _persist.Click += Persist;
        Update();
    }

    /// <summary>The exact accepted action, including metadata persistence. It does not grant
    /// control, establish audible playback, or adopt a different playback object.</summary>
    public Task WhenActionIdleAsync() => Task.WhenAll(_pending, _cleanup, _actionObservation, _cleanupObservation);
    public VisionVoiceResult<MultimodalSession>? LastObservation { get; private set; }
    /// <summary>The observed accepted-action failure; it does not discard a retained checkpoint.</summary>
    public Exception? LastActionFailure { get; private set; }
    public Exception? LastCleanupFailure { get; private set; }
    public Guid OriginalPlaybackId => _original.PlaybackId;

    private bool Current()
    {
        if (_retired) return false;
        try { if (_originalHostCurrent()) return true; }
        catch { }
        Dispose();
        return false;
    }

    private void Pause(object? sender, RoutedEventArgs args) => Accept(ActionKind.Pause);
    private void Resume(object? sender, RoutedEventArgs args) => Accept(ActionKind.Resume);
    private void Persist(object? sender, RoutedEventArgs args) => Accept(ActionKind.Persist);
    private enum ActionKind { Pause, Resume, Persist }

    private void Accept(ActionKind action)
    {
        if (_busy || !Current() || (action != ActionKind.Persist && _original.Completion.IsCompleted) ||
            (action == ActionKind.Pause && (_paused || _original.PendingCheckpoint is not null)) ||
            (action == ActionKind.Resume && (!_paused || _original.PendingCheckpoint is not null)) ||
            (action == ActionKind.Persist && _original.PendingCheckpoint is null))
        { Update(); return; }
        _busy = true;
        Update();
        _pending = ExecuteAsync(action);
        _actionObservation = ObserveFailureAsync(_pending, cleanup: false);
    }

    private async Task ObserveFailureAsync(Task accepted, bool cleanup)
    {
        // Event dispatch has no awaiting caller. Observe that exact task independently while
        // keeping its fault available to the containing route through WhenActionIdleAsync.
        try { await accepted; }
        catch (Exception error)
        {
            if (cleanup) LastCleanupFailure = error;
            else LastActionFailure = error;
        }
    }

    private async Task ExecuteAsync(ActionKind action)
    {
        try
        {
            var observation = await (action switch
            {
                ActionKind.Pause => _original.PauseAsync(),
                ActionKind.Resume => _original.ResumeAsync(),
                _ => _original.PersistPendingAsync()
            });
            // Preserve a known native/canonical acknowledgement even if the surface retired
            // during the actual owner await. Retirement only denies subsequent UI adoption.
            LastObservation = observation;
            if (!Current()) return;
            if (_original.LastNativeCheckpoint is { } checkpoint) _paused = checkpoint.IsPaused;
            _status.Text = observation.IsSuccess ? "Original playback checkpoint saved."
                : _original.PendingCheckpoint is not null ? "Original checkpoint retained; retry saving it."
                : "Original playback cannot be continued.";
        }
        catch
        {
            if (Current()) _status.Text = _original.PendingCheckpoint is not null
                ? "Original checkpoint retained; retry saving it."
                : "Original playback outcome is unavailable.";
            throw;
        }
        finally { _busy = false; Update(); }
    }

    private void Update()
    {
        if (_original.LastNativeCheckpoint is { } observed) _paused = observed.IsPaused;
        var current = Current() && !_original.Completion.IsCompleted;
        var pending = _original.PendingCheckpoint is not null;
        _pause.IsEnabled = current && !_busy && !_paused && !pending;
        _resume.IsEnabled = current && !_busy && _paused && !pending;
        _persist.IsEnabled = Current() && !_busy && pending;
        if (_retired) _status.Text = "Original narration controls retired.";
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Dispose();
        base.OnDetachedFromVisualTree(e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _retired = true;
        // Retain the exact original cleanup task. It never selects or stops another current utterance.
        _cleanup = _original.StopOriginalAsync();
        _cleanupObservation = ObserveFailureAsync(_cleanup, cleanup: true);
        _pause.Click -= Pause; _resume.Click -= Resume; _persist.Click -= Persist;
        Update();
    }
}
