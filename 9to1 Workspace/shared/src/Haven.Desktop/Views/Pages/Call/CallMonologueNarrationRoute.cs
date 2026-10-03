using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Haven.Application;
using Haven.Application.Call;

namespace Haven.Desktop.Views.Pages.Call;

/// <summary>Trusted composition of the actual local Call owner and canonical playback metadata.
/// Existing CallPage construction leaves this opt-in route absent; no Home/source permission
/// is inferred from the local Call's output selection.</summary>
public sealed class CallMonologueNarrationRoute(CallCoordinator owner, VisionVoiceSessionService sessions)
{
    private readonly CallCoordinator _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private readonly VisionVoiceSessionService _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    public static CallMonologueNarrationRoute? ForActualOwner(ICallCoordinator call, VisionVoiceSessionService sessions) =>
        call is CallCoordinator direct ? new(direct, sessions) :
        call is ResponsiveCallCoordinator responsive && responsive.OriginalNarrationOwner is { } original
            ? new(original, sessions) : null;
    public bool IsBoundTo(ICallCoordinator call) => ReferenceEquals(call, _owner) ||
        call is ResponsiveCallCoordinator responsive && responsive.IsBoundToOriginalNarrationOwner(_owner);
    public CallOriginalNarrationSelection? CaptureOriginalSelection(Guid replyId) =>
        _owner.CaptureOriginalNarration() is { } selection && selection.ReplyId == replyId ? selection : null;
    public CallMonologueNarrationHost? CreateOriginalHost(CallOriginalNarrationSelection selection) =>
        CreateOriginalHost(selection, () => true);
    public CallMonologueNarrationHost? CreateOriginalHost(CallOriginalNarrationSelection selection, Func<bool> originalContainerCurrent)
    {
        ArgumentNullException.ThrowIfNull(originalContainerCurrent);
        return originalContainerCurrent() && _owner.IsOriginalNarrationCurrent(selection)
            ? new(_owner, _sessions, selection, originalContainerCurrent) : null;
    }
    public CallMonologueNarrationHost? CreateOriginalHost() => _owner.CaptureOriginalNarration() is { } selection
        ? CreateOriginalHost(selection) : null;
}

/// <summary>Explicit native admission of the retained completed reply, followed by controls
/// on only its original issued playback. It never reads sources or regenerates model output.</summary>
public sealed class CallMonologueNarrationHost : StackPanel, IDisposable
{
    private readonly CallCoordinator _owner;
    private readonly VisionVoiceSessionService _sessions;
    private readonly CallOriginalNarrationSelection _selection;
    private readonly Func<bool> _originalContainerCurrent;
    private readonly Button _start = new() { Content = "Narrate this completed reply" };
    private readonly TextBlock _status = new();
    private readonly CancellationTokenSource _lifetime = new();
    private MonologueOriginalPlaybackHost? _controls;
    private volatile bool _retired;
    private bool _attempted;
    private Task _pending = Task.CompletedTask, _observation = Task.CompletedTask;
    internal CallMonologueNarrationHost(CallCoordinator owner, VisionVoiceSessionService sessions,
        CallOriginalNarrationSelection selection, Func<bool> originalContainerCurrent)
    {
        _owner = owner; _sessions = sessions; _selection = selection; _originalContainerCurrent = originalContainerCurrent;
        Children.Add(_start); Children.Add(_status); _start.Click += Start;
    }
    public MonologueOriginalPlayback? OriginalPlayback { get; private set; }
    public Exception? LastFailure { get; private set; }
    public Task WhenActionIdleAsync() => Task.WhenAll(_pending, _observation,
        _controls?.WhenActionIdleAsync() ?? Task.CompletedTask);
    private bool Current() => !_retired && _originalContainerCurrent() && _owner.IsOriginalNarrationCurrent(_selection);
    private void Start(object? sender, RoutedEventArgs args)
    {
        if (_attempted || !Current()) { Dispose(); return; }
        _attempted = true; _start.IsEnabled = false;
        _pending = StartAsync(); _observation = ObserveAsync(_pending);
    }
    private async Task ObserveAsync(Task pending)
    {
        try { await pending; }
        catch (Exception error) { LastFailure = error; }
        finally { if (_retired) _lifetime.Dispose(); }
    }
    private async Task StartAsync()
    {
        try
        {
            var original = await _owner.StartOriginalNarrationAsync(_selection, _sessions, Current, _lifetime.Token);
            OriginalPlayback = original; // Retain a known issued object before the late native surface check.
            if (!Current()) { await original.StopOriginalAsync(CancellationToken.None); return; }
            _controls = new(original, Current); Children.Add(_controls);
            _status.Text = "Original narration opened. Playback output has not been confirmed.";
        }
        catch { if (Current()) _status.Text = "Original narration unavailable; no automatic retry."; throw; }
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { Dispose(); base.OnDetachedFromVisualTree(e); }
    public void Dispose()
    {
        if (_retired) return; _retired = true; _start.IsEnabled = false;
        _start.Click -= Start;
        try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
        _controls?.Dispose();
        if (!_attempted || _observation.IsCompleted) _lifetime.Dispose();
        // Do not dispose the CTS while its accepted asynchronous Start still uses the token.
        // Late Start owns its exact original cleanup; detached controls never adopt it again.
    }
}
