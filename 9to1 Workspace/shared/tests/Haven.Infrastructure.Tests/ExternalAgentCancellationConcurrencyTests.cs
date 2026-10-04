using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Real canonical SQLite and task services. The factory controls only admission of the cancel write.
public sealed class ExternalAgentCancellationConcurrencyTests
{
    [Theory]
    [InlineData(HavenTaskStatus.InProgress)]
    [InlineData(HavenTaskStatus.Failed)]
    public async Task Intervening_progress_or_failure_survives_stale_cancellation_and_restart(HavenTaskStatus status)
    {
        var fixture = await Fixture.CreateAsync();
        ExternalAgentTask? committed = null;
        string? committedPayload = null;
        var cancelled = await RunBlockedCancelAsync(fixture, async () =>
        {
            await fixture.Service.UpdateAsync(fixture.Task.Id, fixture.Claim.LeaseToken, status,
                "new-progress", "new-result", "new-diagnostic", "new-update-1", CancellationToken.None);
            committed = await fixture.Repository.GetByIdAsync(fixture.Task.Id, CancellationToken.None);
            committedPayload = await ReadPayloadAsync(fixture.Database, fixture.Task.Id);
        });

        Assert.False(cancelled);
        Assert.NotNull(committed);
        Assert.Equal(status, committed.Status);
        Assert.Equal("new-update-1", committed.IdempotencyKey);
        await AssertUnchangedAfterRestartAsync(fixture, committed, committedPayload!);
    }

