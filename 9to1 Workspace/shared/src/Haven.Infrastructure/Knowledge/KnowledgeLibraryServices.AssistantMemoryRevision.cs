using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class KnowledgeLibraryService
{
    internal sealed class AssistantMemoryOriginalRecord(KnowledgeLibraryService owner,
        AssistantOriginalReadScope scope, KnowledgeRecord record, RetrievalDocument document, string rawJson)
    {
        internal KnowledgeLibraryService Owner => owner;
        internal AssistantOriginalReadScope Scope => scope;
        internal KnowledgeRecord Record => record;
        internal RetrievalDocument Document => document;
        internal string RawJson => rawJson;
        internal string Sha256 { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson)));
    }
    private readonly ConditionalWeakTable<AssistantMemoryOriginalRecord, object> _assistantOriginalRecords = new();

    internal async Task<AssistantMemoryOriginalRecord?> ReadOriginalAssistantMemoryRecordAsync(
        CanonicalSqliteOriginalStoreLease lease, AssistantOriginalReadScope scope, Guid selectedId, CancellationToken token)
    {
        // The selected ID narrows the SAME admitted SQL scope before content. An
        // older owner-issued management page is not limited to prompt selection.
        var record = await ReadSelectedAssistantMemoryWithinSourceAsync(lease, scope, selectedId, token).ConfigureAwait(false);
        if (record is null) return null;
        var rows = new List<IReadOnlyList<RawMemoryColumn>>();
        foreach (var table in new[] { "knowledge_records", "knowledge_record_details" })
        {
            var row = await ReadAssistantMemoryRowsAsync<IReadOnlyList<RawMemoryColumn>?>(lease, command =>
            {
                command.CommandText = $"SELECT * FROM {table} WHERE id=$id;";
                command.Parameters.AddWithValue("$id", record.Id.ToString());
            }, async reader =>
            {
                if (!await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false)) return null;
                return lease.InvokeOriginalSource<IReadOnlyList<RawMemoryColumn>>(() =>
                    Enumerable.Range(0, reader.FieldCount).Select(i => new RawMemoryColumn(reader.GetName(i),
                        reader.GetDataTypeName(i), reader.IsDBNull(i) ? null : reader.GetValue(i))).ToArray());
            }, token).ConfigureAwait(false);
            if (row is null) return null;
            rows.Add(row);
        }
        var document = await ReadAssistantMemoryRowsAsync<RetrievalDocument?>(lease, command =>
        {
            command.CommandText = "SELECT id,scope_kind,scope_id,source_type,source_id,title,content_hash,created_at,updated_at FROM retrieval_documents WHERE scope_kind=$kind AND scope_id=$id AND source_type='knowledge' AND source_id=$id;";
            command.Parameters.AddWithValue("$kind", (int)RetrievalScopeKind.Collection);
            command.Parameters.AddWithValue("$id", record.Id.ToString());
        }, async reader =>
        {
            if (!await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false)) return null;
            return lease.InvokeOriginalSource(() => new RetrievalDocument(Guid.Parse(reader.GetString(0)),
                (RetrievalScopeKind)reader.GetInt32(1), Guid.Parse(reader.GetString(2)), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6),
                DateTimeOffset.Parse(reader.GetString(7), System.Globalization.CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(8), System.Globalization.CultureInfo.InvariantCulture)));
        }, token).ConfigureAwait(false);
        if (document is null) return null;
        var snapshot = new AssistantMemoryOriginalRecord(this, scope, record, document,
            JsonSerializer.Serialize(new { Rows = rows, Document = document }));
        _assistantOriginalRecords.Add(snapshot, new()); return snapshot;
    }
    private sealed record RawMemoryColumn(string Name, string Type, object? Value);

    internal async Task<bool> IsCurrentOriginalAssistantMemoryRecordAsync(CanonicalSqliteOriginalStoreLease lease,
        AssistantMemoryOriginalRecord original, CancellationToken token)
    {
        DemandOriginalAssistantMemoryRecord(lease, original);
        var current = await ReadOriginalAssistantMemoryRecordAsync(lease, original.Scope, original.Record.Id, token).ConfigureAwait(false);
        return current is not null && current.Sha256 == original.Sha256;
    }
    private void DemandOriginalAssistantMemoryRecord(CanonicalSqliteOriginalStoreLease lease, AssistantMemoryOriginalRecord original)
    {
        if (!_assistantOriginalRecords.TryGetValue(original, out _) || !ReferenceEquals(original.Owner, this) ||
            !ReferenceEquals(original.Scope.Store, lease.OriginalOwner) || !lease.OriginalOwner.IsIssuedOriginalLease(lease))
            throw new UnauthorizedAccessException("The SAME protected canonical memory snapshot is required.");
    }
    internal async Task RetireOriginalAssistantMemoryWithinTransactionAsync(CanonicalSqliteOriginalStoreLease lease,
        AssistantMemoryOriginalRecord original, KnowledgeRecord candidate, Guid operationId,
        KnowledgeRecordStatus state, Action demandOriginalWrite, CancellationToken token)
    {
        DemandOriginalAssistantMemoryRecord(lease, original);
        if (state is not (KnowledgeRecordStatus.Superseded or KnowledgeRecordStatus.Rejected))
            throw new ArgumentException("Only a separately approved correction or rejection can retire this source.");
        // Caller rechecked the complete snapshot inside this SAME reserved writer.
        // Preserve all original columns and rows except the maintained status fields.
        await ExecuteAssistantMemoryInsertAsync(lease, command =>
        {
            demandOriginalWrite(); command.CommandText = "UPDATE knowledge_record_details SET status=$status,user_correction=$reason WHERE id=$id AND COALESCE(status,0) IN ($active,$corrected);";
            command.Parameters.AddWithValue("$status", (int)state); command.Parameters.AddWithValue("$reason", candidate.UserCorrection ?? "Explicit user decision");
            command.Parameters.AddWithValue("$id", original.Record.Id.ToString());
            command.Parameters.AddWithValue("$active", (int)KnowledgeRecordStatus.Active); command.Parameters.AddWithValue("$corrected", (int)KnowledgeRecordStatus.Corrected);
        }, token).ConfigureAwait(false);
        await ExecuteAssistantMemoryInsertAsync(lease, command =>
        {
            demandOriginalWrite(); command.CommandText = "UPDATE knowledge_records SET updated_at=$at WHERE id=$id;";
            command.Parameters.AddWithValue("$at", candidate.UpdatedAt.ToString("O")); command.Parameters.AddWithValue("$id", original.Record.Id.ToString());
        }, token).ConfigureAwait(false);
        var provenance = await ExecuteAssistantMemoryScalarAsync(lease, command =>
        {
            demandOriginalWrite(); command.CommandText = "SELECT CASE WHEN EXISTS(SELECT 1 FROM sqlite_schema WHERE type='table' AND name='retrieval_source_states') THEN 1 ELSE 0 END || ':' || CASE WHEN EXISTS(SELECT 1 FROM settings WHERE key=$key) THEN COALESCE((SELECT value FROM settings WHERE key=$key),'invalid-null') ELSE 'absent' END;";
            command.Parameters.AddWithValue("$key", RetrievalSchema.SourceStateMarker);
        }, token).ConfigureAwait(false);
        if (provenance as string is not ("0:absent" or "1:1"))
            throw new InvalidOperationException("The existing recall source-state provenance is unsupported; it cannot be repaired by this memory operation.");
        await ExecuteOriginalMemorySchemaCommandAsync(lease, command =>
        { demandOriginalWrite(); RetrievalSchema.ConfigureCanonicalSourceStateTables(command); }, token).ConfigureAwait(false);
        await ExecuteAssistantMemoryInsertAsync(lease, command =>
        {
            demandOriginalWrite(); command.CommandText = "INSERT INTO retrieval_source_states(scope_kind,scope_id,source_type,source_id,state,operation_id,store_id,storage_scope,record_sha256,updated_at) VALUES($kind,$id,'knowledge',$id,$state,$operation,$store,$scope,$hash,$at);";
            command.Parameters.AddWithValue("$kind", (int)RetrievalScopeKind.Collection); command.Parameters.AddWithValue("$id", original.Record.Id.ToString());
            command.Parameters.AddWithValue("$state", (int)state); command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
            command.Parameters.AddWithValue("$store", lease.OriginalIdentity.StoreId.ToString("D")); command.Parameters.AddWithValue("$scope", original.Scope.StorageScope);
            command.Parameters.AddWithValue("$hash", original.Sha256); command.Parameters.AddWithValue("$at", candidate.UpdatedAt.ToString("O"));
        }, token).ConfigureAwait(false);
        // No global rejection fingerprint: it could suppress another Assistant's
        // same wording. The scoped durable receipt and source state preserve lineage.
    }
}
