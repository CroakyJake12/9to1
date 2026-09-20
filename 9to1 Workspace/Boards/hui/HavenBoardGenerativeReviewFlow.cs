using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.Hui;

/// <summary>
/// User-review workflow for generated board plans.
///
/// Providers submit typed proposals for review, but only explicit HUI Apply/Cancel actions can advance
/// the coordinator. The provider never receives the board session or storage boundary.
/// </summary>
public sealed class HavenBoardGenerativeReviewFlow : IAsyncDisposable
{
    private readonly HavenBoardsHuiSession _boardSession;
    private readonly HavenBoardGenerativeUiCoordinator _coordinator;
    private readonly object _queueLock = new();
    private Task _queuedActions = Task.CompletedTask;
    private Guid? _activePlanId;
    private bool _disposed;

    public HavenBoardGenerativeReviewFlow(
        HavenBoardsHuiSession boardSession,
        HavenBoardGenerativeUiCoordinator? coordinator = null)
    {
        _boardSession = boardSession ?? throw new ArgumentNullException(nameof(boardSession));
        _coordinator = coordinator ?? new HavenBoardGenerativeUiCoordinator();
        Scene = new HavenBoardGenerativeReviewHuiScene();
        Scene.ApplyRequested += OnApplyRequested;
        Scene.CancelRequested += OnCancelRequested;
    }

    public HavenBoardGenerativeReviewHuiScene Scene { get; }
    public Guid? ActivePlanId => _activePlanId;
    public Guid? LastAppliedPlanId { get; private set; }

    public HavenBoardGenerativePlan PreparePlan(IEnumerable<HavenBoardCommand> proposedCommands)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(proposedCommands);

        if (_activePlanId is Guid previous)
            _coordinator.CancelPlan(previous);

        var plan = _coordinator.PreparePlan(_boardSession.Snapshot, proposedCommands);
        _activePlanId = plan.Id;
        Scene.SetPlan(plan);
        return plan;
    }

    public async Task FlushAsync()
    {
        ThrowIfDisposed();
        Task pending;
        lock (_queueLock)
            pending = _queuedActions;
        await pending.ConfigureAwait(false);
    }

    public Task<HavenBoardSnapshot> UndoLastAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (LastAppliedPlanId is not Guid planId)
            throw new InvalidOperationException("There is no generated board plan available to undo.");
        return UndoCoreAsync(planId, cancellationToken);
    }

    private async Task<HavenBoardSnapshot> UndoCoreAsync(Guid planId, CancellationToken cancellationToken)
    {
        var restored = await _coordinator.UndoAsync(_boardSession, planId, cancellationToken).ConfigureAwait(false);
        LastAppliedPlanId = null;
        Scene.SetStatus("Generated changes undone locally.");
        return restored;
    }

    private void OnApplyRequested(object? sender, Guid planId) => Enqueue(() => ApplyCoreAsync(planId));

    private void OnCancelRequested(object? sender, Guid planId) => Enqueue(() => CancelCoreAsync(planId));

    private void Enqueue(Func<Task> action)
    {
        lock (_queueLock)
        {
            var preceding = _queuedActions;
            _queuedActions = RunQueuedAsync(preceding, action);
        }
    }

    private static async Task RunQueuedAsync(Task preceding, Func<Task> action)
    {
        try
        {
            await preceding.ConfigureAwait(false);
        }
        catch
        {
            // Preserve user-action ordering after a previously surfaced review failure.
        }
        await action().ConfigureAwait(false);
    }

    private async Task ApplyCoreAsync(Guid planId)
    {
        if (_activePlanId != planId)
            throw new InvalidOperationException("The requested generated board plan is no longer active.");

        try
        {
            var applied = await _coordinator.ApplyAsync(_boardSession, planId).ConfigureAwait(false);
            LastAppliedPlanId = applied.PlanId;
            _activePlanId = null;
            Scene.ClearPlan("Generated changes applied locally.");
        }
        catch
        {
            Scene.SetStatus("Generated changes could not be applied; refresh the preview.");
            throw;
        }
    }

    private Task CancelCoreAsync(Guid planId)
    {
        if (_activePlanId != planId)
            throw new InvalidOperationException("The requested generated board plan is no longer active.");

        _coordinator.CancelPlan(planId);
        _activePlanId = null;
        Scene.ClearPlan("Generated changes cancelled.");
        return Task.CompletedTask;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        Task pending;
        lock (_queueLock)
            pending = _queuedActions;

        try
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch
            {
                // The failure is already represented in the review scene status.
            }

            if (_activePlanId is Guid active)
                _coordinator.CancelPlan(active);
        }
        finally
        {
            _disposed = true;
            Scene.ApplyRequested -= OnApplyRequested;
            Scene.CancelRequested -= OnCancelRequested;
            Scene.Dispose();
        }
    }
}
