using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class ConversationRepository : IAssistantConversationArchiveStore
{
    public async Task<bool> TrySetAssistantSecondaryArchiveAsync(Conversation original, bool archived, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.Id == Guid.Empty || original.IsTemporary || original.Mode != HavenMode.Chat ||
            original.Kind != ConversationKind.Chat || original.SpaceId is not null)
            return false;
        await using var connection = await factory.OpenAsync(token).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM conversations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", original.Id.ToString());
        var rows = await ReadConversationsAsync(command, token).ConfigureAwait(false);
        if (rows.Count != 1 || rows[0] != original) return false;

        if (original.IsArchived != archived)
        {
            command.CommandText = "UPDATE conversations SET is_archived=$archived WHERE id=$id;";
            command.Parameters.AddWithValue("$archived", archived ? 1 : 0);
            if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new InvalidDataException("Secondary conversation archive did not change its exact observed original.");
        }
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return true;
    }
}
