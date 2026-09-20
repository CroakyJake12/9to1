using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.Hui;

/// <summary>
/// Thin application-session boundary for Haven Boards.
///
/// It owns no network behavior: opening, mutation, and reopen all flow through the supplied
/// <see cref="IHavenBoardStore"/>. HUI commands are serialized, persisted before becoming the
/// current snapshot, and then projected into both the structured and freeform HUI scenes.
/// </summary>
public sealed class HavenBoardsHuiSession : IAsyncDisposable
{
    private readonly IHavenBoardStore _store;
    private readonly string _boardId;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly object _queueLock = new();
    private Task _queuedSceneCommands = Task.CompletedTask;
    private bool _disposed;

    private HavenBoardsHuiSession(
        IHavenBoardStore store,
        string boardId,
        HavenBoardSnapshot snapshot,
        bool created)
    {
        _store = store;
        _boardId = boardId;
        Snapshot = snapshot;

        Scene = new HavenBoardsHuiScene();
        FreeformScene = new HavenBoardsFreeformHuiScene();
        Scene.SetSnapshot(snapshot);
        FreeformScene.SetSnapshot(snapshot);

        var status = created ? "Created locally" : "Loaded locally";
        SetSceneStatus(status);

        Scene.CommandRequested += OnSceneCommandRequested;
        FreeformScene.CommandRequested += OnSceneCommandRequested;
    }

    public HavenBoardsHuiScene Scene { get; }
    public HavenBoardsFreeformHuiScene FreeformScene { get; }
    public HavenBoardSnapshot Snapshot { get; private set; }

    public static async Task<HavenBoardsHuiSession> OpenAsync(
        IHavenBoardStore store,
        string boardId = "board-main",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrWhiteSpace(boardId))
            throw new ArgumentException("A board ID is required.", nameof(boardId));

        var snapshot = await store.LoadAsync(boardId, cancellationToken).ConfigureAwait(false);
        var created = snapshot is null;
        if (snapshot is null)
        {
            snapshot = HavenBoardSnapshot.CreateDefault() with { Id = boardId };
            HavenBoardReducer.Validate(snapshot);
            await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Persisted snapshots are untrusted input at the application boundary. Reject malformed
            // hierarchy and freeform geometry before any of it reaches HUI.
            HavenBoardReducer.Validate(snapshot);
        }

        return new HavenBoardsHuiSession(store, boardId, snapshot, created);
    }

    public Task<HavenBoardSnapshot> ExecuteAsync(
        HavenBoardCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteCoreAsync([command], expectedVersion: null, cancellationToken);
    }

    /// <summary>
    /// Applies a command batch as one durable transaction. Every command is reduced in memory first;
    /// no storage or visible HUI state changes unless the complete batch is valid and the expected
    /// version still matches the open board.
    /// </summary>
    public Task<HavenBoardSnapshot> ExecuteBatchAsync(
        IReadOnlyList<HavenBoardCommand> commands,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count == 0)
            throw new ArgumentException("A board transaction must contain at least one command.", nameof(commands));

        return ExecuteCoreAsync(commands, expectedVersion, cancellationToken);
    }

    /// <summary>
    /// Restores a previously validated checkpoint without rewinding the durable version counter.
    /// This is the explicit undo primitive for reviewed generative UI plans.
    /// </summary>
    public async Task<HavenBoardSnapshot> RestoreSnapshotAsync(
        HavenBoardSnapshot checkpoint,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(checkpoint);
        HavenBoardReducer.Validate(checkpoint);
        if (!string.Equals(checkpoint.Id, _boardId, StringComparison.Ordinal))
            throw new InvalidOperationException("Cannot restore a checkpoint for a different board.");

        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireExpectedVersion(expectedVersion);
            var restored = checkpoint with { Version = checked(Snapshot.Version + 1) };
            HavenBoardReducer.Validate(restored);

            await _store.SaveAsync(restored, cancellationToken).ConfigureAwait(false);
            PublishDurableSnapshot(restored, "Restored locally");
            return restored;
        }
        catch
        {
            SetSceneStatus("Local restore failed");
            throw;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <summary>Waits until every HUI command queued before this call has completed.</summary>
    public async Task FlushAsync()
    {
        ThrowIfDisposed();
        Task pending;
        lock (_queueLock)
            pending = _queuedSceneCommands;

        await pending.ConfigureAwait(false);
    }

    private async Task<HavenBoardSnapshot> ExecuteCoreAsync(
        IReadOnlyList<HavenBoardCommand> commands,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(commands);

        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (expectedVersion is not null)
                RequireExpectedVersion(expectedVersion.Value);

            var updated = Snapshot;
            foreach (var command in commands)
            {
                ArgumentNullException.ThrowIfNull(command);
                updated = HavenBoardReducer.Apply(updated, command);
            }

            if (!string.Equals(updated.Id, _boardId, StringComparison.Ordinal))
                throw new InvalidOperationException("The reducer changed the open board identity.");

            // Persist once, after the complete batch has reduced successfully. If this fails, neither
            // visible projection advances past the last durable state.
            await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            PublishDurableSnapshot(updated, "Saved locally");
            return updated;
        }
        catch
        {
            SetSceneStatus("Local save failed");
            throw;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private void RequireExpectedVersion(long expectedVersion)
    {
        if (Snapshot.Version != expectedVersion)
        {
            throw new InvalidOperationException(
                $"Board version changed from expected {expectedVersion} to {Snapshot.Version}; re-create the plan before applying it.");
        }
    }

    private void PublishDurableSnapshot(HavenBoardSnapshot snapshot, string status)
    {
        Snapshot = snapshot;
        Scene.SetSnapshot(snapshot);
        FreeformScene.SetSnapshot(snapshot);
        SetSceneStatus(status);
    }

    private void SetSceneStatus(string status)
    {
        Scene.SetStatus(status);
        FreeformScene.SetStatus(status);
    }

    private void OnSceneCommandRequested(object? sender, HavenBoardCommand command)
    {
        lock (_queueLock)
        {
            var preceding = _queuedSceneCommands;
            _queuedSceneCommands = RunQueuedSceneCommandAsync(preceding, command);
        }
    }

    private async Task RunQueuedSceneCommandAsync(Task preceding, HavenBoardCommand command)
    {
        try
        {
            await preceding.ConfigureAwait(false);
        }
        catch
        {
            // A previous UI command already surfaced its own failure. Preserve ordering while allowing
            // a later command to retry against the last durable snapshot.
        }

        await ExecuteAsync(command).ConfigureAwait(false);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        Task pending;
        lock (_queueLock)
            pending = _queuedSceneCommands;

        try
        {
            await pending.ConfigureAwait(false);
        }
        finally
        {
            _disposed = true;
            Scene.CommandRequested -= OnSceneCommandRequested;
            FreeformScene.CommandRequested -= OnSceneCommandRequested;
            Scene.Dispose();
            FreeformScene.Dispose();
            _mutationGate.Dispose();
        }
    }
}
