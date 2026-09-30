using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;

namespace HavenOS.Apps.Terminal.NativeUI;

/// <summary>A trusted host adapter over its actual terminal viewport. This interface grants no process authority.</summary>
public interface ITerminalNativeViewport
{
    Control View { get; }
    void Attach(ITerminalInteractiveSession session, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> sendInput);
    void Detach();
    /// <summary>AI mode must not send typed viewport input directly to the PTY.</summary>
    void SetInteractiveInputEnabled(bool enabled);
}

/// <summary>The host retains ownership of the actual surface and viewport lifetimes.</summary>
public sealed class TerminalCuiWorkspace : ICuiActionDispatcher, IDisposable
{
    private readonly TerminalAppSurface _surface;
    private readonly ITerminalNativeViewport _viewport;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private ITerminalInteractiveSession? _attached;
    private (Guid ActionId, string RequestId)? _review;
    private volatile bool _disposed;
    public CuiViewModel Bindings { get; } = new();
    public Control Viewport => _viewport.View;

    public TerminalCuiWorkspace(TerminalAppSurface surface, ITerminalNativeViewport viewport)
    {
        Dispatcher.UIThread.VerifyAccess();
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        Bindings.Set("InputText", "");
        Bindings.Set("Status", "Ready.");
        _surface.MetadataChanged += OnMetadataChanged;
        Refresh();
    }

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Capture the displayed session and input before queuing. A delayed click cannot
        // silently target a replacement session or newly edited command.
        var sessionId = _surface.SessionMetadata?.SessionId;
        var mode = _surface.Mode;
        var input = Bindings.Get("InputText")?.ToString() ?? "";
        var resolved = _surface.ResolvedAction;
        await _operations.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_surface.SessionMetadata?.SessionId != sessionId || _surface.Mode != mode)
                throw new InvalidOperationException("The Terminal session context changed before the action ran.");
            switch (command)
            {
                case "TerminalCommandMode":
                    _review = null; _surface.SetMode(TerminalInputMode.Command); break;
                case "TerminalAiMode":
                    _review = null; _surface.SetMode(TerminalInputMode.AI); break;
                case "TerminalNewSession":
                    _review = null;
                    Bindings.Set("Status", _surface.NewSession() ? "New session ready." : "The new session could not be started.");
                    break;
                case "TerminalSubmit":
                    _review = null;
                    var submitted = await _surface.SubmitAsync(input, cancellationToken);
                    if (!_disposed) Bindings.Set("Status", submitted.Message);
                    break;
                case "TerminalExecuteResolved":
                    if (resolved is null || _surface.ResolvedAction?.Id != resolved.Id)
                        throw new InvalidOperationException("Resolve an action for this session first.");
                    var reference = _review is { } retained && retained.ActionId == resolved.Id ? retained.RequestId : null;
                    var execution = await _surface.ExecuteResolvedActionAsync(resolved.Id.ToString("D"), reference, cancellationToken);
                    _review = execution.State == TerminalAppCommandState.RequiresApproval ? (resolved.Id, execution.Message) : null;
                    if (!_disposed) Bindings.Set("Status", execution.State == TerminalAppCommandState.RequiresApproval
                        ? "Review this action in Home, then retry it here." : execution.Message);
                    break;
                default: throw new InvalidOperationException("Unknown Terminal action.");
            }
            if (!_disposed) Refresh();
        }
        finally { _operations.Release(); }
    }

    private void OnMetadataChanged(object? sender, TerminalSessionMetadata metadata) => Dispatcher.UIThread.Post(() =>
    {
        if (!_disposed) Refresh();
    });

    private void Refresh()
    {
        var session = _surface.InteractiveSession;
        if (!ReferenceEquals(session, _attached))
        {
            _viewport.Detach();
            _attached = session;
            _review = null;
            if (session is not null)
            {
                var attachedId = session.Metadata.SessionId;
                _viewport.Attach(session, (bytes, ct) =>
                {
                    if (_disposed || !ReferenceEquals(_attached, session))
                        throw new UnauthorizedAccessException("The terminal viewport was detached.");
                    return _surface.SendInteractiveInputAsync(attachedId, bytes, ct);
                });
            }
        }
        _viewport.SetInteractiveInputEnabled(session is not null && _surface.Mode == TerminalInputMode.Command);
        Bindings.Set("Mode", _surface.ModeLabel);
        Bindings.Set("WorkingDirectory", _surface.WorkingDirectory);
        Bindings.Set("ActionSummary", _surface.ResolvedAction?.Summary ?? "");
        Bindings.Set("ApprovalRequestId", _review?.RequestId ?? "");
        Bindings.Set("Availability", _surface.UnavailableReason ?? "");
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _surface.MetadataChanged -= OnMetadataChanged;
        _viewport.SetInteractiveInputEnabled(false);
        _viewport.Detach();
        _attached = null;
        _review = null;
        // An in-flight owner operation may still release the semaphore. Neither the
        // surface nor viewport is disposed here; their trusted host owns both.
    }
}
