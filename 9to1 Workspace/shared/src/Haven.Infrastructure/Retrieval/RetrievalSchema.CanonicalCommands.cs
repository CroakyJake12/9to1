using Microsoft.Data.Sqlite;
using Haven.Core;
using System.Text.Json;

namespace Haven.Infrastructure;

internal static partial class RetrievalSchema
{
    internal static void ConfigureCanonicalTables(SqliteCommand command)
    {
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS retrieval_documents(
                id TEXT PRIMARY KEY,
                scope_kind INTEGER NOT NULL,
                scope_id TEXT NOT NULL,
                source_type TEXT NOT NULL,
                source_id TEXT NOT NULL,
                title TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_retrieval_documents_source
                ON retrieval_documents(scope_kind,scope_id,source_type,source_id);
            CREATE INDEX IF NOT EXISTS ix_retrieval_documents_scope
                ON retrieval_documents(scope_kind,scope_id,updated_at);

            CREATE TABLE IF NOT EXISTS retrieval_chunks(
                id TEXT PRIMARY KEY,
                document_id TEXT NOT NULL REFERENCES retrieval_documents(id) ON DELETE CASCADE,
                ordinal INTEGER NOT NULL,
                text TEXT NOT NULL,
                start_character INTEGER NOT NULL,
                length INTEGER NOT NULL,
                embedding_json TEXT NOT NULL,
                terms_json TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_retrieval_chunks_ordinal ON retrieval_chunks(document_id,ordinal);
            """;
    }
    internal const string SourceStateMarker = "canonical.retrieval.source-states.v1";
    // Called ONLY by the separately approved memory revision writer on its SAME
    // connection/transaction, never from EnsureAsync or any recall/read path.
    internal static void ConfigureCanonicalSourceStateTables(SqliteCommand command)
    {
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS retrieval_source_states(
                scope_kind INTEGER NOT NULL, scope_id TEXT NOT NULL,
                source_type TEXT NOT NULL, source_id TEXT NOT NULL,
                state INTEGER NOT NULL CHECK(state IN (2,3)), operation_id TEXT NOT NULL,
                store_id TEXT NOT NULL, storage_scope TEXT NOT NULL,
                record_sha256 TEXT NOT NULL, updated_at TEXT NOT NULL,
                PRIMARY KEY(scope_kind,scope_id,source_type,source_id));
            INSERT INTO settings(key,value,updated_at)
                VALUES('canonical.retrieval.source-states.v1','1',strftime('%Y-%m-%dT%H:%M:%fZ','now'))
                ON CONFLICT(key) DO NOTHING;
            """;
    }
    internal static async Task<string> ReadSourceEligibilityPredicateAsync(SqliteConnection connection,
        SqliteTransaction transaction, IReadOnlyList<RetrievalScope> requestedScopes, CancellationToken token)
    {
        // Metadata and candidates share one read snapshot, so a concurrently committed
        // retirement cannot be observed as table-absent followed by newly retired rows.
        await using var metadata = connection.CreateCommand(); metadata.Transaction = transaction;
        metadata.CommandText = "SELECT name,type FROM sqlite_schema WHERE name IN ('retrieval_source_states','settings');";
        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using (var reader = await metadata.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (reader.GetString(1) != "table")
                    throw new InvalidOperationException("Canonical recall source-state metadata has an unexpected schema object kind.");
                tables.Add(reader.GetString(0));
            }
        string? marker = null;
        if (tables.Contains("settings"))
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT value FROM settings WHERE key=$key;";
            command.Parameters.AddWithValue("$key", SourceStateMarker);
            var storedMarker = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            if (storedMarker is not null) marker = storedMarker as string ??
                throw new InvalidOperationException("Canonical recall source-state version is invalid.");
        }
        var present = tables.Contains("retrieval_source_states");
        if (!present && marker is null) return ""; // Genuine older stores stay readable.
        if (!present || marker != "1") throw new InvalidOperationException("Canonical recall source-state provenance is missing or unsupported.");
        // This is an observation of the existing canonical identity, never a Home
        // grant. A state copied from another store cannot silently qualify here.
        await using var identityCommand = connection.CreateCommand(); identityCommand.Transaction = transaction;
        identityCommand.CommandText = "SELECT value FROM settings WHERE key=$key;";
        identityCommand.Parameters.AddWithValue("$key", CanonicalSqliteOriginalStoreOwner.IdentityKey);
        var identityJson = await identityCommand.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
        var identity = identityJson is null ? null : JsonSerializer.Deserialize<CanonicalSqliteOriginalStoreOwner.IdentityMetadata>(identityJson);
        if (identity is null || identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty || identity.CreatedAtUtc == default)
            throw new InvalidOperationException("Canonical recall source-state store identity is unavailable.");
        foreach (var requested in requestedScopes.Distinct())
        {
            await using var validate = connection.CreateCommand(); validate.Transaction = transaction;
            validate.CommandText = "SELECT scope_kind,scope_id,source_type,source_id,state,operation_id,store_id,storage_scope,record_sha256,updated_at FROM retrieval_source_states WHERE scope_kind=$kind AND scope_id=$id;";
            validate.Parameters.AddWithValue("$kind", (int)requested.Kind); validate.Parameters.AddWithValue("$id", requested.Id.ToString());
            await using var reader = await validate.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var hash = reader.IsDBNull(8) ? null : reader.GetString(8);
                if (Enumerable.Range(0, 10).Any(reader.IsDBNull) || reader.GetInt32(0) != (int)RetrievalScopeKind.Collection ||
                    !Guid.TryParse(reader.GetString(1), out var record) || record == Guid.Empty || reader.GetString(2) != "knowledge" ||
                    !Guid.TryParse(reader.GetString(3), out var source) || source != record || reader.GetInt32(4) is not (2 or 3) ||
                    !Guid.TryParse(reader.GetString(5), out var operation) || operation == Guid.Empty ||
                    !Guid.TryParse(reader.GetString(6), out var store) || store != identity.StoreId ||
                    hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit) ||
                    !DateTimeOffset.TryParse(reader.GetString(9), System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out _))
                    throw new InvalidOperationException("Canonical recall source-state metadata is invalid.");
                var scope = JsonSerializer.Deserialize<string[]>(reader.GetString(7));
                if (scope is not { Length: 5 } || scope[0] != "assistant-memory.v1" ||
                    !Guid.TryParse(scope[1], out var scopeStore) || scopeStore != store || scope.Skip(2).Any(string.IsNullOrWhiteSpace))
                    throw new InvalidOperationException("Canonical recall source-state lineage is unsupported.");
            }
        }
        return "AND NOT EXISTS (SELECT 1 FROM retrieval_source_states s WHERE s.scope_kind=d.scope_kind AND s.scope_id=d.scope_id AND s.source_type=d.source_type AND s.source_id=d.source_id)";
    }

}
