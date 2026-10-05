/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Safety/CheckpointService.cs, in the Application layer.
 * What: Owns ICheckpointRepository, ICheckpointRestorer and CheckpointService — one shared
 *       checkpoint system for agentic file modifications that does NOT depend on Git
 *       (state lives in the SQLite workspace-version history).
 * How: A checkpoint records the workspace version-history sequence at creation time. Restoring
 *      replays the recorded BeforeContent of every later mutation per path (latest wins), which is
 *      exact for any directory including non-Git workspaces.
 * Why: Recovery must be inspectable (Action Graph), policy-driven and honest about reversibility.
 * Maintenance: Keep restore plans pure so Infrastructure only performs confined file writes.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>Persistence for checkpoints plus sequence-addressed access to recorded mutations.</summary>
public interface ICheckpointRepository
{
    Task SaveAsync(CheckpointInfo checkpoint, CancellationToken cancellationToken);
    Task<CheckpointInfo?> GetLatestAsync(Guid? conversationId, string workspaceRoot, CancellationToken cancellationToken);
    Task<CheckpointInfo?> GetAsync(Guid checkpointId, CancellationToken cancellationToken);
    Task<long> GetLatestVersionSequenceAsync(string workspaceRoot, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkspaceRestoreEntry>> GetVersionsSinceAsync(string workspaceRoot, long sequence, CancellationToken cancellationToken);
    Task<WorkspaceRestoreEntry?> GetLatestVersionAsync(string workspaceRoot, CancellationToken cancellationToken);
}

/// <summary>Performs the confined file writes for an approved restore plan.</summary>
public interface ICheckpointRestorer
{
    /// <summary>Writes every planned path inside the workspace root; returns the paths actually restored.</summary>
    Task<IReadOnlyList<string>> RestoreAsync(string workspaceRoot, CheckpointRestorePlan plan, CancellationToken cancellationToken);
}

public sealed class CheckpointService(
    ICheckpointRepository repository,
    ICheckpointRestorer restorer,
    IExecutionEventSink? executionEvents = null) : ICheckpointExecutionObservationSource
{
    private sealed class ExecutionCheckpointScope
    {
        private readonly object _publication = new();
        private CheckpointInfo? _originalCheckpoint;
        // Retain the immutable record the actual successful producer saved. A repository
        // rewrite under the same ID cannot borrow this execution's original provenance.
        public Guid? CheckpointId
        {
            get { lock (_publication) return _originalCheckpoint?.Id; }
        }
        public CheckpointInfo? GetOriginal(Guid checkpointId)
        {
            lock (_publication)
                return _originalCheckpoint?.Id == checkpointId ? _originalCheckpoint : null;
        }
        public void PublishOriginal(CheckpointInfo checkpoint)
        {
            lock (_publication) _originalCheckpoint = checkpoint;
        }
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, ExecutionCheckpointScope> _scopesByExecution = new();

    /// <summary>The active user policy; Desktop keeps this aligned with Settings (engine-owned like permission policy).</summary>
    public CheckpointMode Mode { get; set; } = CheckpointMode.BeforeFileChanges;

    /// <summary>Observes only a checkpoint actually published by this retained execution scope.
    /// A repository ID, conversation or workspace alone establishes no execution provenance.</summary>
    public async Task<CheckpointInfo?> GetOriginalCheckpointAsync(Guid executionId, Guid checkpointId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExecutionCheckpointScope original;
        lock (_gate)
        {
            if (!_scopesByExecution.TryGetValue(executionId, out original!) || original.CheckpointId != checkpointId)
                return null;
        }
        var created = original.GetOriginal(checkpointId);
        if (created is null) return null;
        var record = await repository.GetAsync(checkpointId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (record != created) return null;
        lock (_gate)
            return _scopesByExecution.TryGetValue(executionId, out var current) &&
                ReferenceEquals(current, original) && ReferenceEquals(current.GetOriginal(checkpointId), created) ? record : null;
    }

    /// <summary>Creates at most one checkpoint per agentic execution, honouring the user's policy.</summary>
    public async Task<CheckpointInfo?> EnsureBeforeMutationAsync(
        Guid executionId,
        Guid? conversationId,
        Guid? containerId,
        string workspaceRoot,
        CheckpointMode mode,
        CancellationToken cancellationToken)
    {
        if (mode == CheckpointMode.Off) return null;
        if (string.IsNullOrWhiteSpace(workspaceRoot)) return null;
        ExecutionCheckpointScope scope;
        lock (_gate)
        {
            if (!_scopesByExecution.TryGetValue(executionId, out scope!))
            {
                scope = new ExecutionCheckpointScope();
                _scopesByExecution[executionId] = scope;
            }
        }
        if (scope.CheckpointId.HasValue)
            return await repository.GetAsync(scope.CheckpointId.Value, cancellationToken).ConfigureAwait(false);

        var startSequence = await repository.GetLatestVersionSequenceAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var checkpoint = new CheckpointInfo(
            Guid.NewGuid(), conversationId, containerId, workspaceRoot,
            "Before agentic changes", mode, startSequence, DateTimeOffset.UtcNow);
        await repository.SaveAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        scope.PublishOriginal(checkpoint);

        executionEvents?.TryPublish(new ExecutionEvent(
            Guid.NewGuid(), executionId, Guid.NewGuid(), null, ExecutionOrigin.Haven,
            ExecutionActionType.CheckpointCreated, ExecutionActionStatus.Completed,
            $"Checkpoint created: {checkpoint.Label}", null,
            "Recoverable state recorded before file changes.", "checkpoints",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            SafeMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["checkpointId"] = checkpoint.Id.ToString(),
                ["workspaceRoot"] = SensitiveTextRedactor.Redact(workspaceRoot, 300)
            }));
        return checkpoint;
    }

    /// <summary>Computes the pure restore plan for a checkpoint without touching files.</summary>
    public async Task<CheckpointRestorePlan> PlanRestoreAsync(Guid checkpointId, CancellationToken cancellationToken)
    {
        var checkpoint = await repository.GetAsync(checkpointId, cancellationToken).ConfigureAwait(false)
                         ?? throw new InvalidOperationException("Checkpoint not found.");
        var versions = await repository.GetVersionsSinceAsync(checkpoint.WorkspaceRoot, checkpoint.StartSequence, cancellationToken).ConfigureAwait(false);
        var plan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in versions.OrderBy(item => item.Sequence))
        {
            // The latest recorded before-content per path reconstructs the checkpoint-time state.
            plan[entry.RelativePath] = entry.BeforeContent;
        }
        return new CheckpointRestorePlan(checkpoint.Id, plan);
    }

    /// <summary>Plans and executes a restore; returns restored relative paths.</summary>
    public async Task<IReadOnlyList<string>> RestoreCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken)
    {
        var checkpoint = await repository.GetAsync(checkpointId, cancellationToken).ConfigureAwait(false)
                         ?? throw new InvalidOperationException("Checkpoint not found.");
        var plan = await PlanRestoreAsync(checkpointId, cancellationToken).ConfigureAwait(false);
        if (plan.IsEmpty) return [];
        var restored = await restorer.RestoreAsync(checkpoint.WorkspaceRoot, plan, cancellationToken).ConfigureAwait(false);

        executionEvents?.TryPublish(new ExecutionEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExecutionOrigin.Haven,
            ExecutionActionType.CheckpointRestored, ExecutionActionStatus.Completed,
            $"Checkpoint restored ({restored.Count} files)", null,
            "Files returned to the checkpointed state.", "checkpoints",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        return restored;
    }

    /// <summary>Undoes the single most recent recorded mutation in the workspace when reversible.</summary>
    public async Task<bool> UndoLastActionAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var latest = await repository.GetLatestVersionAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        if (latest is null) return false;
        var plan = new CheckpointRestorePlan(Guid.Empty, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [latest.RelativePath] = latest.BeforeContent
        });
        var restored = await restorer.RestoreAsync(workspaceRoot, plan, cancellationToken).ConfigureAwait(false);
        return restored.Count > 0;
    }
}
