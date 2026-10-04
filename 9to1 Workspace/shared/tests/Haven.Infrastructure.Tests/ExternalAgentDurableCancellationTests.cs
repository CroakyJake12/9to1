using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Haven.Infrastructure.Tests;

// Real production SQLite/service/repository/migrations; only connection admission timing is controlled.
public sealed class ExternalAgentDurableCancellationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Completion_committed_after_cancel_read_cannot_be_overwritten_by_stale_cancel()
    {
        var paths = new Paths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(CancellationToken.None);
        var repository = new ExternalAgentTaskRepository(database);
        var (service, task, principal, claim) = await CreateClaimedAsync(repository);
        var gate = new CancelWriteGate(database);
        var cancelling = new ExternalAgentTaskRepository(gate).TryCancelAsync(task.Id, principal.UserId, DateTimeOffset.UtcNow, CancellationToken.None);
        Exception? primary = null, drain = null;
        bool cancelled = false;
        try
        {
            await gate.BeforeWrite.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, gate.Opens); // Initial read finished; cancel has not obtained its write connection.
            await service.UpdateAsync(task.Id, claim.LeaseToken, HavenTaskStatus.Completed, "done", "retained-result", null, "completion-1", CancellationToken.None);
            var completed = await repository.GetByIdAsync(task.Id, CancellationToken.None);
            Assert.Equal(HavenTaskStatus.Completed, completed!.Status);
            Assert.Equal("retained-result", completed.SafeResult);
            gate.Release.TrySetResult();
            cancelled = await cancelling.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception error) { primary = error; }
        finally
        {
            gate.Release.TrySetResult();
            try { await cancelling; } catch (Exception error) { drain = error; }
        }
        if (primary is not null || drain is not null)
            throw new AggregateException("Original test and independent original cancel drain retained", new[] { primary, drain }.OfType<Exception>());
        var reopened = new ExternalAgentTaskRepository(new SqliteDatabase(paths));
        var restored = await reopened.GetByIdAsync(task.Id, CancellationToken.None);
        output.WriteLine($"Actual retained database={paths.DatabasePath}; cancelReturned={cancelled}; restoredStatus={restored!.Status}; result={restored.SafeResult ?? "<null>"}; idempotency={restored.IdempotencyKey ?? "<null>"}");
        Assert.Equal(HavenTaskStatus.Completed, restored.Status);
        Assert.Equal("retained-result", restored.SafeResult);
        Assert.Equal("completion-1", restored.IdempotencyKey);
        Assert.False(cancelled);
        Assert.Equal(task.OwnerUserId, restored.OwnerUserId);
        Assert.Equal(task.WorkspaceId, restored.WorkspaceId);
        Assert.Equal(task.ProjectId, restored.ProjectId);
    }

    [Fact]
    public async Task Cancelled_task_survives_restart_and_original_lease_cannot_complete_it()
    {
        var paths = new Paths(); var database = new SqliteDatabase(paths);
        await database.InitializeAsync(CancellationToken.None);
        var repository = new ExternalAgentTaskRepository(database);
        var (service, task, principal, claim) = await CreateClaimedAsync(repository);
        await service.CancelAsync(task.Id, principal, CancellationToken.None);
        var reopened = new ExternalAgentTaskRepository(new SqliteDatabase(paths));
        var resumed = new ExternalAgentTaskService(reopened, new Sink());
        await Assert.ThrowsAsync<InvalidOperationException>(() => resumed.UpdateAsync(task.Id, claim.LeaseToken, HavenTaskStatus.Completed, "late", "forbidden-result", null, "late-1", CancellationToken.None));
        var restored = await resumed.GetAuthorisedAsync(task.Locator, principal, CancellationToken.None);
        Assert.Equal(HavenTaskStatus.Cancelled, restored!.Status); Assert.Null(restored.SafeResult);
        Assert.Equal(task.Id, restored.Id); Assert.Equal(task.OwnerUserId, restored.OwnerUserId);
        Assert.Equal(task.WorkspaceId, restored.WorkspaceId); Assert.Equal(task.ProjectId, restored.ProjectId);
        output.WriteLine($"Actual cancellation/reconnect database={paths.DatabasePath}");
    }

    [Fact]
    public async Task Wrong_owner_cannot_cancel_or_reconnect_and_completed_task_refuses_cancel()
    {
        var paths = new Paths(); var database = new SqliteDatabase(paths);
        await database.InitializeAsync(CancellationToken.None);
        var repository = new ExternalAgentTaskRepository(database);
        var (service, task, principal, claim) = await CreateClaimedAsync(repository);
        var foreign = principal with { UserId = Guid.NewGuid() };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CancelAsync(task.Id, foreign, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAuthorisedAsync(task.Locator, foreign, CancellationToken.None));
        Assert.Equal(HavenTaskStatus.Claimed, (await repository.GetByIdAsync(task.Id, CancellationToken.None))!.Status);
        await service.UpdateAsync(task.Id, claim.LeaseToken, HavenTaskStatus.Completed, "done", "original-result", null, "complete-1", CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelAsync(task.Id, principal, CancellationToken.None));
        Assert.Equal("original-result", (await new ExternalAgentTaskRepository(new SqliteDatabase(paths)).GetByIdAsync(task.Id, CancellationToken.None))!.SafeResult);
    }

    private static async Task<(ExternalAgentTaskService Service, ExternalAgentTask Task, ExternalAgentPrincipal Principal, ExternalTaskClaim Claim)> CreateClaimedAsync(ExternalAgentTaskRepository repository)
    {
        var service = new ExternalAgentTaskService(repository, new Sink());
        var workspace = Guid.NewGuid(); var project = Guid.NewGuid();
        var principal = new ExternalAgentPrincipal(Guid.NewGuid(), new HashSet<Guid> { workspace }, new HashSet<Guid> { project }, "local-test-client");
        var task = await service.CreateAsync(principal, "local-test-agent", "Durable test", "No provider execution", "{}", "result", workspace, project, null, null, null, CancellationToken.None);
        var claim = await service.ClaimAsync(task.Locator, principal, "original-agent", CancellationToken.None);
        return (service, task, principal, claim);
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
            { BeforeWrite.TrySetResult(); await Release.Task.WaitAsync(token); }
            return await actual.OpenAsync(token);
        }
    }
    private sealed class Paths : IAppPaths
    {
        public Paths()
        {
            DataDirectory = Path.Combine(Environment.GetEnvironmentVariable("C5_DURABLE_JOB_EVIDENCE") ?? Path.GetTempPath(), "actual-task-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "tasks.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
        // Original SQLite artifacts intentionally retained for independent restart inspection.
    }
}
