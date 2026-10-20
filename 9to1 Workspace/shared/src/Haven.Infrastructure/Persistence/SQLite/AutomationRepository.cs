/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/AutomationRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns AutomationRepository. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents automation repository and keeps its related state and behavior together.
/// </summary>
public sealed class AutomationRepository(ISqliteConnectionFactory factory) : IAutomationRepository, IAutomationRunContinuationStore
{
    /// <summary>Exact maintained factory observation only; no READ, WRITE or scheduler authority.</summary>
    public bool HasOriginalSqliteFactory(ISqliteConnectionFactory sameActual) => ReferenceEquals(factory, sameActual);

    /// <summary>
    /// Retrieves all async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM automations ORDER BY updated_at DESC;";
        return await ReadAutomationsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves due async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM automations
            WHERE is_enabled=1 AND next_run_at IS NOT NULL AND next_run_at <= $now
              AND (lease_until IS NULL OR lease_until < $now)
            ORDER BY next_run_at;
            """;
        command.Parameters.AddWithValue("$now", now.ToUniversalTime().ToString("O"));
        return await ReadAutomationsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs upsert asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertAsync(AutomationDefinition automation, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO automations(id,name,mode,instruction,schedule_kind,schedule_json,next_run_at,container_id,is_enabled,created_at,updated_at)
            VALUES($id,$name,$mode,$instruction,$scheduleKind,$scheduleJson,$nextRunAt,$containerId,$isEnabled,$createdAt,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name,mode=excluded.mode,instruction=excluded.instruction,
              schedule_kind=excluded.schedule_kind,schedule_json=excluded.schedule_json,next_run_at=excluded.next_run_at,
              container_id=excluded.container_id,is_enabled=excluded.is_enabled,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", automation.Id.ToString());
        command.Parameters.AddWithValue("$name", automation.Name);
        command.Parameters.AddWithValue("$mode", (int)automation.Mode);
        command.Parameters.AddWithValue("$instruction", automation.Instruction);
        command.Parameters.AddWithValue("$scheduleKind", (int)automation.ScheduleKind);
        command.Parameters.AddWithValue("$scheduleJson", automation.ScheduleJson);
        command.Parameters.AddWithValue("$nextRunAt", (object?)automation.NextRunAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$containerId", (object?)automation.ContainerId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$isEnabled", automation.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", automation.CreatedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", automation.UpdatedAt.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// Performs delete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM automations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts to acquire lease async and reports the result without using failure for normal control flow.
    /// </summary>
    public async Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE automations SET lease_token=$leaseToken, lease_until=$leaseUntil
            WHERE id=$id AND (lease_until IS NULL OR lease_until < $now);
            """;
        command.Parameters.AddWithValue("$leaseToken", leaseToken);
        command.Parameters.AddWithValue("$leaseUntil", leaseUntil.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$id", automationId.ToString());
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <summary>
    /// Completes a Run. Completion is a state transition on the persisted Run row performed in the
    /// same SQLite write transaction as any definition lease release/scheduling. Completion applies only
    /// to a known terminal status (Succeeded, Failed or Cancelled) that carries a completion timestamp.
    /// A Run's canonical identity (AutomationId, pinned definition revision and pinned graph binding) is
    /// immutable, the desired persisted revision must be exactly one greater than the stored revision,
    /// terminal states are final, and an identical persisted payload is a no-write acknowledgment. The
    /// ORIGINAL Run lease token is immutable across a transition: a stored leased Run cannot be completed
    /// with a different (or null) token, so a desired null token cannot bypass the definition CAS. A Run
    /// that carries a real acquired definition lease token is a scheduled write and must still match the
    /// definition's current lease; the definition CAS must affect exactly one row or the whole Run
    /// mutation rolls back. History-only appends (null lease, including the legacy revision-0 new
    /// terminal case) never touch the definition schedule/lease/updated_at.
    /// </summary>
    public async Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var desired = ToRow(run);

        // Invariant validation applies to both brand-new and existing rows.
        if (desired.Revision < 0)
            throw new InvalidOperationException("A Run revision cannot be negative.");
        if (!IsTerminal((AutomationRunStatus)desired.Status))
            throw new InvalidOperationException("Completion applies only to a known terminal status (Succeeded, Failed, or Cancelled).");
        if (desired.CompletedAt is null)
            throw new InvalidOperationException("A completed Run must carry a completion timestamp.");

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var existing = await ReadRunRowAsync(connection, transaction, run.Id, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            // The Run's canonical identity is immutable: the automation binding and the pinned definition
            // revision/graph binding cannot be re-pointed at a newer or different definition.
            if (existing.AutomationId != desired.AutomationId)
                throw new InvalidOperationException("The AutomationId on a Run is immutable and cannot be changed on completion.");
            if (!PinnedStateMatches(existing, desired))
                throw new InvalidOperationException("A Run's pinned definition revision and graph binding are immutable.");

            // An identical persisted payload is a no-write acknowledgment. It is recognized before any
            // definition lease/scheduling requirement, so a lost-ack retry after a newer lease/schedule
            // returns with no writes and never reapplies the supplied nextRunAt.
            if (RowEquals(existing, desired))
                return;

            if (IsTerminal((AutomationRunStatus)existing.Status))
                throw new InvalidOperationException("A completed Run cannot be mutated.");

            // The ORIGINAL Run lease token must be carried across the transition. A stored leased Run
            // cannot be completed with a different token (even when the definition now holds that token)
            // or with a null token that would otherwise bypass the definition CAS.
            if (desired.LeaseToken != existing.LeaseToken)
                throw new InvalidOperationException("A Run's original lease token is immutable across completion.");

            // The desired revision is the revision to persist; it must advance by exactly one from the
            // stored revision. Anything else (stale, jump, or changed-same-version) is rejected.
            if (desired.Revision != existing.Revision + 1)
                throw new InvalidOperationException("Run revision must advance by exactly one from the persisted revision.");

            await UpdateRunRowAsync(connection, transaction, desired, existing.Revision, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // A brand-new Run (for example a scheduler's first terminal write or a history-only append).
            await InsertRunRowAsync(connection, transaction, desired, cancellationToken).ConfigureAwait(false);
        }

        // Scheduled writes (a terminal write that carries a real acquired definition lease token) must
        // match the definition's current lease and mutate the definition. History-only appends (null
        // lease) never touch the definition schedule/lease/updated_at. The definition mutation is a
        // conditional CAS: if it affects zero rows the Run mutation is rolled back.
        if (desired.LeaseToken is not null)
        {
            var released = await ReleaseDefinitionLeaseAsync(connection, transaction,
                desired.AutomationId, desired.LeaseToken, nextRunAt, cancellationToken).ConfigureAwait(false);
            if (!released)
                throw new InvalidOperationException("Definition lease no longer matches; refusing to complete the Run.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves runs async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid automationId, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM automation_runs WHERE automation_id=$automationId ORDER BY scheduled_for DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$automationId", automationId.ToString());
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<AutomationRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadRun(reader));
        }
        return result;
    }

