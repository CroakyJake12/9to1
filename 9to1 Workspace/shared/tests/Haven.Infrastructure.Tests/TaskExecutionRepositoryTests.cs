using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure.Tests;

public sealed class TaskExecutionRepositoryTests : IDisposable
{
    private readonly TestPaths paths = new();

    [Fact]
    public async Task A_stale_checkpoint_preserves_the_current_accepted_plan_and_queue()
    {
        var database = await DatabaseAsync();
        var first = Snapshot();
        var owner = new TaskExecutionRepository(database);
        await owner.UpsertAsync(first, CancellationToken.None);
        var original = Assert.IsType<TaskExecutionSnapshot>(await owner.GetAsync(first.TaskId, CancellationToken.None));
        var winner = original with { PromptSummary = "Accepted checkpoint", PersistenceRevision = 2 };
        await owner.UpsertAsync(winner, CancellationToken.None);
        var committed = await PhysicalPayloadAsync(database, first.TaskId);
        var stale = original with
        {
            PromptSummary = "Stale work",
            PersistenceRevision = 2,
            Plan = [original.Plan[0] with { State = TaskPlanNodeState.Failed }],
            Queue = [],
            LastCheckpointActionId = null
        };

        var conflict = await Assert.ThrowsAsync<TaskExecutionRevisionConflictException>(() =>
            new TaskExecutionRepository(new SqliteDatabase(paths)).UpsertAsync(stale, CancellationToken.None));

        Assert.Equal(first.TaskId, conflict.TaskId);
        Assert.Equal(1, conflict.ExpectedRevision);
        Assert.Equal(2, conflict.ProposedRevision);
        Assert.Equal(committed, await PhysicalPayloadAsync(database, first.TaskId));
        var current = Assert.IsType<TaskExecutionSnapshot>(await owner.GetAsync(first.TaskId, CancellationToken.None));
        Assert.Equal(winner.ExecutionId, current.ExecutionId);
        Assert.Equal(2, current.PersistenceRevision);
        Assert.Equal(TaskPlanNodeState.Completed, Assert.Single(current.Plan).State);
        Assert.Equal(winner.LastCheckpointActionId, current.LastCheckpointActionId);
        Assert.Equal(first.Queue[0].TaskId, Assert.Single(current.Queue).TaskId);
    }

    [Fact]
    public async Task Two_independent_connections_commit_exactly_one_competing_checkpoint()
    {
        var database = await DatabaseAsync();
        var original = Snapshot();
        await new TaskExecutionRepository(database).UpsertAsync(original, CancellationToken.None);
        var left = original with { PromptSummary = "Left", PersistenceRevision = 2 };
        var right = original with { PromptSummary = "Right", PersistenceRevision = 2 };
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> SaveAsync(TaskExecutionSnapshot candidate)
        {
            await start.Task;
            try
            {
                await new TaskExecutionRepository(new SqliteDatabase(paths)).UpsertAsync(candidate, CancellationToken.None);
                return true;
            }
            catch (TaskExecutionRevisionConflictException) { return false; }
        }
        var a = SaveAsync(left);
        var b = SaveAsync(right);
        start.SetResult();
        var results = await Task.WhenAll(a, b);

        Assert.Single(results, result => result);
        Assert.Single(results, result => !result);
        var current = Assert.IsType<TaskExecutionSnapshot>(await new TaskExecutionRepository(database).GetAsync(original.TaskId, CancellationToken.None));
        Assert.Equal(results[0] ? "Left" : "Right", current.PromptSummary);
        Assert.Equal(2, current.PersistenceRevision);
        Assert.Equal(original.ExecutionId, current.ExecutionId);
        Assert.Equal(TaskPlanNodeState.Completed, Assert.Single(current.Plan).State);
        Assert.Equal(original.LastCheckpointActionId, current.LastCheckpointActionId);
    }

    [Fact]
    public async Task A_missing_task_cannot_be_recreated_by_a_stale_continuation()
    {
        var database = await DatabaseAsync();
        var candidate = Snapshot() with { PersistenceRevision = 2 };
        var repository = new TaskExecutionRepository(database);

        await Assert.ThrowsAsync<TaskExecutionRevisionConflictException>(() => repository.UpsertAsync(candidate, CancellationToken.None));
        Assert.Null(await repository.GetAsync(candidate.TaskId, CancellationToken.None));
    }

    [Fact]
    public async Task A_checkpoint_cannot_change_the_existing_execution_identity()
    {
        var database = await DatabaseAsync();
        var original = Snapshot();
        var repository = new TaskExecutionRepository(database);
        await repository.UpsertAsync(original, CancellationToken.None);
        var candidate = original with { ExecutionId = Guid.NewGuid(), PersistenceRevision = 2 };

        await Assert.ThrowsAsync<TaskExecutionRevisionConflictException>(() => repository.UpsertAsync(candidate, CancellationToken.None));
        var current = Assert.IsType<TaskExecutionSnapshot>(await repository.GetAsync(original.TaskId, CancellationToken.None));
        Assert.Equal(original.ExecutionId, current.ExecutionId);
        Assert.Equal(1, current.PersistenceRevision);
    }

