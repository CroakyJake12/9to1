/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Safety/CheckpointService.cs, in the Application layer.
 * What: Owns ICheckpointRepository, ICheckpointRestorer and CheckpointService — one shared
 *       checkpoint system for agentic file modifications that does NOT depend on Git
 *       (state lives in the SQLite workspace-version history).
 * How: A checkpoint records the workspace version-history sequence at creation time. Restoring
 *      selects the recorded BeforeContent of the earliest later mutation per path, which is
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
    // Match the owning workspace tools: Linux paths differing only by case are distinct.
    private static readonly StringComparer WorkspacePathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed class ExecutionCheckpointScope
    {
        private readonly object _publication = new();
        private CheckpointInfo? _originalCheckpoint;
        private bool _saveAdmitted;
        private Task? _originalSave;
        private Exception? _originalSaveFailure;
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
        public void DemandNoUnacknowledgedSave()
        {
            lock (_publication)
                if (_saveAdmitted && _originalCheckpoint is null) throw RefuseSecondSave();
        }
        public void AdmitOriginalSave()
        {
            lock (_publication)
            {
                if (_saveAdmitted) throw RefuseSecondSave();
                _saveAdmitted = true;
            }
        }
        public void RetainOriginalSave(Task original)
        {
            ArgumentNullException.ThrowIfNull(original);
            lock (_publication) _originalSave = original;
        }
        public void RetainOriginalSaveFailure(Exception failure)
        {
            lock (_publication) _originalSaveFailure = failure;
        }
        private Exception RefuseSecondSave() => new InvalidOperationException(
            "The same execution's original checkpoint save is pending or unacknowledged; a second save is refused. " +
            "Recovery requires an actual owned authorised inspection, not row absence or automatic replay.",
            _originalSaveFailure ?? _originalSave?.Exception);
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
        var record = await AwaitOriginalCheckpointTaskAsync(() => repository.GetAsync(checkpointId, cancellationToken)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (record != created) return null;
        lock (_gate)
            return _scopesByExecution.TryGetValue(executionId, out var current) &&
                ReferenceEquals(current, original) && ReferenceEquals(current.GetOriginal(checkpointId), created) ? record : null;
    }

    // These are the provider's SAME original Tasks, not a cancellable wait substitute.
    // A save that physically persisted and then faulted remains unacknowledged: the
    // successful record is published only after its actual Task completed successfully.
    private static async Task<T> AwaitOriginalCheckpointTaskAsync<T>(Func<Task<T>> acquireOriginal)
    {
        Task<T>? original = null;
        try
        {
            original = acquireOriginal();
            return await original.ConfigureAwait(false);
        }
        catch (Exception observed)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(
                OriginalCheckpointTaskCause(original, observed)).Throw();
            throw;
        }
    }

    private static async Task AwaitOriginalCheckpointTaskAsync(Func<Task> acquireOriginal)
    {
        Task? original = null;
        try
        {
            original = acquireOriginal();
            await original.ConfigureAwait(false);
        }
        catch (Exception observed)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(
                OriginalCheckpointTaskCause(original, observed)).Throw();
            throw;
        }
    }

    private static Exception OriginalCheckpointTaskCause(Task? original, Exception observed)
    {
        if (original?.Exception is { InnerExceptions.Count: > 0 } fault)
        {
            if (fault.InnerExceptions.Count != 1) return fault;
            var cause = fault.InnerExceptions[0];
            // A faulted OCE must remain Faulted; an opaque empty aggregate is itself an
            // original cause, so retain it as a member of a nonempty envelope.
            return cause is OperationCanceledException or AggregateException { InnerExceptions.Count: 0 }
                ? fault : cause;
        }
        if (original is null && observed is OperationCanceledException or AggregateException { InnerExceptions.Count: 0 })
            return new AggregateException("An original checkpoint provider failed before returning its Task.", observed);
        return observed; // An actual canceled original retains its real cancellation token/status.
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
        if (scope.CheckpointId is Guid acknowledgedId)
        {
            var acknowledged = scope.GetOriginal(acknowledgedId);
            var actualRecord = await AwaitOriginalCheckpointTaskAsync(() =>
                repository.GetAsync(acknowledgedId, cancellationToken)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
                if (acknowledged is not null && actualRecord == acknowledged &&
                    _scopesByExecution.TryGetValue(executionId, out var current) && ReferenceEquals(current, scope) &&
                    ReferenceEquals(current.GetOriginal(acknowledgedId), acknowledged)) return actualRecord;
            throw new InvalidOperationException(
                "The already-published checkpoint no longer matches this execution's original acknowledgement; " +
                "a second save is refused. Recovery requires an actual owned authorised inspection.");
        }

        scope.DemandNoUnacknowledgedSave();

        var startSequence = await AwaitOriginalCheckpointTaskAsync(() => repository.GetLatestVersionSequenceAsync(workspaceRoot, cancellationToken)).ConfigureAwait(false);
        var checkpoint = new CheckpointInfo(
            Guid.NewGuid(), conversationId, containerId, workspaceRoot,
            "Before agentic changes", mode, startSequence, DateTimeOffset.UtcNow);
        // Admission is only pending/attempted metadata. It is never a save acknowledgement.
        // Concurrent calls can refuse, but cannot launch another physical Save for this scope.
        scope.AdmitOriginalSave();
        try
        {
            await AwaitOriginalCheckpointTaskAsync(() =>
            {
                var original = repository.SaveAsync(checkpoint, cancellationToken);
                scope.RetainOriginalSave(original);
                return original;
            }).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            scope.RetainOriginalSaveFailure(failure);
            throw;
        }
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
        var plan = new Dictionary<string, string>(WorkspacePathComparer);
        foreach (var entry in versions.OrderBy(item => item.Sequence))
        {
            // The first mutation after the checkpoint still records that path's checkpoint-time content.
            plan.TryAdd(entry.RelativePath, entry.BeforeContent);
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
        var plan = new CheckpointRestorePlan(Guid.Empty, new Dictionary<string, string>(WorkspacePathComparer)
        {
            [latest.RelativePath] = latest.BeforeContent
        });
        var restored = await restorer.RestoreAsync(workspaceRoot, plan, cancellationToken).ConfigureAwait(false);
        return restored.Count > 0;
    }
}
