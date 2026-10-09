using System.Text.Json;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class RetrievalIndexService
{
    // Existing and protected index writers share the exact canonical chunk bindings.
    private static void AddCanonicalChunkParameters(SqliteCommand insert, RetrievalChunk chunk)
    {
        insert.Parameters.AddWithValue("$id", chunk.Id.ToString());
        insert.Parameters.AddWithValue("$documentId", chunk.DocumentId.ToString());
        insert.Parameters.AddWithValue("$ordinal", chunk.Ordinal);
        insert.Parameters.AddWithValue("$text", chunk.Text);
        insert.Parameters.AddWithValue("$start", chunk.StartCharacter);
        insert.Parameters.AddWithValue("$length", chunk.Length);
        insert.Parameters.AddWithValue("$embedding", JsonSerializer.Serialize(chunk.Embedding, JsonOptions));
        insert.Parameters.AddWithValue("$terms", JsonSerializer.Serialize(chunk.Terms, JsonOptions));
    }
}
