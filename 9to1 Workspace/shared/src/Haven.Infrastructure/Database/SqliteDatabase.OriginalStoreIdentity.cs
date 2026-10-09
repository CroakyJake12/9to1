using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class SqliteDatabase
{
    private static async Task InitializeOriginalStoreIdentityAsync(SqliteConnection connection,
        SqliteTransaction actualInitializationTransaction, CancellationToken token)
    {
        var metadata = new CanonicalSqliteOriginalStoreOwner.IdentityMetadata(1, Guid.NewGuid(), DateTimeOffset.UtcNow);
        await using var command = connection.CreateCommand();
        command.Transaction = actualInitializationTransaction;
        command.CommandText = "INSERT INTO settings(key,value,updated_at) VALUES($key,$value,$at) ON CONFLICT(key) DO NOTHING;";
        command.Parameters.AddWithValue("$key", CanonicalSqliteOriginalStoreOwner.IdentityKey);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(metadata));
        command.Parameters.AddWithValue("$at", metadata.CreatedAtUtc.ToString("O"));
        // Preserve every existing identity byte, including a corrupt/unsupported row for
        // recovery. Successful initialization never silently repairs or rekeys that row.
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