    [Fact]
    public async Task Renewed_claim_survives_stale_cancellation_and_restart()
    {
        var fixture = await Fixture.CreateAsync();
        ExternalTaskClaim? renewed = null;
        string? committedPayload = null;
        var cancelled = await RunBlockedCancelAsync(fixture, async () =>
        {
            renewed = await fixture.Service.ClaimAsync(fixture.Task.Locator, fixture.Principal, "owner-agent", CancellationToken.None);
            committedPayload = await ReadPayloadAsync(fixture.Database, fixture.Task.Id);
        });

        Assert.False(cancelled);
        Assert.NotNull(renewed);
        Assert.False(renewed.Task.LeaseTokenHash == fixture.Claim.Task.LeaseTokenHash, "The real claim must acquire a new lease.");
        await AssertUnchangedAfterRestartAsync(fixture, renewed.Task, committedPayload!);
        await fixture.Service.UpdateAsync(fixture.Task.Id, renewed.LeaseToken, HavenTaskStatus.Completed,
            "done", "renewed-owner-result", null, "renewed-completion-1", CancellationToken.None);
        var reopened = await fixture.ReopenedRepository.GetByIdAsync(fixture.Task.Id, CancellationToken.None);
        Assert.Equal(HavenTaskStatus.Completed, reopened!.Status);
        Assert.Equal("renewed-owner-result", reopened.SafeResult);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UpdateAsync(fixture.Task.Id,
            fixture.Claim.LeaseToken, HavenTaskStatus.Completed, "old", "old-result", null, "old-completion", CancellationToken.None));
    }

    [Fact]
    public async Task A_deleted_task_is_not_recreated_by_stale_cancellation()
    {
        var fixture = await Fixture.CreateAsync();
        var cancelled = await RunBlockedCancelAsync(fixture, async () =>
        {
            await using var connection = await fixture.Database.OpenAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM external_agent_tasks WHERE id=$id;";
            command.Parameters.AddWithValue("$id", fixture.Task.Id.ToString());
            Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken.None));
        });

        Assert.False(cancelled);
        Assert.Null(await fixture.ReopenedRepository.GetByIdAsync(fixture.Task.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Competing_cancellations_commit_once_and_preserve_the_first_commit_after_restart()
    {
        var fixture = await Fixture.CreateAsync();
        var firstGate = new CancelWriteGate(fixture.Database);
        var secondGate = new CancelWriteGate(fixture.Database);
        var firstNow = DateTimeOffset.UtcNow;
        var first = new ExternalAgentTaskRepository(firstGate).TryCancelAsync(fixture.Task.Id, fixture.Principal.UserId, firstNow, CancellationToken.None);
        var second = new ExternalAgentTaskRepository(secondGate).TryCancelAsync(fixture.Task.Id, fixture.Principal.UserId, firstNow.AddSeconds(1), CancellationToken.None);
        Exception? primary = null;
        List<Exception> drains = [];
        ExternalAgentTask? committed = null;
        string? committedPayload = null;
        try
        {
            await Task.WhenAll(firstGate.BeforeWrite.Task, secondGate.BeforeWrite.Task).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, firstGate.Opens);
            Assert.Equal(2, secondGate.Opens);
            firstGate.Release.TrySetResult();
            Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(10)));
            committed = await fixture.Repository.GetByIdAsync(fixture.Task.Id, CancellationToken.None);
            committedPayload = await ReadPayloadAsync(fixture.Database, fixture.Task.Id);
            secondGate.Release.TrySetResult();
            Assert.False(await second.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        catch (Exception error) { primary = error; }
        finally
        {
            firstGate.Release.TrySetResult();
            secondGate.Release.TrySetResult();
            try { await first; } catch (Exception error) { drains.Add(error); }
            try { await second; } catch (Exception error) { drains.Add(error); }
        }
        ThrowRetainedErrors(primary, drains);
        Assert.NotNull(committed);
        Assert.Equal(HavenTaskStatus.Cancelled, committed.Status);
        Assert.Equal(firstNow, committed.UpdatedAt);
        await AssertUnchangedAfterRestartAsync(fixture, committed, committedPayload!);
    }

    [Fact]
    public async Task Caller_cancellation_before_write_leaves_the_durable_task_unchanged()
    {
        var fixture = await Fixture.CreateAsync();
        var before = await fixture.Repository.GetByIdAsync(fixture.Task.Id, CancellationToken.None);
        var beforePayload = await ReadPayloadAsync(fixture.Database, fixture.Task.Id);
        var gate = new CancelWriteGate(fixture.Database);
        using var cancellation = new CancellationTokenSource();
        var cancelling = new ExternalAgentTaskRepository(gate).TryCancelAsync(fixture.Task.Id, fixture.Principal.UserId, DateTimeOffset.UtcNow, cancellation.Token);
        Exception? primary = null;
        List<Exception> drains = [];
        try
        {
            await gate.BeforeWrite.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, gate.Opens);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelling);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            gate.Release.TrySetResult();
            try { await cancelling; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error) { drains.Add(error); }
        }
        ThrowRetainedErrors(primary, drains);
        await AssertUnchangedAfterRestartAsync(fixture, before!, beforePayload);
    }

    [Fact]
    public async Task Unchanged_valid_JSON_whitespace_does_not_prevent_cancellation()
    {
        var fixture = await Fixture.CreateAsync();
        var canonicalPayload = await ReadPayloadAsync(fixture.Database, fixture.Task.Id);
        await using (var connection = await fixture.Database.OpenAsync(CancellationToken.None))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE external_agent_tasks SET payload_json=$payload WHERE id=$id;";
            command.Parameters.AddWithValue("$payload", " \n" + canonicalPayload + "\n ");
            command.Parameters.AddWithValue("$id", fixture.Task.Id.ToString());
            Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken.None));
        }
        var before = await fixture.Repository.GetByIdAsync(fixture.Task.Id, CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        Assert.True(await fixture.Repository.TryCancelAsync(fixture.Task.Id, fixture.Principal.UserId, now, CancellationToken.None));
        var reopened = await fixture.ReopenedRepository.GetByIdAsync(fixture.Task.Id, CancellationToken.None);
        Assert.True((before! with { Status = HavenTaskStatus.Cancelled, UpdatedAt = now }) == reopened,
            "Cancellation of the unchanged original JSON must retain every other canonical task field.");
    }

    private static async Task<bool> RunBlockedCancelAsync(Fixture fixture, Func<Task> commitWhileBlocked)
    {
        var gate = new CancelWriteGate(fixture.Database);
        var cancelling = new ExternalAgentTaskRepository(gate).TryCancelAsync(fixture.Task.Id, fixture.Principal.UserId, DateTimeOffset.UtcNow, CancellationToken.None);
        Exception? primary = null;
        List<Exception> drains = [];
        var cancelled = false;
        try
        {
            await gate.BeforeWrite.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, gate.Opens);
            await commitWhileBlocked();
            gate.Release.TrySetResult();
            cancelled = await cancelling.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception error) { primary = error; }
        finally
        {
            gate.Release.TrySetResult();
            try { await cancelling; } catch (Exception error) { drains.Add(error); }
        }
        ThrowRetainedErrors(primary, drains);
        return cancelled;
    }

    private static void ThrowRetainedErrors(Exception? primary, List<Exception> drains)
    {
        if (primary is not null) drains.Insert(0, primary);
        if (drains.Count > 0) throw new AggregateException("Original test and independent original task drains retained", drains);
    }

    private static async Task AssertUnchangedAfterRestartAsync(Fixture fixture, ExternalAgentTask expected, string expectedPayload)
    {
        var database = new SqliteDatabase(fixture.Paths);
        var reopened = await new ExternalAgentTaskRepository(database).GetByIdAsync(fixture.Task.Id, CancellationToken.None);
        Assert.True(expected == reopened, "The latest committed canonical record must survive reopening unchanged.");
        Assert.True(expectedPayload == await ReadPayloadAsync(database, fixture.Task.Id), "The latest committed original payload must survive reopening byte for byte.");
    }

    private static async Task<string> ReadPayloadAsync(SqliteDatabase database, Guid taskId)
    {
        await using var connection = await database.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM external_agent_tasks WHERE id=$id;";
        command.Parameters.AddWithValue("$id", taskId.ToString());
        return (string)(await command.ExecuteScalarAsync(CancellationToken.None))!;
    }

    private sealed record Fixture(Paths Paths, SqliteDatabase Database, ExternalAgentTaskRepository Repository,
        ExternalAgentTaskService Service, ExternalAgentTask Task, ExternalAgentPrincipal Principal, ExternalTaskClaim Claim)
    {
        public ExternalAgentTaskRepository ReopenedRepository => new(new SqliteDatabase(Paths));
        public static async Task<Fixture> CreateAsync()
        {
            var paths = new Paths();
            var database = new SqliteDatabase(paths);
            await database.InitializeAsync(CancellationToken.None);
            var repository = new ExternalAgentTaskRepository(database);
            var service = new ExternalAgentTaskService(repository, new Sink());
            var workspace = Guid.NewGuid();
            var project = Guid.NewGuid();
            var principal = new ExternalAgentPrincipal(Guid.NewGuid(), new HashSet<Guid> { workspace }, new HashSet<Guid> { project }, "local-test-client");
            var task = await service.CreateAsync(principal, "local-test-agent", "Durable race control", "No provider execution", "{}", "result",
                workspace, project, null, null, null, CancellationToken.None);
            var claim = await service.ClaimAsync(task.Locator, principal, "owner-agent", CancellationToken.None);
            return new Fixture(paths, database, repository, service, task, principal, claim);
        }
    }

    private sealed class Sink : IExecutionEventSink { public bool TryPublish(ExecutionEvent value) => true; }
    private sealed class CancelWriteGate(ISqliteConnectionFactory actual) : ISqliteConnectionFactory
    {
        private int opens;
        public int Opens => Volatile.Read(ref opens);
        public TaskCompletionSource BeforeWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SqliteConnection> OpenAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref opens) == 2)
            {
                BeforeWrite.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            return await actual.OpenAsync(token);
        }
    }

    private sealed class Paths : IAppPaths
    {
        public Paths()
        {
            DataDirectory = Path.Combine(Environment.GetEnvironmentVariable("C5_DURABLE_JOB_EVIDENCE") ?? Path.GetTempPath(), "actual-cancel-race-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "tasks.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
        // Synthetic SQLite artifacts are retained outside Git for independent inspection.
    }
}
