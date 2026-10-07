using NineToOne.Cui.AI;

namespace HavenOS.Apps.Canvas;

public enum CanvasAiSurfaceVisibility { Hidden, Expanded }
public enum CanvasAiLifecycle { Idle, Active, WaitingForUser, Completed, Failed }
public enum CanvasAiSessionIndicator { None, Active, WaitingForUser, CompletedAttention, ErrorAttention }
public sealed record CanvasAiSurfaceSnapshot(bool IsAiToolSelected, CanvasAiSurfaceVisibility Visibility,
    CanvasAiLifecycle Lifecycle, CanvasAiSessionIndicator Indicator);

/// <summary>Canvas presentation over the actual shared coordinator/bar instance; hiding never cancels its work.</summary>
public sealed class CanvasAiSurfaceState : IDisposable
{
    private readonly FloatingAiBarState _sharedSession;
    private readonly object _gate = new();
    private bool _selected;
    private bool _attention;
    private bool _disposed;
    private CanvasAiLifecycle _observedLifecycle;

    public CanvasAiSurfaceState(FloatingAiBarState sharedSession)
    {
        _sharedSession = sharedSession ?? throw new ArgumentNullException(nameof(sharedSession));
        _observedLifecycle = Lifecycle();
        _sharedSession.Changed += OnSessionChanged;
    }

    public FloatingAiBarState SharedSession => _sharedSession;
    // The shared runtime does not yet publish a persistent session ID. Retain
    // this exact controller instance; do not create a cosmetic private registry.
    public string SessionIdentityCapability => "Persistent shared session ID and restart restoration are unavailable";
    public event EventHandler? Changed;

    public CanvasAiSurfaceSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                var lifecycle = Lifecycle();
                var indicator = _selected ? CanvasAiSessionIndicator.None : lifecycle switch
                {
                    CanvasAiLifecycle.Active => CanvasAiSessionIndicator.Active,
                    CanvasAiLifecycle.WaitingForUser => CanvasAiSessionIndicator.WaitingForUser,
                    CanvasAiLifecycle.Completed when _attention => CanvasAiSessionIndicator.CompletedAttention,
                    CanvasAiLifecycle.Failed when _attention => CanvasAiSessionIndicator.ErrorAttention,
                    _ => CanvasAiSessionIndicator.None,
                };
                return new(_selected, _selected ? CanvasAiSurfaceVisibility.Expanded : CanvasAiSurfaceVisibility.Hidden, lifecycle, indicator);
            }
        }
    }

    public void SelectAiTool()
    {
        lock (_gate) { EnsureOpen(); _selected = true; _attention = false; }
        _sharedSession.Expand();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SelectOtherTool()
    {
        lock (_gate) { EnsureOpen(); _selected = false; }
        // Shared Collapse currently cancels the request. Presentation visibility
        // belongs to Canvas and is independent of the shared session lifecycle.
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void DismissAttention()
    {
        lock (_gate) { EnsureOpen(); _attention = false; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void CancelSession()
    {
        lock (_gate) EnsureOpen();
        _sharedSession.Cancel();
    }

    private void OnSessionChanged(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var lifecycle = Lifecycle();
            if (lifecycle is CanvasAiLifecycle.Active or CanvasAiLifecycle.WaitingForUser) _attention = false;
            else if (!_selected && lifecycle != _observedLifecycle && lifecycle is CanvasAiLifecycle.Completed or CanvasAiLifecycle.Failed) _attention = true;
            _observedLifecycle = lifecycle;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private CanvasAiLifecycle Lifecycle() => _sharedSession.RequestState switch
    {
        AppAiRequestState.CapturingContext or AppAiRequestState.Generating or AppAiRequestState.ExecutingAction => CanvasAiLifecycle.Active,
        AppAiRequestState.WaitingForApproval => CanvasAiLifecycle.WaitingForUser,
        AppAiRequestState.Completed => CanvasAiLifecycle.Completed,
        AppAiRequestState.Failed => CanvasAiLifecycle.Failed,
        _ when _sharedSession.Mode == FloatingAiBarMode.Error => CanvasAiLifecycle.Failed,
        _ => CanvasAiLifecycle.Idle,
    };

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _sharedSession.Changed -= OnSessionChanged;
        // The shared session owner manages lifetime. A CUI surface teardown
        // must not silently cancel authorised work owned by that session.
    }

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(_disposed, this);
}
