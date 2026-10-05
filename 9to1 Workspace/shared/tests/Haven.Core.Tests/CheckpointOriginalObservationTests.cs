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

    [Fact]
    public async Task Actual_sequence_task_conserves_both_direct_original_faults()
    {
        var first = new IOException("original sequence IO");
        var second = new InvalidOperationException("original sequence companion");
        var original = new TaskCompletionSource<long>();
        original.SetException([first, second]);
        var repository = new Records { OriginalSequence = () => original.Task };
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var actual = producer.EnsureBeforeMutationAsync(execution, Guid.NewGuid(), null,
            "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None);

        var observed = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.True(HasOriginalCause(observed, first));
        Assert.True(HasOriginalCause(observed, second));
        Assert.True(actual.IsFaulted);
        Assert.Equal(0, repository.Saves);
        Assert.Null(await producer.GetOriginalCheckpointAsync(execution, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Failed_actual_save_conserves_both_faults_without_publishing_its_persisted_record()
    {
        var first = new IOException("original save IO");
        var second = new InvalidOperationException("original save companion");
        var original = new TaskCompletionSource();
        original.SetException([first, second]);
        var repository = new Records { OriginalSave = _ => original.Task };
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var actual = producer.EnsureBeforeMutationAsync(execution, Guid.NewGuid(), null,
            "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None);

        var observed = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.True(HasOriginalCause(observed, first));
        Assert.True(HasOriginalCause(observed, second));
        Assert.True(actual.IsFaulted);
        var saved = Assert.IsType<CheckpointInfo>(repository.LastSaved);
        Assert.Same(saved, await repository.GetAsync(saved.Id, CancellationToken.None));
        Assert.Null(await producer.GetOriginalCheckpointAsync(execution, saved.Id, CancellationToken.None));
        Assert.Equal(1, repository.Saves);
    }

    [Fact]
    public async Task Original_observation_read_conserves_both_actual_provider_faults()
    {
        var repository = new Records();
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var checkpoint = Assert.IsType<CheckpointInfo>(await producer.EnsureBeforeMutationAsync(execution,
            Guid.NewGuid(), null, "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        var first = new IOException("original read IO");
        var second = new InvalidOperationException("original read companion");
        var original = new TaskCompletionSource<CheckpointInfo?>();
        original.SetException([first, second]);
        repository.OriginalRead = _ => original.Task;
        var actual = producer.GetOriginalCheckpointAsync(execution, checkpoint.Id, CancellationToken.None);

        var observed = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.True(HasOriginalCause(observed, first));
        Assert.True(HasOriginalCause(observed, second));
        Assert.True(actual.IsFaulted);
        repository.OriginalRead = null;
        Assert.Same(checkpoint, await producer.GetOriginalCheckpointAsync(execution, checkpoint.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Caller_stop_does_not_replace_or_abandon_the_same_held_actual_save_task()
    {
        var originalSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Records { OriginalSave = _ => originalSave.Task };
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        Task<CheckpointInfo?>? actual = null;
        var failures = new List<Exception>();
        try
        {
            actual = producer.EnsureBeforeMutationAsync(execution, Guid.NewGuid(), null,
                "synthetic-workspace", CheckpointMode.BeforeFileChanges, cancellation.Token);
            var saved = Assert.IsType<CheckpointInfo>(repository.LastSaved);
            Assert.False(actual.IsCompleted);
            cancellation.Cancel();
            Assert.False(actual.IsCompleted);
            Assert.Null(await producer.GetOriginalCheckpointAsync(execution, saved.Id, CancellationToken.None));
            originalSave.SetResult();
            Assert.Same(saved, await actual);
            Assert.Same(saved, await producer.GetOriginalCheckpointAsync(execution, saved.Id, CancellationToken.None));
            Assert.True(originalSave.Task.IsCompletedSuccessfully);
            Assert.Equal(1, repository.Saves);
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            originalSave.TrySetResult();
            if (actual is not null) await JoinOriginalFixtureTaskAsync(actual, failures);
        }
        ThrowOriginalFixtureFailures(failures);
    }

    [Fact]
    public async Task A_faulted_original_save_OCE_remains_faulted_with_the_same_original_cause()
    {
        var cause = new OperationCanceledException("faulted provider cause, not a canceled task");
        var original = Task.FromException(cause);
        var repository = new Records { OriginalSave = _ => original };
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var actual = producer.EnsureBeforeMutationAsync(execution, Guid.NewGuid(), null,
            "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None);

        var observed = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.True(HasOriginalCause(observed, cause));
        Assert.True(original.IsFaulted);
        Assert.True(actual.IsFaulted);
        Assert.False(actual.IsCanceled);
        Assert.Null(await producer.GetOriginalCheckpointAsync(execution, Assert.IsType<CheckpointInfo>(repository.LastSaved).Id, CancellationToken.None));
    }

    [Fact]
    public async Task An_actually_canceled_original_save_retains_its_real_cancellation_token()
    {
        using var providerCancellation = new CancellationTokenSource();
        providerCancellation.Cancel();
        var original = Task.FromCanceled(providerCancellation.Token);
        var repository = new Records { OriginalSave = _ => original };
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var actual = producer.EnsureBeforeMutationAsync(execution, Guid.NewGuid(), null,
            "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None);

        var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
        Assert.Equal(providerCancellation.Token, observed.CancellationToken);
        Assert.True(original.IsCanceled);
        Assert.True(actual.IsCanceled);
        Assert.False(actual.IsFaulted);
        Assert.Null(await producer.GetOriginalCheckpointAsync(execution, Assert.IsType<CheckpointInfo>(repository.LastSaved).Id, CancellationToken.None));
    }

    [Fact]
    public async Task An_opaque_empty_original_fault_aggregate_is_retained_as_a_cause()
    {
        var cause = new AggregateException("opaque original provider failure");
        var repository = new Records { OriginalSave = _ => Task.FromException(cause) };
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var actual = producer.EnsureBeforeMutationAsync(execution, Guid.NewGuid(), null,
            "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None);

        var observed = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.NotEmpty(observed.InnerExceptions);
        Assert.True(HasOriginalCause(observed, cause));
        Assert.True(actual.IsFaulted);
        Assert.Null(await producer.GetOriginalCheckpointAsync(execution, Assert.IsType<CheckpointInfo>(repository.LastSaved).Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_synchronous_unknown_provider_OCE_cannot_masquerade_as_a_canceled_original_task()
    {
        var cause = new OperationCanceledException("provider threw before returning an original");
        var repository = new Records { OriginalSave = _ => throw cause };
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var actual = producer.EnsureBeforeMutationAsync(Guid.NewGuid(), Guid.NewGuid(), null,
            "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None);

        var observed = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.True(HasOriginalCause(observed, cause));
        Assert.True(actual.IsFaulted);
        Assert.False(actual.IsCanceled);
    }

    [Fact]
    public async Task Same_execution_failed_save_cannot_automatically_replay_from_its_null_map()
    {
        var originalFailure = new IOException("original provider persisted then faulted");
        var repository = new Records { OriginalSave = _ => Task.FromException(originalFailure) };
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var context = Guid.NewGuid();
        var original = producer.EnsureBeforeMutationAsync(execution, context, null,
            "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None);
        Assert.Same(originalFailure, await Assert.ThrowsAsync<IOException>(() => original));
        var saved = Assert.IsType<CheckpointInfo>(repository.LastSaved);
        Assert.Null(await producer.GetOriginalCheckpointAsync(execution, saved.Id, CancellationToken.None));

        repository.OriginalSave = _ => Task.CompletedTask; // Provider availability alone cannot reconcile the original.
        var replay = producer.EnsureBeforeMutationAsync(execution, context, null,
            "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None);
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => replay);
        Assert.Same(originalFailure, refusal.InnerException);
        Assert.Equal(1, repository.Saves);
        Assert.Same(saved, await repository.GetAsync(saved.Id, CancellationToken.None));
        Assert.Null(await producer.GetOriginalCheckpointAsync(execution, saved.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_same_scope_ensure_refuses_instead_of_launching_a_second_actual_save()
    {
        var heldSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Records { OriginalSave = _ => heldSave.Task };
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var context = Guid.NewGuid();
        Task<CheckpointInfo?>? original = null;
        var failures = new List<Exception>();
        try
        {
            original = producer.EnsureBeforeMutationAsync(execution, context, null,
                "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None);
            var saved = Assert.IsType<CheckpointInfo>(repository.LastSaved);
            Assert.False(original.IsCompleted);
            var concurrent = producer.EnsureBeforeMutationAsync(execution, context, null,
                "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => concurrent);
            Assert.False(original.IsCompleted);
            Assert.Equal(1, repository.Saves);
            heldSave.SetResult();
            Assert.Same(saved, await original);
            Assert.Same(saved, await producer.EnsureBeforeMutationAsync(execution, context, null,
                "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
            Assert.Equal(1, repository.Saves);
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            heldSave.TrySetResult();
            if (original is not null) await JoinOriginalFixtureTaskAsync(original, failures);
        }
        ThrowOriginalFixtureFailures(failures);
    }

    [Fact]
    public async Task Already_acknowledged_actual_read_conserves_both_provider_faults_without_another_save()
    {
        var repository = new Records();
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var checkpoint = Assert.IsType<CheckpointInfo>(await producer.EnsureBeforeMutationAsync(execution,
            Guid.NewGuid(), null, "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        var first = new IOException("original acknowledged read IO");
        var second = new InvalidOperationException("original acknowledged read companion");
        var original = new TaskCompletionSource<CheckpointInfo?>();
        original.SetException([first, second]);
        repository.OriginalRead = _ => original.Task;

        var actual = producer.EnsureBeforeMutationAsync(execution, Guid.NewGuid(), null,
            "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None);
        var observed = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.True(HasOriginalCause(observed, first));
        Assert.True(HasOriginalCause(observed, second));
        Assert.True(actual.IsFaulted);
        Assert.Equal(1, repository.Saves);
        repository.OriginalRead = null;
        Assert.Same(checkpoint, await producer.EnsureBeforeMutationAsync(execution, Guid.NewGuid(), null,
            "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        Assert.Equal(1, repository.Saves);
    }

    [Fact]
    public async Task Rewritten_same_id_acknowledgement_is_refused_without_a_second_original_save()
    {
        var repository = new Records();
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var checkpoint = Assert.IsType<CheckpointInfo>(await producer.EnsureBeforeMutationAsync(execution,
            Guid.NewGuid(), null, "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        repository.ReturnInstead = checkpoint with
        {
            Label = "Rewritten acknowledged checkpoint", StartSequence = checkpoint.StartSequence + 1
        };

        Assert.Equal(checkpoint.Id, repository.ReturnInstead.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => producer.EnsureBeforeMutationAsync(execution,
            Guid.NewGuid(), null, "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        Assert.Equal(1, repository.Saves);
        Assert.Null(await producer.GetOriginalCheckpointAsync(execution, checkpoint.Id, CancellationToken.None));
        repository.ReturnInstead = null;
        Assert.Same(checkpoint, await producer.EnsureBeforeMutationAsync(execution, Guid.NewGuid(), null,
            "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        Assert.Equal(1, repository.Saves);
    }

    [Fact]
    public async Task Missing_acknowledged_row_does_not_authorise_a_second_original_save()
    {
        var repository = new Records();
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var checkpoint = Assert.IsType<CheckpointInfo>(await producer.EnsureBeforeMutationAsync(execution,
            Guid.NewGuid(), null, "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        repository.OriginalRead = _ => Task.FromResult<CheckpointInfo?>(null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => producer.EnsureBeforeMutationAsync(execution,
            Guid.NewGuid(), null, "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        Assert.Equal(1, repository.Saves);
        Assert.Null(await producer.GetOriginalCheckpointAsync(execution, checkpoint.Id, CancellationToken.None));
        repository.OriginalRead = null;
        Assert.Same(checkpoint, await producer.EnsureBeforeMutationAsync(execution, Guid.NewGuid(), null,
            "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        Assert.Equal(1, repository.Saves);
    }

    [Fact]
    public async Task Caller_stop_after_actual_acknowledged_read_cannot_publish_or_resave_the_checkpoint()
    {
        var repository = new Records();
        var producer = new CheckpointService(repository, new UnusedRestorer());
        var execution = Guid.NewGuid();
        var checkpoint = Assert.IsType<CheckpointInfo>(await producer.EnsureBeforeMutationAsync(execution,
            Guid.NewGuid(), null, "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        using var stopped = new CancellationTokenSource();
        repository.AfterRead = stopped.Cancel;

        var actual = producer.EnsureBeforeMutationAsync(execution, Guid.NewGuid(), null,
            "synthetic-workspace", CheckpointMode.BeforeFileChanges, stopped.Token);
        var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
        Assert.Equal(stopped.Token, observed.CancellationToken);
        Assert.True(actual.IsCanceled);
        Assert.Equal(1, repository.Saves);
        repository.AfterRead = null;
        Assert.Same(checkpoint, await producer.EnsureBeforeMutationAsync(execution, Guid.NewGuid(), null,
            "synthetic-workspace", CheckpointMode.BeforeFileChanges, CancellationToken.None));
        Assert.Equal(1, repository.Saves);
    }

    private static bool HasOriginalCause(Exception observed, Exception exact) => ReferenceEquals(observed, exact) ||
        observed is AggregateException compound && compound.InnerExceptions.Any(item => HasOriginalCause(item, exact));
    private static async Task JoinOriginalFixtureTaskAsync(Task original, List<Exception> failures)
    {
        try { await original; }
        catch (Exception error)
        {
            if (original.Exception is { InnerExceptions.Count: > 0 } compound) failures.AddRange(compound.InnerExceptions);
            else failures.Add(error);
        }
    }
    private static void ThrowOriginalFixtureFailures(List<Exception> failures)
    {
        var exact = failures.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (exact.Length == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exact[0]).Throw();
        if (exact.Length != 0) throw new AggregateException("Original checkpoint fixture assertion/cleanup failed.", exact);
    }

    private sealed class Records : ICheckpointRepository
    {
        private readonly Dictionary<Guid, CheckpointInfo> _records = [];
        internal CheckpointInfo? ReturnInstead;
        internal Action? AfterRead;
        internal int Saves;
        internal int Reads;
        internal CheckpointInfo? LastSaved;
        internal Func<CheckpointInfo, Task>? OriginalSave;
        internal Func<Task<long>>? OriginalSequence;
        internal Func<CheckpointInfo?, Task<CheckpointInfo?>>? OriginalRead;
        public Task SaveAsync(CheckpointInfo checkpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _records.Add(checkpoint.Id, checkpoint); Saves++;
            LastSaved = checkpoint;
            return OriginalSave?.Invoke(checkpoint) ?? Task.CompletedTask;
        }
        public Task<CheckpointInfo?> GetAsync(Guid checkpointId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Reads++;
            var record = ReturnInstead ?? _records.GetValueOrDefault(checkpointId);
            AfterRead?.Invoke();
            return OriginalRead?.Invoke(record) ?? Task.FromResult(record);
        }
        public Task<CheckpointInfo?> GetLatestAsync(Guid? conversationId, string workspaceRoot, CancellationToken cancellationToken) =>
            Task.FromResult(_records.Values.LastOrDefault());
        public Task<long> GetLatestVersionSequenceAsync(string workspaceRoot, CancellationToken cancellationToken) => OriginalSequence?.Invoke() ?? Task.FromResult(1L);
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
