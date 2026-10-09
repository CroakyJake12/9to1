using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class KnowledgeLibraryService
{
    // Pure schema observation under an already admitted memory READ. Incomplete setup
    // enables a truthful first-write workflow; it never calls either canonical Ensure.
    internal static async Task<bool> IsOriginalAssistantMemorySchemaReadyAsync(
        CanonicalSqliteOriginalStoreLease lease, CancellationToken token)
    {
        var tables = await ExecuteAssistantMemoryScalarAsync(lease, command => command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('knowledge_records','knowledge_record_details','retrieval_documents','retrieval_chunks');", token).ConfigureAwait(false);
        if (Convert.ToInt64(tables) != 4) return false;
        var columns = await ReadOriginalKnowledgeDetailColumnsAsync(lease, token).ConfigureAwait(false);
        return KnowledgeSchema.CanonicalDetailColumns.All(value => columns.Contains(value.Name));
    }
    internal static async Task EnsureOriginalAssistantMemorySchemaWithinTransactionAsync(
        CanonicalSqliteOriginalStoreLease lease, Action demandOriginalWrite, CancellationToken token)
    {
        // ONLY the separately approved writer calls this method, after beginning the
        // SAME protected transaction. Reuse maintained canonical SQL/column definitions.
        await ExecuteOriginalMemorySchemaCommandAsync(lease, command =>
        { demandOriginalWrite(); RetrievalSchema.ConfigureCanonicalTables(command); }, token).ConfigureAwait(false);
        await ExecuteOriginalMemorySchemaCommandAsync(lease, command =>
        { demandOriginalWrite(); KnowledgeSchema.ConfigureCanonicalTables(command); }, token).ConfigureAwait(false);
        var columns = await ReadOriginalKnowledgeDetailColumnsAsync(lease, token).ConfigureAwait(false);
        foreach (var (name, definition) in KnowledgeSchema.CanonicalDetailColumns)
        {
            if (columns.Contains(name)) continue;
            await ExecuteOriginalMemorySchemaCommandAsync(lease, command =>
            {
                demandOriginalWrite();
                // Both tokens come only from the SAME private maintained schema table.
                command.CommandText = $"ALTER TABLE knowledge_record_details ADD COLUMN {name} {definition};";
            }, token).ConfigureAwait(false);
        }
        foreach (var version in new[] { 10, 12 })
            await ExecuteOriginalMemorySchemaCommandAsync(lease, command =>
            {
                demandOriginalWrite();
                command.CommandText = "INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES($version,$now);";
                command.Parameters.AddWithValue("$version", version);
                command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            }, token).ConfigureAwait(false);
    }
    private static Task<HashSet<string>> ReadOriginalKnowledgeDetailColumnsAsync(CanonicalSqliteOriginalStoreLease lease,
        CancellationToken token) => ReadAssistantMemoryRowsAsync(lease, command =>
            command.CommandText = "PRAGMA table_info(knowledge_record_details);", async reader =>
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
                columns.Add(lease.InvokeOriginalSource(() => reader.GetString(1)));
            return columns;
        }, token);
    private static async Task ExecuteOriginalMemorySchemaCommandAsync(CanonicalSqliteOriginalStoreLease lease,
        Action<SqliteCommand> configure, CancellationToken token)
    {
        SqliteCommand? command = null; var errors = new List<Exception>();
        try
        {
            lease.InvokeOriginalSource(() => { command = lease.Connection.CreateCommand(); return command; });
            lease.InvokeOriginalSource(() => { command!.Transaction = lease.Transaction; configure(command); });
            await lease.ReadOriginalSourceAsync(() => command!.ExecuteNonQueryAsync(token)).ConfigureAwait(false);
        }
        catch (Exception failure) { errors.Add(failure); }
        if (command is not null) await CloseAssistantMemoryResourceAsync(lease, command, errors).ConfigureAwait(false);
        ThrowAssistantMemoryErrors(errors);
    }
}
