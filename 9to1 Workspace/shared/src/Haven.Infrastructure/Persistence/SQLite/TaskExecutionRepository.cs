using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>Publishes one task checkpoint atomically only while its persisted revision and identity are current.</summary>
public sealed class TaskExecutionRepository(ISqliteConnectionFactory factory) : ITaskExecutionRepository
{
    public async Task UpsertAsync(TaskExecutionSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.PersistenceRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(snapshot), "A checkpoint must propose the next positive persistence revision.");
        cancellationToken.ThrowIfCancellationRequested();
        var expectedRevision = snapshot.PersistenceRevision - 1;
        var payload = UnifiedPersistenceJson.Write(snapshot);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // INSERT and its conditional conflict update are one SQLite write statement. A stale
        // continuation cannot recreate a removed task or overwrite a committed checkpoint.
        command.CommandText = """
            INSERT INTO task_execution_state(task_id,context_id,execution_id,state,durability,plan_version,payload_json,created_at,updated_at,persistence_revision)
            SELECT $taskId,$contextId,$executionId,$state,$durability,$planVersion,$payload,$createdAt,$updatedAt,$revision
            WHERE $expectedRevision=0 OR EXISTS(SELECT 1 FROM task_execution_state WHERE task_id=$taskId)
            ON CONFLICT(task_id) DO UPDATE SET state=excluded.state,durability=excluded.durability,
              plan_version=excluded.plan_version,payload_json=excluded.payload_json,updated_at=excluded.updated_at,
              persistence_revision=excluded.persistence_revision
            WHERE task_execution_state.persistence_revision=$expectedRevision
              AND task_execution_state.context_id=excluded.context_id
              AND task_execution_state.execution_id=excluded.execution_id
              AND task_execution_state.created_at=excluded.created_at;
            """;
        command.Parameters.AddWithValue("$taskId", snapshot.TaskId.ToString());
        command.Parameters.AddWithValue("$contextId", snapshot.ContextId.ToString());
        command.Parameters.AddWithValue("$executionId", snapshot.ExecutionId.ToString());
        command.Parameters.AddWithValue("$state", (int)snapshot.State);
        command.Parameters.AddWithValue("$durability", (int)snapshot.Durability);
        command.Parameters.AddWithValue("$planVersion", snapshot.PlanVersion);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$createdAt", snapshot.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", snapshot.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$revision", snapshot.PersistenceRevision);
        command.Parameters.AddWithValue("$expectedRevision", expectedRevision);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (changed != 1)
            throw new TaskExecutionRevisionConflictException(snapshot.TaskId, expectedRevision, snapshot.PersistenceRevision);
        // Returning acknowledges the actual atomic write. No post-commit cancellation check
        // or callback turns an observed durable success into an unacknowledged failure.
    }

    public Task<TaskExecutionSnapshot?> GetAsync(Guid taskId, CancellationToken cancellationToken) =>
        ReadOneAsync("task_id", taskId.ToString(), cancellationToken);

    public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid contextId, CancellationToken cancellationToken) =>
        ReadOneAsync("context_id", contextId.ToString(), cancellationToken);

    public async Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT task_id,context_id,execution_id,payload_json,persistence_revision FROM task_execution_state WHERE state IN($running,$waiting,$blocked,$suspended) ORDER BY updated_at DESC;";
        command.Parameters.AddWithValue("$running", (int)TaskExecutionLifecycle.Running);
        command.Parameters.AddWithValue("$waiting", (int)TaskExecutionLifecycle.WaitingSafeBoundary);
        command.Parameters.AddWithValue("$blocked", (int)TaskExecutionLifecycle.Blocked);
        command.Parameters.AddWithValue("$suspended", (int)TaskExecutionLifecycle.Suspended);
        var result = new List<TaskExecutionSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(ReadSnapshot(reader));
        return result;
    }

    private async Task<TaskExecutionSnapshot?> ReadOneAsync(string column, string value, CancellationToken cancellationToken)
    {
        if (column is not ("task_id" or "context_id")) throw new ArgumentOutOfRangeException(nameof(column));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT task_id,context_id,execution_id,payload_json,persistence_revision FROM task_execution_state WHERE {column}=$value ORDER BY updated_at DESC LIMIT 1;";
        command.Parameters.AddWithValue("$value", value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadSnapshot(reader) : null;
    }

    private static TaskExecutionSnapshot ReadSnapshot(SqliteDataReader reader)
    {
        var snapshot = UnifiedPersistenceJson.Read<TaskExecutionSnapshot>(reader.GetString(3));
        var revision = reader.GetInt64(4);
        if (!Guid.TryParse(reader.GetString(0), out var taskId) || snapshot.TaskId != taskId ||
            !Guid.TryParse(reader.GetString(1), out var contextId) || snapshot.ContextId != contextId ||
            !Guid.TryParse(reader.GetString(2), out var executionId) || snapshot.ExecutionId != executionId ||
            revision < 0 || snapshot.PersistenceRevision != revision)
            throw new InvalidDataException("The durable task payload does not match its owning task, context, execution or persistence revision.");
        return snapshot;
    }
}
