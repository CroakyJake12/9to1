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
using Haven.Application.Automations;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Haven.Infrastructure;

/// <summary>
/// Represents automation repository and keeps its related state and behavior together.
/// </summary>
public sealed partial class AutomationRepository(ISqliteConnectionFactory factory, AutomationLocalStoreAuthority? ownerAuthority = null,
    Func<AutomationLocalStoreAuthority>? ownerAuthorityAccessor = null) : IAutomationRepository, IAutomationOwnerRepository
{
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
            WHERE is_enabled=1 AND operational_state=2 AND archived_at IS NULL
              AND owner_binding_json IS NOT NULL AND graph_binding_json IS NOT NULL
              AND next_run_at IS NOT NULL AND next_run_at <= $now
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
        ArgumentNullException.ThrowIfNull(automation);
        if (automation.Revision != 0 || automation.OwnerBinding is not null || automation.GraphBinding is not null ||
            automation.Metadata is not null || automation.PublicationJournal is not null || automation.LastOwnerCommit is not null ||
            automation.ArchivedAt is not null || automation.OperationalState != AutomationOperationalState.NeedsAttention)
            throw new NotSupportedException("Protected automation definitions require the owning Home-reviewed writer.");
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var storedID = automation.Id.ToString();
        await using (var inspect = connection.CreateCommand())
        {
            inspect.Transaction = transaction;
            inspect.CommandText = "SELECT revision,owner_binding_json,graph_binding_json,definition_metadata_json,publication_journal_json,owner_commit_receipt_json,archived_at,id FROM automations WHERE lower(replace(replace(replace(id,'-',''),'{',''),'}',''))=lower(replace($id,'-',''));";
            inspect.Parameters.AddWithValue("$id", automation.Id.ToString());
            await using var row = await inspect.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var matched = false;
            while (await row.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (matched) throw new NotSupportedException("Ambiguous legacy identity requires explicit recovery.");
                matched = true;
                if (row.GetInt64(0) != 0 || Enumerable.Range(1, 6).Any(index => !row.IsDBNull(index)))
                    throw new NotSupportedException("Ordinary saves cannot change an owned or recovering automation.");
                storedID = row.GetString(7); // Retain actual legacy row spelling; never create a GUID alias row.
            }
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO automations(id,name,mode,instruction,schedule_kind,schedule_json,next_run_at,container_id,is_enabled,created_at,updated_at)
            VALUES($id,$name,$mode,$instruction,$scheduleKind,$scheduleJson,$nextRunAt,$containerId,$isEnabled,$createdAt,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name,mode=excluded.mode,instruction=excluded.instruction,
              schedule_kind=excluded.schedule_kind,schedule_json=excluded.schedule_json,next_run_at=excluded.next_run_at,
              container_id=excluded.container_id,is_enabled=excluded.is_enabled,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", storedID);
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
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// Performs delete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Automation deletion requires the owning recoverable archive operation.");
    }

    /// <summary>
    /// Attempts to acquire lease async and reports the result without using failure for normal control flow.
    /// </summary>
    public Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Automation execution requires the canonical graph/run owner admission.");
    }

    /// <summary>
    /// Performs complete run asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Automation run publication requires the pinned owning runtime writer.");
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
            result.Add(new AutomationRun(reader.Guid("id"), reader.Guid("automation_id"), (AutomationRunStatus)reader.Int32("status"),
                reader.DateTimeOffset("scheduled_for"), reader.NullableDateTimeOffset("started_at"), reader.NullableDateTimeOffset("completed_at"),
                reader.NullableString("result"), reader.NullableString("error"), reader.NullableString("lease_token")));
        }
        return result;
    }

    public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken cancellationToken) =>
        factory is SqliteDatabase database ? database.GetStoreIdentityAsync(cancellationToken) :
        throw new NotSupportedException("Owning automation operations require the actual canonical SQL factory.");

    public async Task<AutomationOwnerRead<AutomationDefinition>?> GetOwnedAsync(Guid automationID, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM automations WHERE lower(replace(replace(replace(id,'-',''),'{',''),'}',''))=lower(replace($id,'-',''));";
        command.Parameters.AddWithValue("$id", automationID.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var row = await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadOwnedDefinition(reader) : null;
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidDataException("AmbiguousCanonicalDefinitionIdentity");
        return row;
    }

    public async Task<AutomationLibraryPage<AutomationDefinition>> ListOwnedAsync(AutomationLibraryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 100 || query.Search.Length > 256 || query.Cursor is { Length: > 64 })
            throw new ArgumentException("Use a bounded canonical library query.");
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM automations WHERE ($archived=1 OR archived_at IS NULL)
            AND ($disabled=1 OR is_enabled=1) AND id > $cursor
            AND ($search='' OR instr(lower(name),lower($search)) > 0)
            ORDER BY id LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$archived", query.IncludeArchived ? 1 : 0);
        command.Parameters.AddWithValue("$disabled", query.IncludeDisabled ? 1 : 0);
        command.Parameters.AddWithValue("$cursor", query.Cursor ?? "");
        command.Parameters.AddWithValue("$search", query.Search);
        command.Parameters.AddWithValue("$limit", query.Limit + 1);
        var rows = new List<AutomationOwnerRead<AutomationDefinition>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add(ReadOwnedDefinition(reader));
        var next = rows.Count > query.Limit ? rows[query.Limit - 1].RetainedProtectedDescriptors["id"] : null;
        return new(Array.AsReadOnly(rows.Take(query.Limit).ToArray()), next);
    }

    public async Task<AutomationCommitReceiptRead> ObserveCommitAsync(Guid automationID, Guid operationID,
        string payloadSHA256, long expectedRevision, CancellationToken cancellationToken)
    {
        if (automationID == Guid.Empty || operationID == Guid.Empty || expectedRevision < 0 || expectedRevision == long.MaxValue)
            return new(AutomationCommitReceiptObservation.OutcomeUnconfirmed, null);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var identity = await SqliteDatabase.ReadStoreIdentityAsync(connection, false, cancellationToken, transaction).ConfigureAwait(false);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT * FROM automations WHERE lower(replace(replace(replace(id,'-',''),'{',''),'}',''))=lower(replace($id,'-',''));"; command.Parameters.AddWithValue("$id", automationID.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var observed = await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadOwnedDefinition(reader) : null;
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new(AutomationCommitReceiptObservation.OutcomeUnconfirmed, null);
        var receipt = observed?.Value.LastOwnerCommit;
        if (observed is null || receipt is null || receipt.SchemaVersion != 1 ||
            receipt.StoreId != identity.StoreId || receipt.EntityId != automationID ||
            receipt.EntityKind != AutomationDefinitionEntityKind.Automation || receipt.OperationId != operationID ||
            receipt.PayloadSha256 != payloadSHA256 || receipt.ExpectedRevision != expectedRevision ||
            receipt.CommittedRevision != observed.Value.Revision || receipt.CommittedRevision != expectedRevision + 1 ||
            receipt.ObservedOwner != observed.Value.OwnerBinding || observed.Value.Revision < 1 ||
            receipt.ObservedOwner.StoreId != identity.StoreId || string.IsNullOrWhiteSpace(receipt.ObservedOwner.AuthenticationRevision))
            return new(AutomationCommitReceiptObservation.OutcomeUnconfirmed, null);
        return new(AutomationCommitReceiptObservation.CurrentCommitted, receipt);
    }

    public async Task<AutomationDefinitionCommitResult> CompareExchangeOwnedAsync(AutomationDefinitionChange change,
        IAutomationDefinitionCommitAdmission admission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change); ArgumentNullException.ThrowIfNull(admission);
        AutomationDefinitionCommitResult Denied(string code) => new(false, code, null, change.OperationID, change.PayloadSHA256);
        // Resolve the actual same-graph issuer only on the owning commit path, before SQL admission.
        // Canonical reads and ordinary writers never resolve the broker/accessor.
        var actualAuthority = ownerAuthority ?? ownerAuthorityAccessor?.Invoke();
        if (change.RequiresLinkedCommit || actualAuthority is null || !actualAuthority.Issued(admission, factory) ||
            change.EntityKind != AutomationOwnerEntityKind.Automation || change.Automation is not { } proposal ||
            proposal.Id != change.EntityID || proposal.IsEnabled || proposal.OperationalState == AutomationOperationalState.Ready ||
            change.ChangeKind == AutomationDefinitionChangeKind.PublishGraph || proposal.Name is not { Length: > 0 and <= 256 } ||
            change.ExpectedRevision == long.MaxValue)
            return Denied("OwnerAdmissionUnavailable");
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var result = await ApplyOwnedDefinitionWithinTransactionAsync(change, admission, new SqlOwnerWriteTurn(this, actualAuthority, admission), connection, transaction, false, cancellationToken).ConfigureAwait(false);
        if (result.Committed) await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private sealed record SqlOwnerWriteTurn(AutomationRepository Issuer, AutomationLocalStoreAuthority Authority,
        IAutomationDefinitionCommitAdmission Admission) : IAutomationSqlOwnerWriteTurn;
    internal IAutomationSqlOwnerWriteTurn? CaptureOwnerWriteTurn(IAutomationDefinitionCommitAdmission admission)
    {
        var authority = ownerAuthority ?? ownerAuthorityAccessor?.Invoke();
        return authority is not null && authority.Issued(admission, factory)
            ? new SqlOwnerWriteTurn(this, authority, admission) : null;
    }
    internal bool UsesConnectionFactory(ISqliteConnectionFactory expected) => ReferenceEquals(factory, expected);
    internal async Task<AutomationDefinitionCommitResult> ApplyOwnedDefinitionWithinTransactionAsync(AutomationDefinitionChange change,
        IAutomationDefinitionCommitAdmission admission, IAutomationSqlOwnerWriteTurn ownerTurn,
        SqliteConnection connection, SqliteTransaction transaction, bool allowLinkedCommit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change); ArgumentNullException.ThrowIfNull(admission);
        AutomationDefinitionCommitResult Denied(string code) => new(false, code, null, change.OperationID, change.PayloadSHA256);
        // Resolve the actual same-graph issuer only on the owning commit path, before SQL admission.
        // Canonical reads and ordinary writers never resolve the broker/accessor.
        if (ownerTurn is not SqlOwnerWriteTurn issued || !ReferenceEquals(issued.Issuer, this) ||
            !ReferenceEquals(issued.Admission, admission)) return Denied("OwnerAdmissionUnavailable");
        var actualAuthority = issued.Authority; // Actual private turn captured BEFORE opening SQL; no DI under this lease.
        if (change.RequiresLinkedCommit && !allowLinkedCommit || actualAuthority is null || !actualAuthority.Issued(admission, factory) ||
            change.EntityKind != AutomationOwnerEntityKind.Automation || change.Automation is not { } proposal ||
            proposal.Id != change.EntityID || proposal.IsEnabled || proposal.OperationalState == AutomationOperationalState.Ready ||
            change.ChangeKind == AutomationDefinitionChangeKind.PublishGraph || proposal.Name is not { Length: > 0 and <= 256 } ||
            change.ExpectedRevision == long.MaxValue)
            return Denied("OwnerAdmissionUnavailable");
        var identity = await SqliteDatabase.ReadStoreIdentityAsync(connection, false, cancellationToken, transaction).ConfigureAwait(false);
        if (identity.StoreId != change.StoreID) return Denied("OriginalStoreChanged");
        AutomationOwnerRead<AutomationDefinition>? current;
        await using (var inspect = connection.CreateCommand())
        {
            inspect.Transaction = transaction; inspect.CommandText = "SELECT * FROM automations WHERE lower(replace(replace(replace(id,'-',''),'{',''),'}',''))=lower(replace($id,'-',''));";
            inspect.Parameters.AddWithValue("$id", change.EntityID.ToString());
            await using var reader = await inspect.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            current = await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadOwnedDefinition(reader) : null;
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return Denied("AmbiguousCanonicalDefinitionIdentity");
        }
        if (current is null && (change.ExpectedRevision != 0 || change.ChangeKind != AutomationDefinitionChangeKind.Create) ||
            current is not null && (change.ChangeKind == AutomationDefinitionChangeKind.Create || current.Value.Revision != change.ExpectedRevision))
            return Denied("RevisionConflict");
        var recovering = change.ChangeKind == AutomationDefinitionChangeKind.RecoverLegacy;
        if (recovering && (current is null || current.RecoveryCode != "LegacyUnboundDefinition" ||
            change.RecoveryRowSHA256 != AutomationDefinitionChange.ComputeRawRowSHA256(current.RetainedProtectedDescriptors) ||
            JsonSerializer.Serialize(proposal) != JsonSerializer.Serialize(current.Value with { IsEnabled = false, OperationalState = AutomationOperationalState.NeedsAttention })))
            return Denied("LegacyRecoveryObservationChanged");
        if (!recovering && current?.RequiresRecovery == true) return Denied("ExplicitDefinitionRecoveryRequired");
        // The actual graph publisher is a separate owner. A descriptive proposal cannot replace its stable binding/journal.
        if (current is null && (proposal.GraphBinding is not null || proposal.PublicationJournal is not null) ||
            current is not null && (proposal.GraphBinding != current.Value.GraphBinding || proposal.PublicationJournal != current.Value.PublicationJournal) ||
            current is not null && current.RetainedProtectedDescriptors["owner_binding_json"] is not null && current.Value.OwnerBinding is null ||
            current?.Value.OwnerBinding is { } priorOwner && (priorOwner.StoreId != identity.StoreId ||
                priorOwner.ProfileId != change.OriginalActor.ProfileId || priorOwner.AccountId != change.OriginalActor.AccountId ||
                priorOwner.OrganisationId != change.OriginalActor.OrganisationId))
            return Denied("ProtectedBindingChanged");
        var actor = change.OriginalActor;
        var owner = new AutomationOwnerBinding(identity.StoreId, actor.ProfileId, actor.ActorId, actor.AuthenticationRevision, actor.AccountId, actor.OrganisationId);
        var context = new AutomationDefinitionCommitContext(identity, change.EntityID, change.EntityKind, change.ExpectedRevision,
            change.OperationID, change.PayloadSHA256, change.ActionID, owner);
        if (!await admission.CheckAsync(context, cancellationToken).ConfigureAwait(false)) return Denied("OwnerAdmissionChanged");
        var revision = checked(change.ExpectedRevision + 1); var now = DateTimeOffset.UtcNow;
        var state = change.ChangeKind == AutomationDefinitionChangeKind.Archive ? AutomationOperationalState.Archived :
            change.ChangeKind == AutomationDefinitionChangeKind.Disable ? AutomationOperationalState.Disabled : AutomationOperationalState.NeedsAttention;
        var archive = state == AutomationOperationalState.Archived ? now : (DateTimeOffset?)null;
        var receipt = new AutomationOwnerCommitReceipt(1, identity.StoreId, change.EntityID, AutomationDefinitionEntityKind.Automation,
            change.OperationID, change.PayloadSHA256, change.ExpectedRevision, revision, now, owner);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO automations(id,name,mode,instruction,schedule_kind,schedule_json,next_run_at,container_id,is_enabled,created_at,updated_at,
              revision,owner_binding_json,graph_binding_json,definition_metadata_json,operational_state,archived_at,publication_journal_json,owner_commit_receipt_json)
            VALUES($id,$name,$mode,$instruction,$scheduleKind,$scheduleJson,$nextRunAt,$containerId,0,$createdAt,$updatedAt,
              $revision,$owner,$graph,$metadata,$state,$archive,$journal,$receipt)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name,mode=excluded.mode,instruction=excluded.instruction,
              schedule_kind=excluded.schedule_kind,schedule_json=excluded.schedule_json,next_run_at=excluded.next_run_at,
              container_id=excluded.container_id,is_enabled=0,updated_at=excluded.updated_at,revision=excluded.revision,
              owner_binding_json=excluded.owner_binding_json,graph_binding_json=excluded.graph_binding_json,
              definition_metadata_json=excluded.definition_metadata_json,operational_state=excluded.operational_state,
              archived_at=excluded.archived_at,publication_journal_json=excluded.publication_journal_json,
              owner_commit_receipt_json=excluded.owner_commit_receipt_json WHERE automations.revision=$expected;
            """;
        command.Parameters.AddWithValue("$id", current?.RetainedProtectedDescriptors["id"] ?? change.EntityID.ToString()); command.Parameters.AddWithValue("$name", proposal.Name);
        command.Parameters.AddWithValue("$mode", (int)proposal.Mode); command.Parameters.AddWithValue("$instruction", proposal.Instruction);
        command.Parameters.AddWithValue("$scheduleKind", (int)proposal.ScheduleKind); command.Parameters.AddWithValue("$scheduleJson", proposal.ScheduleJson);
        command.Parameters.AddWithValue("$nextRunAt", (object?)proposal.NextRunAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$containerId", (object?)proposal.ContainerId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", (current?.Value.CreatedAt ?? proposal.CreatedAt).ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O")); command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$expected", change.ExpectedRevision); command.Parameters.AddWithValue("$owner", JsonSerializer.Serialize(owner));
        command.Parameters.AddWithValue("$graph", (object?)current?.RetainedProtectedDescriptors["graph_binding_json"] ?? DBNull.Value);
        command.Parameters.AddWithValue("$journal", (object?)current?.RetainedProtectedDescriptors["publication_journal_json"] ?? DBNull.Value);
        command.Parameters.AddWithValue("$metadata", proposal.Metadata is null ? DBNull.Value : JsonSerializer.Serialize(proposal.Metadata));
        command.Parameters.AddWithValue("$state", (int)state); command.Parameters.AddWithValue("$archive", (object?)archive?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) return Denied("RevisionConflict");
        // Only raw issuer receipts/actor observations under this SQL transaction; no repository/resource recursion.
        if (!await admission.CheckAsync(context, cancellationToken).ConfigureAwait(false)) return Denied("OwnerAdmissionChanged");
        return new(true, "DefinitionCommitted", revision, change.OperationID, change.PayloadSHA256);
    }

    private static AutomationOwnerRead<AutomationDefinition> ReadOwnedDefinition(SqliteDataReader reader)
    {
        var raw = new Dictionary<string, string?>();
        for (var index = 0; index < reader.FieldCount; index++) raw[reader.GetName(index)] = reader.IsDBNull(index) ? null :
            Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture);
        var recovery = false;
        T? Descriptor<T>(string key) where T : class
        {
            if (raw[key] is not { } text) return null;
            try { if (text.Length > 2 * 1024 * 1024) throw new JsonException(); return JsonSerializer.Deserialize<T>(text) ?? throw new JsonException(); }
            catch (JsonException) { recovery = true; return null; }
        }
        var owner = Descriptor<AutomationOwnerBinding>("owner_binding_json");
        var graph = Descriptor<AutomationDefinitionGraphBinding>("graph_binding_json");
        var metadata = Descriptor<AutomationDefinitionMetadata>("definition_metadata_json");
        var journal = Descriptor<AutomationGraphPublicationJournal>("publication_journal_json");
        var receipt = Descriptor<AutomationOwnerCommitReceipt>("owner_commit_receipt_json");
        var protectedRecovery = recovery; recovery = false;
        Guid Identifier(string key, bool optional = false)
        {
            if (raw[key] is null && optional) return Guid.Empty;
            if (Guid.TryParse(raw[key], out var value) && value != Guid.Empty) return value;
            recovery = true; return Guid.Empty; // Raw identity is retained; an empty display identity grants no owner mutation.
        }
        DateTimeOffset? Timestamp(string key, bool optional = false)
        {
            if (raw[key] is null && optional) return null;
            if (DateTimeOffset.TryParse(raw[key], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var value)) return value;
            recovery = true; return null;
        }
        long Number(string key)
        {
            if (long.TryParse(raw[key], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var value)) return value;
            recovery = true; return 0;
        }
        TEnum Enumeration<TEnum>(string key) where TEnum : struct, Enum
        {
            var number = Number(key);
            if (number is >= int.MinValue and <= int.MaxValue)
            {
                var value = (TEnum)Enum.ToObject(typeof(TEnum), (int)number);
                if (Enum.IsDefined(value)) return value;
            }
            recovery = true; return default;
        }
        var container = Identifier("container_id", optional: true);
        var enabled = Number("is_enabled");
        if (enabled is not (0 or 1)) recovery = true;
        var definition = new AutomationDefinition(Identifier("id"), raw["name"] ?? "", Enumeration<HavenMode>("mode"),
            raw["instruction"] ?? "", Enumeration<AutomationScheduleKind>("schedule_kind"), raw["schedule_json"] ?? "",
            Timestamp("next_run_at", optional: true), container == Guid.Empty ? null : container, enabled == 1,
            Timestamp("created_at") ?? DateTimeOffset.MinValue, Timestamp("updated_at") ?? DateTimeOffset.MinValue)
        { Revision = Number("revision"), OwnerBinding = owner, GraphBinding = graph, Metadata = metadata,
            PublicationJournal = journal, LastOwnerCommit = receipt,
            OperationalState = Enumeration<AutomationOperationalState>("operational_state"), ArchivedAt = Timestamp("archived_at", optional: true) };
        var baseRecovery = recovery; recovery |= protectedRecovery;
        recovery |= owner is null || definition.Revision < 1 || !Enum.IsDefined(definition.OperationalState) ||
            owner.StoreId == Guid.Empty || string.IsNullOrWhiteSpace(owner.ProfileId) || string.IsNullOrWhiteSpace(owner.ActorId);
        return new(definition, recovery, recovery ?
            (!baseRecovery && !protectedRecovery && owner is null && definition.Revision == 0
                ? "LegacyUnboundDefinition" : "ProtectedDefinitionRequiresRecovery") : null,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string?>(raw));
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
            var owned = ReadOwnedDefinition(reader);
            result.Add(owned.RequiresRecovery ? owned.Value with { IsEnabled = false, OperationalState = AutomationOperationalState.NeedsAttention } : owned.Value);
        }
        return result;
    }
}
