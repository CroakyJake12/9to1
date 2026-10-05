using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual checkpoint producer mapping over controlled repository records; no physical database or authority grant is asserted.</summary>
public sealed class CheckpointOriginalObservationTests
{
    [Fact]
    public async Task The_actual_producer_checkpoint_is_bound_to_its_original_execution()
    {
        var repository = new Records();
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var checkpoint = Assert.IsType<CheckpointInfo>(await producer.EnsureBeforeMutationAsync(execution,
            Guid.NewGuid(), null, "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));

        Assert.Same(checkpoint, await producer.GetOriginalCheckpointAsync(execution, checkpoint.Id, CancellationToken.None));
        Assert.Null(await producer.GetOriginalCheckpointAsync(Guid.NewGuid(), checkpoint.Id, CancellationToken.None));
        Assert.Null(await producer.GetOriginalCheckpointAsync(execution, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(1, repository.Saves);
    }

    [Fact]
    public async Task An_existing_same_conversation_record_does_not_invent_producer_execution_provenance()
    {
        var repository = new Records();
        var conversation = Guid.NewGuid();
        var unrelated = new CheckpointInfo(Guid.NewGuid(), conversation, null, "synthetic-workspace",
            "Unrelated persisted checkpoint", CheckpointMode.BeforeFileChanges, 1, DateTimeOffset.UtcNow);
        await repository.SaveAsync(unrelated, CancellationToken.None);
        var producer = new CheckpointService(repository, new UnusedRestorer());

        Assert.Null(await producer.GetOriginalCheckpointAsync(Guid.NewGuid(), unrelated.Id, CancellationToken.None));
        Assert.Equal(0, repository.Reads);
    }

    [Fact]
    public async Task A_repository_record_with_a_different_identity_cannot_replace_the_actual_mapped_checkpoint()
    {
        var repository = new Records();
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var checkpoint = Assert.IsType<CheckpointInfo>(await producer.EnsureBeforeMutationAsync(execution,
            Guid.NewGuid(), null, "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        repository.ReturnInstead = checkpoint with { Id = Guid.NewGuid() };

        Assert.Null(await producer.GetOriginalCheckpointAsync(execution, checkpoint.Id, CancellationToken.None));
        Assert.Equal(1, repository.Reads);
    }

    [Fact]
    public async Task Cancellation_during_the_exact_repository_read_cannot_publish_an_observation()
    {
        var repository = new Records();
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var checkpoint = Assert.IsType<CheckpointInfo>(await producer.EnsureBeforeMutationAsync(execution,
            Guid.NewGuid(), null, "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        repository.AfterRead = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            producer.GetOriginalCheckpointAsync(execution, checkpoint.Id, cancellation.Token));
        Assert.Equal(1, repository.Reads);
        repository.AfterRead = null;
        Assert.Same(checkpoint, await producer.GetOriginalCheckpointAsync(execution, checkpoint.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Same_id_metadata_rewrite_cannot_inherit_the_original_checkpoint_provenance()
    {
        var repository = new Records();
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var checkpoint = Assert.IsType<CheckpointInfo>(await producer.EnsureBeforeMutationAsync(execution,
            Guid.NewGuid(), null, "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        repository.ReturnInstead = checkpoint with { Label = "Rewritten under the same ID", WorkspaceRoot = "other-workspace" };

        Assert.Equal(checkpoint.Id, repository.ReturnInstead.Id);
        Assert.Null(await producer.GetOriginalCheckpointAsync(execution, checkpoint.Id, CancellationToken.None));
        repository.ReturnInstead = null;
        Assert.Same(checkpoint, await producer.GetOriginalCheckpointAsync(execution, checkpoint.Id, CancellationToken.None));
        Assert.Equal(1, repository.Saves);
    }

    [Fact]
    public async Task Same_id_history_revision_rewrite_cannot_replace_the_actual_created_checkpoint()
    {
        var repository = new Records();
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var checkpoint = Assert.IsType<CheckpointInfo>(await producer.EnsureBeforeMutationAsync(execution,
            Guid.NewGuid(), null, "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        repository.ReturnInstead = checkpoint with { StartSequence = checkpoint.StartSequence + 1 };

        Assert.Equal(checkpoint.Id, repository.ReturnInstead.Id);
        Assert.Null(await producer.GetOriginalCheckpointAsync(execution, checkpoint.Id, CancellationToken.None));
        repository.ReturnInstead = null;
        Assert.Same(checkpoint, await producer.GetOriginalCheckpointAsync(execution, checkpoint.Id, CancellationToken.None));
        Assert.Equal(1, repository.Saves);
    }

    private sealed class Records : ICheckpointRepository
    {
        private readonly Dictionary<Guid, CheckpointInfo> _records = [];
        internal CheckpointInfo? ReturnInstead;
        internal Action? AfterRead;
        internal int Saves;
        internal int Reads;
        public Task SaveAsync(CheckpointInfo checkpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _records.Add(checkpoint.Id, checkpoint); Saves++;
            return Task.CompletedTask;
        }
        public Task<CheckpointInfo?> GetAsync(Guid checkpointId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Reads++;
            var record = ReturnInstead ?? _records.GetValueOrDefault(checkpointId);
            AfterRead?.Invoke();
            return Task.FromResult(record);
        }
        public Task<CheckpointInfo?> GetLatestAsync(Guid? conversationId, string workspaceRoot, CancellationToken cancellationToken) =>
            Task.FromResult(_records.Values.LastOrDefault());
        public Task<long> GetLatestVersionSequenceAsync(string workspaceRoot, CancellationToken cancellationToken) => Task.FromResult(1L);
        public Task<IReadOnlyList<WorkspaceRestoreEntry>> GetVersionsSinceAsync(string workspaceRoot, long sequence, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkspaceRestoreEntry>>([]);
        public Task<WorkspaceRestoreEntry?> GetLatestVersionAsync(string workspaceRoot, CancellationToken cancellationToken) => Task.FromResult<WorkspaceRestoreEntry?>(null);
    }

    private sealed class UnusedRestorer : ICheckpointRestorer
    {
        public Task<IReadOnlyList<string>> RestoreAsync(string workspaceRoot, CheckpointRestorePlan plan, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An observation must not mutate or restore files.");
    }
}
