/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/WorkspaceStateRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns WorkspaceStateRepository. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Application.Automations;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Represents workspace state repository and keeps its related state and behavior together.
/// </summary>
public sealed partial class WorkspaceStateRepository(ISqliteConnectionFactory factory, AutomationLocalStoreAuthority? ownerAuthority = null,
    Func<AutomationLocalStoreAuthority>? ownerAuthorityAccessor = null) : IWorkspaceStateRepository, IReusableTaskOwnerRepository
{
    /// <summary>
    /// Retrieves reusable_tasks async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ReusableTaskDefinition>> GetReusableTasksAsync(Guid? containerId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = containerId is null
            ? "SELECT * FROM reusable_tasks WHERE is_enabled=1 ORDER BY name;"
            : "SELECT * FROM reusable_tasks WHERE is_enabled=1 AND (container_id IS NULL OR container_id=$containerId) ORDER BY name;";
        if (containerId is not null) command.Parameters.AddWithValue("$containerId", containerId.Value.ToString());
        var result = new List<ReusableTaskDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var owned = ReadOwnedReusableTask(reader);
            result.Add(owned.RequiresRecovery ? owned.Value with { IsEnabled = false, OperationalState = AutomationOperationalState.NeedsAttention } : owned.Value);
        }
        return result;
    }

    /// <summary>
    /// Performs upsert macro asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertReusableTaskAsync(ReusableTaskDefinition macro, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(macro);
        if (macro.Revision != 0 || macro.OwnerBinding is not null || macro.GraphBinding is not null ||
            macro.Metadata is not null || macro.PublicationJournal is not null || macro.LastOwnerCommit is not null ||
            macro.ArchivedAt is not null || macro.OperationalState != AutomationOperationalState.NeedsAttention)
            throw new NotSupportedException("Protected reusable tasks require the owning Home-reviewed writer.");
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var storedID = macro.Id.ToString();
        await using (var inspect = connection.CreateCommand())
        {
            inspect.Transaction = transaction;
            inspect.CommandText = "SELECT revision,owner_binding_json,graph_binding_json,definition_metadata_json,publication_journal_json,owner_commit_receipt_json,archived_at,id FROM reusable_tasks WHERE lower(replace(replace(replace(id,'-',''),'{',''),'}',''))=lower(replace($id,'-',''));";
            inspect.Parameters.AddWithValue("$id", macro.Id.ToString());
            await using var row = await inspect.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var matched = false;
            while (await row.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (matched) throw new NotSupportedException("Ambiguous legacy identity requires explicit recovery.");
                matched = true;
                if (row.GetInt64(0) != 0 || Enumerable.Range(1, 6).Any(index => !row.IsDBNull(index)))
                    throw new NotSupportedException("Ordinary saves cannot change an owned or recovering reusable task.");
                storedID = row.GetString(7); // Retain actual legacy row spelling; never create a GUID alias row.
            }
        }
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO reusable_tasks(id,name,description,instruction,container_id,is_enabled,created_at,updated_at,graph_json)
            VALUES($id,$name,$description,$instruction,$containerId,$isEnabled,$createdAt,$updatedAt,$graphJson)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name,description=excluded.description,instruction=excluded.instruction,
              container_id=excluded.container_id,is_enabled=excluded.is_enabled,updated_at=excluded.updated_at,graph_json=excluded.graph_json;
            """;
        command.Parameters.AddWithValue("$id", storedID);
        command.Parameters.AddWithValue("$name", macro.Name);
        command.Parameters.AddWithValue("$description", macro.Description);
        command.Parameters.AddWithValue("$instruction", macro.Instruction);
        command.Parameters.AddWithValue("$containerId", (object?)macro.ContainerId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$isEnabled", macro.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", macro.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", macro.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$graphJson", (object?)macro.GraphJson ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs delete macro asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteReusableTaskAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Reusable task deletion requires the owning recoverable archive operation.");
    }

    /// <summary>
    /// Retrieves versions async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<WorkspaceVersion>> GetVersionsAsync(Guid? containerId, string? relativePath, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var clauses = new List<string>();
        if (containerId is not null)
        {
            clauses.Add("container_id=$containerId");
            command.Parameters.AddWithValue("$containerId", containerId.Value.ToString());
        }
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            clauses.Add("relative_path=$relativePath");
            command.Parameters.AddWithValue("$relativePath", relativePath);
        }
        command.CommandText = $"SELECT * FROM workspace_versions{(clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses))} ORDER BY created_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        var result = new List<WorkspaceVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new WorkspaceVersion(reader.Guid("id"), reader.NullableGuid("conversation_id"), reader.NullableGuid("container_id"),
                reader.String("workspace_root"), reader.String("relative_path"), (WorkspaceVersionKind)reader.Int32("kind"),
                reader.String("before_content"), reader.String("after_content"), reader.String("summary"), reader.Int32("lines_added"),
                reader.Int32("lines_removed"), reader.DateTimeOffset("created_at")));
        return result;
    }

    /// <summary>
    /// Performs add version asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task AddVersionAsync(WorkspaceVersion version, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workspace_versions(id,conversation_id,container_id,workspace_root,relative_path,kind,before_content,after_content,summary,lines_added,lines_removed,created_at)
            VALUES($id,$conversationId,$containerId,$workspaceRoot,$relativePath,$kind,$beforeContent,$afterContent,$summary,$linesAdded,$linesRemoved,$createdAt);
            """;
        command.Parameters.AddWithValue("$id", version.Id.ToString());
        command.Parameters.AddWithValue("$conversationId", (object?)version.ConversationId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$containerId", (object?)version.ContainerId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$workspaceRoot", version.WorkspaceRoot);
        command.Parameters.AddWithValue("$relativePath", version.RelativePath);
        command.Parameters.AddWithValue("$kind", (int)version.Kind);
        command.Parameters.AddWithValue("$beforeContent", version.BeforeContent);
        command.Parameters.AddWithValue("$afterContent", version.AfterContent);
        command.Parameters.AddWithValue("$summary", version.Summary);
        command.Parameters.AddWithValue("$linesAdded", version.LinesAdded);
        command.Parameters.AddWithValue("$linesRemoved", version.LinesRemoved);
        command.Parameters.AddWithValue("$createdAt", version.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves decisions async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<DecisionRecord>> GetDecisionsAsync(Guid containerId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM decisions WHERE container_id=$containerId ORDER BY updated_at DESC;";
        command.Parameters.AddWithValue("$containerId", containerId.ToString());
        var result = new List<DecisionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new DecisionRecord(reader.Guid("id"), reader.Guid("container_id"), reader.String("title"), reader.String("decision_text"),
                reader.String("alternatives"), reader.String("reasoning"), reader.String("evidence"), reader.String("consequences"),
                reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at")));
        return result;
    }

    /// <summary>
    /// Performs upsert decision asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertDecisionAsync(DecisionRecord decision, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO decisions(id,container_id,title,decision_text,alternatives,reasoning,evidence,consequences,created_at,updated_at)
            VALUES($id,$containerId,$title,$decision,$alternatives,$reasoning,$evidence,$consequences,$createdAt,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET title=excluded.title,decision_text=excluded.decision_text,alternatives=excluded.alternatives,
              reasoning=excluded.reasoning,evidence=excluded.evidence,consequences=excluded.consequences,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", decision.Id.ToString());
        command.Parameters.AddWithValue("$containerId", decision.ContainerId.ToString());
        command.Parameters.AddWithValue("$title", decision.Title);
        command.Parameters.AddWithValue("$decision", decision.Decision);
        command.Parameters.AddWithValue("$alternatives", decision.Alternatives);
        command.Parameters.AddWithValue("$reasoning", decision.Reasoning);
        command.Parameters.AddWithValue("$evidence", decision.Evidence);
        command.Parameters.AddWithValue("$consequences", decision.Consequences);
        command.Parameters.AddWithValue("$createdAt", decision.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", decision.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs delete decision asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteDecisionAsync(Guid id, CancellationToken cancellationToken) => ExecuteDeleteAsync("decisions", id, cancellationToken);

    /// <summary>
    /// Runs execute delete async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task ExecuteDeleteAsync(string table, Guid id, CancellationToken cancellationToken)
    {
        if (table is not ("reusable_tasks" or "decisions")) throw new ArgumentOutOfRangeException(nameof(table));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
