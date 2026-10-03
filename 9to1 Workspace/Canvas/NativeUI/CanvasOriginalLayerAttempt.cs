using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Apps.Canvas;

public enum CanvasOriginalLayerAttemptState { Ready, Executing, Unknown, Acknowledged, AuditPending, Completed }

/// <summary>Retains one original claim and its actual owning acknowledgement. Recovery never repeats an owner write.</summary>
public sealed class CanvasOriginalLayerAttempt
{
    private readonly HomeResourceOperationBroker _home;
    private readonly HomeResourceExecutionCapability _capability;
    private readonly Func<CancellationToken, Task<CanvasNativeLayerCommit>> _execute;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private CanvasNativeLayerCommit? _acknowledged;
    private HomeExecutionOutcome? _outcome;
    private int _state;
    private bool _completionAttempted;
    private CanvasOriginalLayerAttempt(HomeResourceOperationBroker home, HomeResourceExecutionCapability capability,
        Func<CancellationToken, Task<CanvasNativeLayerCommit>> execute)
    { _home = home; _capability = capability; _execute = execute; }

    public CanvasOriginalLayerAttemptState State => (CanvasOriginalLayerAttemptState)Volatile.Read(ref _state);
    public CanvasNativeLayerCommit? Acknowledged => Volatile.Read(ref _acknowledged);

    public static CanvasOriginalLayerAttempt Capture(CanvasHomeNativeLayerOperation operation,
        HomeResourceOperationBroker home, CanvasNativeLayerEditIntent intent, HomeResourceExecutionCapability capability)
    {
        ArgumentNullException.ThrowIfNull(operation); ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        if (!operation.IsBoundToHome(home)) throw new ArgumentException("Original layer edit owner and completion must use the same Home broker.", nameof(home));
        return new(home, capability, ct => operation.ExecuteAsync(intent, capability, ct));
    }

    public async Task<CanvasNativeLayerCommit> ExecuteOnceAsync(CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != CanvasOriginalLayerAttemptState.Ready)
                throw new InvalidOperationException("An original Canvas layer edit attempt cannot execute again.");
            Volatile.Write(ref _state, (int)CanvasOriginalLayerAttemptState.Executing);
            CanvasNativeLayerCommit result;
            try { result = await _execute(cancellationToken).ConfigureAwait(false); }
            catch
            {
                // A lost publication acknowledgement is not evidence that the owner made no effect.
                Volatile.Write(ref _state, (int)CanvasOriginalLayerAttemptState.Unknown);
                throw;
            }
            _outcome = new(HomePermissionRequestState.Succeeded, "CANVAS_LAYER_COMMITTED",
                "The original owning Canvas Files revision was acknowledged.",
                [new("files.item", result.FileId.ToString())]);
            Volatile.Write(ref _acknowledged, result);
            Volatile.Write(ref _state, (int)CanvasOriginalLayerAttemptState.Acknowledged);
            return result;
        }
        finally { _serial.Release(); }
    }

    public async Task<HomePermissionOperationResult> CompleteAcknowledgedAsync(CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_acknowledged is null || _outcome is null || _completionAttempted)
                throw new InvalidOperationException("Only an acknowledged original write can begin completion once.");
            _completionAttempted = true;
            Volatile.Write(ref _state, (int)CanvasOriginalLayerAttemptState.AuditPending);
            var result = await _home.CompleteExecutionAsync(_capability, _outcome, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded) Volatile.Write(ref _state, (int)CanvasOriginalLayerAttemptState.Completed);
            return result;
        }
        finally { _serial.Release(); }
    }

    public async Task<HomePermissionOperationResult> RetryCompletionAuditAsync(CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_acknowledged is null || _outcome is null || !_completionAttempted)
                throw new InvalidOperationException("Audit recovery requires a retained owning acknowledgement and completion attempt.");
            // Seed or retry the SAME immutable outcome: a cancelled first completion may not have retained it.
            // Broker exact-outcome idempotence forbids substitution; no owning execution is reachable.
            var result = await _home.CompleteExecutionAsync(_capability, _outcome, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded) Volatile.Write(ref _state, (int)CanvasOriginalLayerAttemptState.Completed);
            return result;
        }
        finally { _serial.Release(); }
    }
}
