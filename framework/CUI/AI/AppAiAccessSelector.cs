namespace NineToOne.Cui.AI;

/// <summary>
/// Typed owner-observed AI access status kind. The selector renders only genuinely supplied
/// observations; it never invents a grant, trust lifetime, or approval policy (see DEC-001).
/// </summary>
public enum AppAiAccessStatusKind
{
    None = 0,
    Pending,
    Denied,
    Unavailable,
}

/// <summary>
/// An observed permission/approval fact for the AI access selector. Carries only data the owning
/// Home/Dulche broker actually reported; it is not a grant, token, or re-execution handle.
/// </summary>
public sealed record AppAiAccessStatus(AppAiAccessStatusKind Kind, string Message);

/// <summary>
/// Optional typed source of real AI access/approval observations. Implementations report only
/// what the owning broker actually observed. A null <see cref="Current"/> means no status is known
/// and the selector presents none; the selector never synthesizes its own observation.
/// </summary>
public interface IAppAiAccessStatusSource
{
    AppAiAccessStatus? Current { get; }
    event EventHandler? Changed;
}

/// <summary>
/// Pure mapping from genuine typed owner observations to presentational status records.
/// This does not grant, deny, queue, or clear anything: it only describes observed outcomes.
/// </summary>
public static class AppAiAccessStatusMapper
{
    public static AppAiAccessStatus? FromOutcome(AppAiApprovalOutcome outcome, string? message = null) =>
        outcome switch
        {
            AppAiApprovalOutcome.Pending => new(AppAiAccessStatusKind.Pending,
                string.IsNullOrWhiteSpace(message) ? "Mode change is awaiting approval." : message),
            AppAiApprovalOutcome.Denied => new(AppAiAccessStatusKind.Denied,
                string.IsNullOrWhiteSpace(message) ? "Mode change was not approved." : message),
            AppAiApprovalOutcome.Unavailable => new(AppAiAccessStatusKind.Unavailable,
                string.IsNullOrWhiteSpace(message) ? "Approval is currently unavailable." : message),
            _ => null
        };

    public static AppAiAccessStatus? FromActionResult(AppAiActionResult result)
    {
        if (result is null) return null;
        return result.ErrorCode switch
        {
            "approval-pending" => new(AppAiAccessStatusKind.Pending, result.Summary),
            "approval-denied" => new(AppAiAccessStatusKind.Denied, result.Summary),
            "approval-unavailable" => new(AppAiAccessStatusKind.Unavailable, result.Summary),
            _ => null
        };
    }
}

/// <summary>
/// Reusable canonical AI access-mode selector adapter. It transports the actual existing
/// <see cref="FloatingAiBarState"/> as the sole authority for the persistent AI Read-only/Write
/// mode: every read reflects live state and every selection delegates to
/// <see cref="FloatingAiBarState.SetAccessMode"/>. The selector owns no separate mode cache and
/// never manufactures a grant or rewrites an approval/action outcome. It is independent of the
/// host application's Edit/View/Suggest state and of the per-action approval flow (DEC-001).
/// </summary>
public sealed class AppAiAccessSelector : IDisposable
{
    private readonly FloatingAiBarState _state;
    private readonly IAppAiAccessStatusSource? _statusSource;
    private bool _disposed;

    public AppAiAccessSelector(FloatingAiBarState state, IAppAiAccessStatusSource? statusSource = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _statusSource = statusSource;
        _state.Changed += OnStateChanged;
        if (_statusSource is not null) _statusSource.Changed += OnStateChanged;
    }

    /// <summary>Raised when the underlying mode or observed status changes.</summary>
    public event EventHandler? Changed;

    public FloatingAiBarState State => _state;
    public AppAiAccessMode AccessMode => _state.AccessMode;
    public bool IsReadOnly => _state.IsReadOnly;
    public bool IsWriteMode => _state.IsWriteMode;
    public string CurrentModeLabel => _state.AccessModeLabel;
    public bool CanChangeMode => !_disposed;
    public AppAiAccessStatus? Status => _statusSource?.Current;
    public bool HasStatus => Status is not null;

    public void SelectReadOnly() => SetMode(AppAiAccessMode.ReadOnly);
    public void SelectWrite() => SetMode(AppAiAccessMode.Write);

    /// <summary>
    /// Delegates to the real authority. Invalid enum values are rejected by the state itself
    /// (ArgumentOutOfRangeException); the selector never swallows or invents a mode.
    /// </summary>
    public void SetMode(AppAiAccessMode accessMode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _state.SetAccessMode(accessMode);
    }

    private void OnStateChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Unsubscribes from both the owned state and the optional status source so no late state or
    /// status change can publish into a closed selector. The selector never disposes the owned
    /// <see cref="FloatingAiBarState"/> or mutates the underlying request/approval lifecycle.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _state.Changed -= OnStateChanged;
        if (_statusSource is not null) _statusSource.Changed -= OnStateChanged;
    }
}
