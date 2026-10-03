using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Haven.Infrastructure;

// Owning reusable methods on the same canonical repository and SQL rows. No runtime authority comes from these descriptors.
public sealed partial class WorkspaceStateRepository
{
    public async Task<AutomationOwnerRead<ReusableTaskDefinition>?> GetOwnedTaskAsync(Guid taskID, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM reusable_tasks WHERE lower(replace(replace(replace(id,'-',''),'{',''),'}',''))=lower(replace($id,'-',''));";
        command.Parameters.AddWithValue("$id", taskID.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var row = await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadOwnedReusableTask(reader) : null;
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidDataException("AmbiguousCanonicalDefinitionIdentity");
        return row;
    }

    public async Task<AutomationLibraryPage<ReusableTaskDefinition>> ListOwnedTasksAsync(AutomationLibraryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 100 || query.Search.Length > 256 || query.Cursor is { Length: > 64 })
            throw new ArgumentException("Use a bounded canonical library query.");
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM reusable_tasks WHERE ($archived=1 OR archived_at IS NULL)
            AND ($disabled=1 OR is_enabled=1) AND id > $cursor
            AND ($search='' OR instr(lower(name),lower($search)) > 0)
            ORDER BY id LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$archived", query.IncludeArchived ? 1 : 0);
        command.Parameters.AddWithValue("$disabled", query.IncludeDisabled ? 1 : 0);
        command.Parameters.AddWithValue("$cursor", query.Cursor ?? "");
        command.Parameters.AddWithValue("$search", query.Search);
        command.Parameters.AddWithValue("$limit", query.Limit + 1);
        var rows = new List<AutomationOwnerRead<ReusableTaskDefinition>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add(ReadOwnedReusableTask(reader));
        var next = rows.Count > query.Limit ? rows[query.Limit - 1].RetainedProtectedDescriptors["id"] : null;
        return new(Array.AsReadOnly(rows.Take(query.Limit).ToArray()), next);
    }

    public async Task<AutomationCommitReceiptRead> ObserveTaskCommitAsync(Guid taskID, Guid operationID,
        string payloadSHA256, long expectedRevision, CancellationToken cancellationToken)
    {
        if (taskID == Guid.Empty || operationID == Guid.Empty || expectedRevision < 0 || expectedRevision == long.MaxValue)
            return new(AutomationCommitReceiptObservation.OutcomeUnconfirmed, null);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var identity = await SqliteDatabase.ReadStoreIdentityAsync(connection, false, cancellationToken, transaction).ConfigureAwait(false);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT * FROM reusable_tasks WHERE lower(replace(replace(replace(id,'-',''),'{',''),'}',''))=lower(replace($id,'-',''));"; command.Parameters.AddWithValue("$id", taskID.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var observed = await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadOwnedReusableTask(reader) : null;
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new(AutomationCommitReceiptObservation.OutcomeUnconfirmed, null);
        var receipt = observed?.Value.LastOwnerCommit;
        if (observed is null || receipt is null || receipt.SchemaVersion != 1 ||
            receipt.StoreId != identity.StoreId || receipt.EntityId != taskID ||
            receipt.EntityKind != AutomationDefinitionEntityKind.ReusableTask || receipt.OperationId != operationID ||
            receipt.PayloadSha256 != payloadSHA256 || receipt.ExpectedRevision != expectedRevision ||
            receipt.CommittedRevision != observed.Value.Revision || receipt.CommittedRevision != expectedRevision + 1 ||
            receipt.ObservedOwner != observed.Value.OwnerBinding || observed.Value.Revision < 1 ||
            receipt.ObservedOwner.StoreId != identity.StoreId || string.IsNullOrWhiteSpace(receipt.ObservedOwner.AuthenticationRevision))
            return new(AutomationCommitReceiptObservation.OutcomeUnconfirmed, null);
        return new(AutomationCommitReceiptObservation.CurrentCommitted, receipt);
    }

