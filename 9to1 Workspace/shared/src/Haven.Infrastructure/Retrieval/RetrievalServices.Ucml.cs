using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class RetrievalIndexService
{
    // The generic index owner cannot replace/delete UCML's opaque canonical index identity.
    // This check is INSIDE the SAME serialized SQLite writer transaction as the mutation.
    // An earlier check before awaiting embeddings is not sufficient against concurrent import.
    private static async Task DemandGenericKnowledgeIndexMutationAsync(SqliteConnection connection,
        SqliteTransaction transaction, RetrievalScope scope, string sourceType, string sourceId, CancellationToken token)
    {
        if (scope.Kind != RetrievalScopeKind.Collection || sourceType.Trim() != "knowledge") return;
        await using var schema = connection.CreateCommand(); schema.Transaction = transaction;
        schema.CommandText = "SELECT type FROM sqlite_schema WHERE name='knowledge_records';";
        var kind = await schema.ExecuteScalarAsync(token).ConfigureAwait(false);
        if (kind is null) return; // This index has no canonical Knowledge owner or records yet.
        if (kind is not "table") throw new UnauthorizedAccessException("The canonical Knowledge source is not an ordinary table.");
        await using var record = connection.CreateCommand(); record.Transaction = transaction;
        record.CommandText = "SELECT learned_because FROM knowledge_records WHERE id=$id;";
        record.Parameters.AddWithValue("$id", sourceId.Trim());
        if (await record.ExecuteScalarAsync(token).ConfigureAwait(false) is string metadata &&
            metadata.StartsWith("ucml.v1:", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("UCML context cannot enter or be removed through the generic retrieval index.");
    }
}