    [Fact]
    public async Task Legacy_payloads_receive_zero_revision_then_commit_the_first_versioned_checkpoint()
    {
        var database = await DatabaseAsync();
        var original = Snapshot() with { PersistenceRevision = 0 };
        var legacy = JsonSerializer.SerializeToNode(original, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        Assert.True(legacy.Remove("persistenceRevision"));
        await using (var connection = await database.OpenAsync(CancellationToken.None))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO task_execution_state(task_id,context_id,execution_id,state,durability,plan_version,payload_json,created_at,updated_at)
                VALUES($task,$context,$execution,$state,$durability,$plan,$payload,$created,$updated);
                """;
            command.Parameters.AddWithValue("$task", original.TaskId.ToString());
            command.Parameters.AddWithValue("$context", original.ContextId.ToString());
            command.Parameters.AddWithValue("$execution", original.ExecutionId.ToString());
            command.Parameters.AddWithValue("$state", (int)original.State);
            command.Parameters.AddWithValue("$durability", (int)original.Durability);
            command.Parameters.AddWithValue("$plan", original.PlanVersion);
            command.Parameters.AddWithValue("$payload", legacy.ToJsonString());
            command.Parameters.AddWithValue("$created", original.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$updated", original.UpdatedAt.ToString("O"));
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        var repository = new TaskExecutionRepository(database);
        var loaded = Assert.IsType<TaskExecutionSnapshot>(await repository.GetByContextAsync(original.ContextId, CancellationToken.None));
        Assert.Equal(0, loaded.PersistenceRevision);
        await repository.UpsertAsync(loaded with { PersistenceRevision = 1 }, CancellationToken.None);

        var resumable = Assert.Single(await repository.GetResumableAsync(CancellationToken.None));
        Assert.Equal(1, resumable.PersistenceRevision);
        Assert.Equal(original.TaskId, resumable.TaskId);
        Assert.Equal(TaskPlanNodeState.Completed, Assert.Single(resumable.Plan).State);
    }

    [Fact]
    public async Task A_payload_revision_mismatch_is_refused_by_every_read_path()
    {
        var database = await DatabaseAsync();
        var original = Snapshot();
        var repository = new TaskExecutionRepository(database);
        await repository.UpsertAsync(original, CancellationToken.None);
        await using (var connection = await database.OpenAsync(CancellationToken.None))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE task_execution_state SET persistence_revision=2 WHERE task_id=$task;";
            command.Parameters.AddWithValue("$task", original.TaskId.ToString());
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.GetAsync(original.TaskId, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.GetByContextAsync(original.ContextId, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.GetResumableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_revision_and_cancelled_admission_write_no_checkpoint()
    {
        var database = await DatabaseAsync();
        var original = Snapshot();
        var repository = new TaskExecutionRepository(database);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.UpsertAsync(original with { PersistenceRevision = 0 }, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.UpsertAsync(original, cancellation.Token));
        Assert.Null(await repository.GetAsync(original.TaskId, CancellationToken.None));
    }

    [Theory]
    [InlineData("task")]
    [InlineData("context")]
    [InlineData("execution")]
    public async Task A_payload_identity_mismatch_is_refused_by_every_read_path(string changedIdentity)
    {
        var database = await DatabaseAsync();
        var original = Snapshot();
        var repository = new TaskExecutionRepository(database);
        await repository.UpsertAsync(original, CancellationToken.None);
        var corrupt = changedIdentity switch
        {
            "task" => original with { TaskId = Guid.NewGuid() },
            "context" => original with { ContextId = Guid.NewGuid() },
            "execution" => original with { ExecutionId = Guid.NewGuid() },
            _ => throw new ArgumentOutOfRangeException(nameof(changedIdentity))
        };
        await using (var connection = await database.OpenAsync(CancellationToken.None))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE task_execution_state SET payload_json=$payload WHERE task_id=$task;";
            command.Parameters.AddWithValue("$task", original.TaskId.ToString());
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(corrupt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.GetAsync(original.TaskId, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.GetByContextAsync(original.ContextId, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.GetResumableAsync(CancellationToken.None));
    }

    private async Task<SqliteDatabase> DatabaseAsync()
    {
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(CancellationToken.None);
        return database;
    }

    private static async Task<string> PhysicalPayloadAsync(SqliteDatabase database, Guid taskId)
    {
        await using var connection = await database.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM task_execution_state WHERE task_id=$task;";
        command.Parameters.AddWithValue("$task", taskId.ToString());
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static TaskExecutionSnapshot Snapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var taskId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var completed = new TaskPlanNode(Guid.NewGuid(), null, "Accepted action", TaskPlanNodeState.Completed, TaskActionInterruptionPolicy.AtomicCommit, 1);
        return new(taskId, Guid.NewGuid(), executionId, "Task", TaskExecutionLifecycle.Running,
            TaskExecutionDurability.RecoverableCheckpoint, 1, [completed], [],
            [new(Guid.NewGuid(), taskId, executionId, "Queued follow-up", 1, 0, QueuedFollowUpState.Queued, null, now, now)],
            [], completed.ActionId, now, now) { PersistenceRevision = 1 };
    }

    public void Dispose() => paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "haven-task-cas-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "tasks.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
        public TestPaths() => Directory.CreateDirectory(DataDirectory);
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (IOException) { }
        }
    }
}