    public async Task<AutomationDefinitionCommitResult> CompareExchangeOwnedTaskAsync(AutomationDefinitionChange change,
        IAutomationDefinitionCommitAdmission admission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change); ArgumentNullException.ThrowIfNull(admission);
        AutomationDefinitionCommitResult Denied(string code) => new(false, code, null, change.OperationID, change.PayloadSHA256);
        // Resolve the actual same-graph issuer only on the owning commit path, before SQL admission.
        // Canonical reads and ordinary writers never resolve the broker/accessor.
        var actualAuthority = ownerAuthority ?? ownerAuthorityAccessor?.Invoke();
        if (change.RequiresLinkedCommit || actualAuthority is null || !actualAuthority.Issued(admission, factory) ||
            change.EntityKind != AutomationOwnerEntityKind.ReusableTask || change.ReusableTask is not { } proposal ||
            proposal.Id != change.EntityID || proposal.IsEnabled || proposal.OperationalState == AutomationOperationalState.Ready ||
            change.ChangeKind == AutomationDefinitionChangeKind.PublishGraph || proposal.Name is not { Length: > 0 and <= 256 } ||
            change.ExpectedRevision == long.MaxValue)
            return Denied("OwnerAdmissionUnavailable");
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var result = await ApplyOwnedTaskWithinTransactionAsync(change, admission, new SqlOwnerWriteTurn(this, actualAuthority, admission), connection, transaction, false, cancellationToken).ConfigureAwait(false);
        if (result.Committed) await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private sealed record SqlOwnerWriteTurn(WorkspaceStateRepository Issuer, AutomationLocalStoreAuthority Authority,
        IAutomationDefinitionCommitAdmission Admission) : IAutomationSqlOwnerWriteTurn;
    internal IAutomationSqlOwnerWriteTurn? CaptureOwnerWriteTurn(IAutomationDefinitionCommitAdmission admission)
    {
        var authority = ownerAuthority ?? ownerAuthorityAccessor?.Invoke();
        return authority is not null && authority.Issued(admission, factory)
            ? new SqlOwnerWriteTurn(this, authority, admission) : null;
    }
    internal bool UsesConnectionFactory(ISqliteConnectionFactory expected) => ReferenceEquals(factory, expected);
    internal async Task<AutomationDefinitionCommitResult> ApplyOwnedTaskWithinTransactionAsync(AutomationDefinitionChange change,
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
            change.EntityKind != AutomationOwnerEntityKind.ReusableTask || change.ReusableTask is not { } proposal ||
            proposal.Id != change.EntityID || proposal.IsEnabled || proposal.OperationalState == AutomationOperationalState.Ready ||
            change.ChangeKind == AutomationDefinitionChangeKind.PublishGraph || proposal.Name is not { Length: > 0 and <= 256 } ||
            change.ExpectedRevision == long.MaxValue)
            return Denied("OwnerAdmissionUnavailable");
        var identity = await SqliteDatabase.ReadStoreIdentityAsync(connection, false, cancellationToken, transaction).ConfigureAwait(false);
        if (identity.StoreId != change.StoreID) return Denied("OriginalStoreChanged");
        AutomationOwnerRead<ReusableTaskDefinition>? current;
        await using (var inspect = connection.CreateCommand())
        {
            inspect.Transaction = transaction; inspect.CommandText = "SELECT * FROM reusable_tasks WHERE lower(replace(replace(replace(id,'-',''),'{',''),'}',''))=lower(replace($id,'-',''));";
            inspect.Parameters.AddWithValue("$id", change.EntityID.ToString());
            await using var reader = await inspect.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            current = await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadOwnedReusableTask(reader) : null;
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
        if (proposal.GraphJson != current?.Value.GraphJson) return Denied("LegacyGraphPreservationRequired");
        var actor = change.OriginalActor;
        var owner = new AutomationOwnerBinding(identity.StoreId, actor.ProfileId, actor.ActorId, actor.AuthenticationRevision, actor.AccountId, actor.OrganisationId);
        var context = new AutomationDefinitionCommitContext(identity, change.EntityID, change.EntityKind, change.ExpectedRevision,
            change.OperationID, change.PayloadSHA256, change.ActionID, owner);
        if (!await admission.CheckAsync(context, cancellationToken).ConfigureAwait(false)) return Denied("OwnerAdmissionChanged");
        var revision = checked(change.ExpectedRevision + 1); var now = DateTimeOffset.UtcNow;
        var state = change.ChangeKind == AutomationDefinitionChangeKind.Archive ? AutomationOperationalState.Archived :
            change.ChangeKind == AutomationDefinitionChangeKind.Disable ? AutomationOperationalState.Disabled : AutomationOperationalState.NeedsAttention;
        var archive = state == AutomationOperationalState.Archived ? now : (DateTimeOffset?)null;
        var receipt = new AutomationOwnerCommitReceipt(1, identity.StoreId, change.EntityID, AutomationDefinitionEntityKind.ReusableTask,
            change.OperationID, change.PayloadSHA256, change.ExpectedRevision, revision, now, owner);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO reusable_tasks(id,name,description,instruction,container_id,is_enabled,created_at,updated_at,graph_json,
              revision,owner_binding_json,graph_binding_json,definition_metadata_json,operational_state,archived_at,publication_journal_json,owner_commit_receipt_json)
            VALUES($id,$name,$description,$instruction,$containerId,0,$createdAt,$updatedAt,$legacyGraph,
              $revision,$owner,$graph,$metadata,$state,$archive,$journal,$receipt)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name,description=excluded.description,instruction=excluded.instruction,
              container_id=excluded.container_id,is_enabled=0,updated_at=excluded.updated_at,revision=excluded.revision,
              owner_binding_json=excluded.owner_binding_json,graph_binding_json=excluded.graph_binding_json,
              definition_metadata_json=excluded.definition_metadata_json,operational_state=excluded.operational_state,
              archived_at=excluded.archived_at,publication_journal_json=excluded.publication_journal_json,
              owner_commit_receipt_json=excluded.owner_commit_receipt_json WHERE reusable_tasks.revision=$expected;
            """;
        command.Parameters.AddWithValue("$id", current?.RetainedProtectedDescriptors["id"] ?? change.EntityID.ToString()); command.Parameters.AddWithValue("$name", proposal.Name);
        command.Parameters.AddWithValue("$description", proposal.Description);
        command.Parameters.AddWithValue("$instruction", proposal.Instruction);
        command.Parameters.AddWithValue("$legacyGraph", (object?)current?.RetainedProtectedDescriptors["graph_json"] ?? DBNull.Value);
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

    private static AutomationOwnerRead<ReusableTaskDefinition> ReadOwnedReusableTask(SqliteDataReader reader)
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
        var definition = new ReusableTaskDefinition(Identifier("id"), raw["name"] ?? "", raw["description"] ?? "",
            raw["instruction"] ?? "", container == Guid.Empty ? null : container, enabled == 1,
            Timestamp("created_at") ?? DateTimeOffset.MinValue, Timestamp("updated_at") ?? DateTimeOffset.MinValue, raw["graph_json"])
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

}
