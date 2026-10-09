using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class ConversationRepository
{
    public async Task<bool> TryCreateConversationAsync(Conversation original, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.Id == Guid.Empty) throw new ArgumentException("A canonical conversation identity is required.", nameof(original));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO conversations(id, mode, kind, title, container_id, lesson_id, is_pinned, is_temporary, created_at, updated_at,is_archived,parent_conversation_id,compacted_at,space_id)
            VALUES($id,$mode,$kind,$title,$containerId,$lessonId,$isPinned,$isTemporary,$createdAt,$updatedAt,$isArchived,$parentConversationId,$compactedAt,$spaceId)
            ON CONFLICT(id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", original.Id.ToString());
        command.Parameters.AddWithValue("$mode", (int)original.Mode);
        command.Parameters.AddWithValue("$kind", (int)original.Kind);
        command.Parameters.AddWithValue("$title", original.Title);
        command.Parameters.AddWithValue("$containerId", (object?)original.ContainerId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$lessonId", (object?)original.LessonId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$isPinned", original.IsPinned ? 1 : 0);
        command.Parameters.AddWithValue("$isTemporary", original.IsTemporary ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", original.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", original.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$isArchived", original.IsArchived ? 1 : 0);
        command.Parameters.AddWithValue("$parentConversationId", (object?)original.ParentConversationId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$compactedAt", (object?)original.CompactedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$spaceId", (object?)original.SpaceId?.ToString() ?? DBNull.Value);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (changed is not (0 or 1)) throw new InvalidDataException("The original create-only write returned an unexpected affected-row count.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return changed == 1;
    }
}
