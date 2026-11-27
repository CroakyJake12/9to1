using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class ConversationRepository : IConversationCompactionRepository
{
    public bool SupportsCompactionSummaries => true;

    public async Task<ConversationCompactionReceipt> CommitCompactionAsync(Guid conversationId,
        ConversationCompaction compaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compaction);
        var admitted = compaction.Snapshot(conversationId);
        cancellationToken.ThrowIfCancellationRequested();
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var stored = await ReadTemporarySavePlaceholderAsync(connection, transaction, conversationId, cancellationToken).ConfigureAwait(false);
        if (stored is null || stored.IsTemporary || stored.IsArchived)
            throw new InvalidOperationException("The original saved conversation is unavailable for compaction.");
        var branchId = await GetCurrentBranchIdAsync(connection, transaction, conversationId, cancellationToken).ConfigureAwait(false);
        var context = await ReadPromptContextWithinTransactionAsync(connection, transaction, conversationId, branchId, cancellationToken).ConfigureAwait(false);
        var byId = context.Messages.ToDictionary(message => message.Id);
        var versions = context.SourceVersions.ToDictionary(version => version.MessageId, version => version.VersionId);
        if (admitted.SourceVersions is null || admitted.SourceBranchId != branchId ||
            admitted.SourceVersions.Any(version => !versions.TryGetValue(version.MessageId, out var actual) || actual != version.VersionId) ||
            admitted.OriginalMessages.Any(message => !byId.TryGetValue(message.Id, out var current) || current != message))
            throw new InvalidOperationException("The original compaction branch, messages or effective versions changed. Their prompt context was retained.");

        // Strict INSERT refuses a duplicate or foreign summary identity instead of replacing it.
        await InsertCompactionSummaryAsync(connection, transaction, admitted.Summary, cancellationToken).ConfigureAwait(false);
        await InsertCompactionCoverageAsync(connection, transaction, admitted, cancellationToken).ConfigureAwait(false);
        foreach (var original in admitted.OriginalMessages)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE messages SET is_compacted=1 WHERE id=$id AND conversation_id=$conversation;";
            command.Parameters.AddWithValue("$id", original.Id.ToString());
            command.Parameters.AddWithValue("$conversation", conversationId.ToString());
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The original compaction message could not be retained with its summary.");
        }
        var now = DateTimeOffset.UtcNow;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE conversations SET compacted_at=$at,updated_at=$at WHERE id=$id AND is_temporary=0 AND is_archived=0;";
            command.Parameters.AddWithValue("$at", now.ToString("O"));
            command.Parameters.AddWithValue("$id", conversationId.ToString());
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The original conversation changed before compaction could commit.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return new(conversationId, admitted.Summary,
            Array.AsReadOnly(admitted.OriginalMessages.Select(message => message.Id).ToArray()), now);
    }

    private static async Task InsertCompactionSummaryAsync(SqliteConnection connection, SqliteTransaction transaction,
        ConversationContextEntry entry, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO conversation_context(id,conversation_id,kind,title,content,evidence,created_at)
            VALUES($id,$conversation,$kind,$title,$content,$evidence,$created);
            """;
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$conversation", entry.ConversationId.ToString());
        command.Parameters.AddWithValue("$kind", (int)entry.Kind);
        command.Parameters.AddWithValue("$title", entry.Title);
        command.Parameters.AddWithValue("$content", entry.Content);
        command.Parameters.AddWithValue("$evidence", entry.Evidence);
        command.Parameters.AddWithValue("$created", entry.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ConversationCompaction[] SnapshotTemporaryCompactions(Guid conversationId,
        IReadOnlyList<ChatMessage> history, IReadOnlyList<ConversationCompaction> compactions)
    {
        ArgumentNullException.ThrowIfNull(compactions);
        var snapshots = compactions.Select(compaction =>
            (compaction ?? throw new ArgumentException("A retained compaction is missing.")).Snapshot(conversationId)).ToArray();
        var summaries = new HashSet<Guid>();
        var covered = new HashSet<Guid>();
        var byId = history.ToDictionary(message => message.Id);
        foreach (var compaction in snapshots)
        {
            if (compaction.SourceBranchId is not null || compaction.SourceVersions is not null)
                throw new ArgumentException("A Temporary replacement cannot borrow persisted branch coverage.");
            if (!summaries.Add(compaction.Summary.Id))
                throw new ArgumentException("A Temporary summary identity is duplicated.");
            foreach (var original in compaction.OriginalMessages)
                if (!covered.Add(original.Id) || !byId.TryGetValue(original.Id, out var saved) ||
                    saved != (original with { IsCompacted = true }))
                    throw new ArgumentException("A Temporary compaction does not match its exact original history.");
        }
        if (!covered.SetEquals(history.Where(message => message.IsCompacted).Select(message => message.Id)))
            throw new ArgumentException("Every compacted Temporary message must have its retained summary replacement.");
        return snapshots;
    }
}