    /// <summary>Reads Runs that are durably waiting to be resumed: a Wait/Delay or approval
    /// continuation whose due time has arrived. The caller resumes from the persisted continuation
    /// instead of restarting the graph, so already-committed node side effects are not repeated.</summary>
    public async Task<IReadOnlyList<AutomationRun>> GetResumableRunsAsync(DateTimeOffset now, int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT * FROM automation_runs
            WHERE status IN ({(int)AutomationRunStatus.Waiting},{(int)AutomationRunStatus.ApprovalRequired},{(int)AutomationRunStatus.RetryPending})
              AND scheduled_for <= $now
            ORDER BY scheduled_for LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$now", now.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<AutomationRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadRun(reader));
        return result;
    }

    /// <summary>Persists a Run that has released its execution resource while it waits, awaits approval
    /// or awaits a bounded retry. Suspension applies only to the Waiting/ApprovalRequired/RetryPending
    /// states, never a terminal or unknown status, and a suspended Run cannot carry a completion timestamp.
    /// The
    /// suspending Run must still hold the definition lease (a lost or stolen token is rejected), and the
    /// ORIGINAL Run lease token is immutable across a transition. Suspension touches ONLY the Run row: it
    /// never advances the definition's schedule, clears its lease, or changes its timestamp, because the
    /// definition is still occupied by the suspended Run.</summary>
    public async Task SuspendRunAsync(AutomationRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.LeaseToken is null)
            throw new InvalidOperationException("A Run cannot be suspended without a definition lease token.");
        if (!IsSupportedSuspendedStatus(run.Status))
            throw new InvalidOperationException("Suspension applies only to Waiting, ApprovalRequired, or RetryPending states.");
        if (run.Revision < 0)
            throw new InvalidOperationException("A Run revision cannot be negative.");
        if (run.CompletedAt is not null)
            throw new InvalidOperationException("A suspended Run cannot carry a completion timestamp.");

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Validate the acquired definition lease: lost/stolen tokens are rejected. Suspend never mutates
        // the definition (schedule, lease or timestamp).
        if (!await DefinitionLeaseMatchesAsync(connection, transaction, run.AutomationId, run.LeaseToken, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Definition lease is not held by this Run; cannot suspend.");

        var existing = await ReadRunRowAsync(connection, transaction, run.Id, cancellationToken).ConfigureAwait(false);
        var desired = ToRow(run);

        if (existing is null)
        {
            await InsertRunRowAsync(connection, transaction, desired, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (existing.AutomationId != desired.AutomationId)
                throw new InvalidOperationException("The AutomationId on a Run is immutable.");
            if (!PinnedStateMatches(existing, desired))
                throw new InvalidOperationException("A Run's pinned definition revision and graph binding are immutable.");
            if (RowEquals(existing, desired))
                return; // no-write acknowledgment
            if (IsTerminal((AutomationRunStatus)existing.Status))
                throw new InvalidOperationException("A completed Run cannot be resumed or re-suspended.");
            if (desired.LeaseToken != existing.LeaseToken)
                throw new InvalidOperationException("A Run's original lease token is immutable across suspension.");
            if (desired.Revision != existing.Revision + 1)
                throw new InvalidOperationException("Run revision must advance by exactly one from the persisted revision.");
            await UpdateRunRowAsync(connection, transaction, desired, existing.Revision, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>True only for the known live completion statuses. Other enum values are not treated as
    /// terminal, and the enum is not renumbered.</summary>
    private static bool IsTerminal(AutomationRunStatus status) =>
        status is AutomationRunStatus.Succeeded or AutomationRunStatus.Failed or AutomationRunStatus.Cancelled;

    /// <summary>True only for the statuses a Run can be durably suspended in. Unknown/other values are
    /// rejected rather than silently accepted as a suspension.</summary>
    private static bool IsSupportedSuspendedStatus(AutomationRunStatus status) =>
        status is AutomationRunStatus.Waiting or AutomationRunStatus.ApprovalRequired or AutomationRunStatus.RetryPending;

    /// <summary>The exact persisted Run row, normalized for comparison.</summary>
    private sealed record AutomationRunRow(
        Guid Id, Guid AutomationId, int Status, DateTimeOffset ScheduledFor,
        DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
        string? Result, string? Error, string? LeaseToken,
        long Revision, long? DefinitionRevision, string? PinnedGraphJson,
        string? AdmissionSnapshotJson, string? ContinuationDescriptorJson, string? DetailsJson);

    private static AutomationRunRow ToRow(AutomationRun run) => new(
        run.Id, run.AutomationId, (int)run.Status, run.ScheduledFor.ToUniversalTime(),
        run.StartedAt?.ToUniversalTime(), run.CompletedAt?.ToUniversalTime(),
        run.Result, run.Error, run.LeaseToken,
        run.Revision, run.DefinitionRevision,
        run.PinnedGraph is { } binding ? System.Text.Json.JsonSerializer.Serialize(binding) : null,
        run.AdmissionSnapshotJson, run.ContinuationDescriptorJson,
        run.Details is { } details ? System.Text.Json.JsonSerializer.Serialize(details) : null);

    /// <summary>Compares the exact serialized persisted fields (including nullable values and canonical
    /// details), not .NET reference equality of deserialized lists.</summary>
    private static bool RowEquals(AutomationRunRow a, AutomationRunRow b) =>
        a.Id == b.Id &&
        a.AutomationId == b.AutomationId &&
        a.Status == b.Status &&
        a.ScheduledFor == b.ScheduledFor &&
        a.StartedAt == b.StartedAt &&
        a.CompletedAt == b.CompletedAt &&
        a.Result == b.Result &&
        a.Error == b.Error &&
        a.LeaseToken == b.LeaseToken &&
        a.Revision == b.Revision &&
        a.DefinitionRevision == b.DefinitionRevision &&
        a.PinnedGraphJson == b.PinnedGraphJson &&
        a.AdmissionSnapshotJson == b.AdmissionSnapshotJson &&
        a.ContinuationDescriptorJson == b.ContinuationDescriptorJson &&
        a.DetailsJson == b.DetailsJson;

    /// <summary>Guards the immutable historical pin: the run's own definition revision and graph binding
    /// must not change across a transition. It does not compare the historical pin to a newer current
    /// active definition.</summary>
    private static bool PinnedStateMatches(AutomationRunRow existing, AutomationRunRow desired) =>
        existing.DefinitionRevision == desired.DefinitionRevision &&
        existing.PinnedGraphJson == desired.PinnedGraphJson;

    private static async Task<AutomationRunRow?> ReadRunRowAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, automation_id, status, scheduled_for, started_at, completed_at, result, error, lease_token,
                   revision, definition_revision, pinned_graph_json, admission_snapshot_json,
                   continuation_descriptor_json, details_json
            FROM automation_runs WHERE id=$id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var ordinal = reader.GetOrdinal("definition_revision");
        var definitionRevision = reader.IsDBNull(ordinal) ? (long?)null : reader.GetInt64(ordinal);
        return new AutomationRunRow(
            reader.Guid("id"), reader.Guid("automation_id"), reader.Int32("status"), reader.DateTimeOffset("scheduled_for"),
            reader.NullableDateTimeOffset("started_at"), reader.NullableDateTimeOffset("completed_at"),
            reader.NullableString("result"), reader.NullableString("error"), reader.NullableString("lease_token"),
            reader.GetInt64(reader.GetOrdinal("revision")), definitionRevision,
            reader.NullableString("pinned_graph_json"), reader.NullableString("admission_snapshot_json"),
            reader.NullableString("continuation_descriptor_json"), reader.NullableString("details_json"));
    }

    private static async Task InsertRunRowAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction, AutomationRunRow row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO automation_runs(id,automation_id,status,scheduled_for,started_at,completed_at,result,error,lease_token,
                revision,definition_revision,pinned_graph_json,admission_snapshot_json,continuation_descriptor_json,details_json)
            VALUES($id,$automationId,$status,$scheduledFor,$startedAt,$completedAt,$result,$error,$leaseToken,
                $revision,$definitionRevision,$pinnedGraph,$admissionSnapshot,$continuation,$details);
            """;
        command.Parameters.AddWithValue("$id", row.Id.ToString());
        AddRunRowBaseParameters(command, row);
        AddCanonicalRunRowParameters(command, row);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Optimistic transition: only the revision we observed may be replaced. A zero-row result
    /// means another writer already advanced the Run, so the transition loses the race.</summary>
    private static async Task UpdateRunRowAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction,
        AutomationRunRow row, long expectedRevision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE automation_runs SET
                automation_id=$automationId, status=$status, scheduled_for=$scheduledFor, started_at=$startedAt,
                completed_at=$completedAt, result=$result, error=$error, lease_token=$leaseToken,
                revision=$revision, definition_revision=$definitionRevision, pinned_graph_json=$pinnedGraph,
                admission_snapshot_json=$admissionSnapshot, continuation_descriptor_json=$continuation, details_json=$details
            WHERE id=$id AND revision=$expectedRevision;
            """;
        command.Parameters.AddWithValue("$id", row.Id.ToString());
        command.Parameters.AddWithValue("$expectedRevision", expectedRevision);
        AddRunRowBaseParameters(command, row);
        AddCanonicalRunRowParameters(command, row);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Concurrent run transition conflict.");
    }

    private static void AddRunRowBaseParameters(Microsoft.Data.Sqlite.SqliteCommand command, AutomationRunRow row)
    {
        command.Parameters.AddWithValue("$automationId", row.AutomationId.ToString());
        command.Parameters.AddWithValue("$status", row.Status);
        command.Parameters.AddWithValue("$scheduledFor", row.ScheduledFor.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$startedAt", (object?)row.StartedAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$completedAt", (object?)row.CompletedAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$result", (object?)row.Result ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)row.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$leaseToken", (object?)row.LeaseToken ?? DBNull.Value);
    }

    /// <summary>Writes the canonical run state that a restarted process needs in order to resume
    /// without replaying committed work or re-pointing a historical Run at a newer definition.</summary>
    private static void AddCanonicalRunRowParameters(Microsoft.Data.Sqlite.SqliteCommand command, AutomationRunRow row)
    {
        command.Parameters.AddWithValue("$revision", row.Revision);
        command.Parameters.AddWithValue("$definitionRevision", (object?)row.DefinitionRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("$pinnedGraph", (object?)row.PinnedGraphJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$admissionSnapshot", (object?)row.AdmissionSnapshotJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$continuation", (object?)row.ContinuationDescriptorJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$details", (object?)row.DetailsJson ?? DBNull.Value);
    }

    /// <summary>Reads whether the definition currently holds exactly the given lease token. Token equality
    /// is the fence; no lease-expiry cutoff is invented without a renewal contract.</summary>
    private static async Task<bool> DefinitionLeaseMatchesAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction,
        Guid automationId, string leaseToken, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT lease_token FROM automations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", automationId.ToString());
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null or DBNull) return false;
        return string.Equals((string)value, leaseToken, StringComparison.Ordinal);
    }

    /// <summary>Conditional definition lease release/scheduling CAS. Returns false (no exception) when
    /// the current lease no longer matches; the caller then rolls back the paired Run mutation.</summary>
    private static async Task<bool> ReleaseDefinitionLeaseAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction,
        Guid automationId, string leaseToken, DateTimeOffset? nextRunAt, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE automations
            SET next_run_at=$nextRunAt, lease_token=NULL, lease_until=NULL, updated_at=$updatedAt
            WHERE id=$id AND lease_token=$leaseToken;
            """;
        command.Parameters.AddWithValue("$nextRunAt", (object?)nextRunAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", automationId.ToString());
        command.Parameters.AddWithValue("$leaseToken", leaseToken);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private static AutomationRun ReadRun(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        var ordinal = reader.GetOrdinal("pinned_graph_json");
        var pinned = reader.IsDBNull(ordinal) ? null : System.Text.Json.JsonSerializer.Deserialize<AutomationGraphBinding>(reader.GetString(ordinal));
        ordinal = reader.GetOrdinal("definition_revision");
        var definitionRevision = reader.IsDBNull(ordinal) ? (long?)null : reader.GetInt64(ordinal);
        ordinal = reader.GetOrdinal("details_json");
        var detailsJson = reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        ordinal = reader.GetOrdinal("continuation_descriptor_json");
        var continuation = reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        ordinal = reader.GetOrdinal("admission_snapshot_json");
        var admission = reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        return new AutomationRun(reader.Guid("id"), reader.Guid("automation_id"),
            (AutomationRunStatus)reader.Int32("status"), reader.DateTimeOffset("scheduled_for"),
            reader.NullableDateTimeOffset("started_at"), reader.NullableDateTimeOffset("completed_at"),
            reader.NullableString("result"), reader.NullableString("error"), reader.NullableString("lease_token"))
        {
            Revision = reader.GetInt64(reader.GetOrdinal("revision")),
            DefinitionRevision = definitionRevision,
            PinnedGraph = pinned,
            AdmissionSnapshotJson = admission,
            ContinuationDescriptorJson = continuation,
            Details = detailsJson is null ? null : System.Text.Json.JsonSerializer.Deserialize<AutomationRunDetails>(detailsJson)
        };
    }

    /// <summary>
    /// Performs read automations asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<IReadOnlyList<AutomationDefinition>> ReadAutomationsAsync(Microsoft.Data.Sqlite.SqliteCommand command, CancellationToken cancellationToken)
    {
        var result = new List<AutomationDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new AutomationDefinition(reader.Guid("id"), reader.String("name"), (HavenMode)reader.Int32("mode"), reader.String("instruction"),
                (AutomationScheduleKind)reader.Int32("schedule_kind"), reader.String("schedule_json"), reader.NullableDateTimeOffset("next_run_at"),
                reader.NullableGuid("container_id"), reader.Boolean("is_enabled"), reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at")));
        }
        return result;
    }
}
