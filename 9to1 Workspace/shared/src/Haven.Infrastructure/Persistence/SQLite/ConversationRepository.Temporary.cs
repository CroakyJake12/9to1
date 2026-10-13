using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class ConversationRepository : ITemporaryConversationSaveRepository
{
    public Task<Conversation> SaveTemporaryLocallyAsync(Conversation temporary,
        IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken) =>
        SaveTemporaryLocallyAsync(temporary, messages, [], cancellationToken);

    public async Task<Conversation> SaveTemporaryLocallyAsync(Conversation temporary,
        IReadOnlyList<ChatMessage> messages, IReadOnlyList<ConversationContextEntry> contextEntries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(temporary);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(contextEntries);
        if (temporary.Id == Guid.Empty || !temporary.IsTemporary || temporary.IsArchived)
            throw new ArgumentException("An active temporary conversation with its original identity is required.", nameof(temporary));
        if (temporary.Kind is ConversationKind.Call or ConversationKind.AutomationRun or ConversationKind.Training or ConversationKind.Task)
            throw new InvalidOperationException("System-managed conversations cannot be converted through a temporary chat save.");

        // Snapshot before any await: caller mutation cannot replace a message mid-commit.
        var history = messages.ToArray();
        var context = contextEntries.ToArray();
        if (history.Any(message => message is null || message.Id == Guid.Empty || message.ConversationId != temporary.Id)
            || history.Select(message => message.Id).Distinct().Count() != history.Length)
            throw new ArgumentException("Messages must have unique original IDs and belong to this conversation.", nameof(messages));
        if (context.Any(entry => entry is null || entry.Id == Guid.Empty || entry.ConversationId != temporary.Id)
            || context.Select(entry => entry.Id).Distinct().Count() != context.Length)
            throw new ArgumentException("Context entries must have unique original IDs and belong to this conversation.", nameof(contextEntries));
        // Retain the original timestamps as well as IDs. An uncertain acknowledgement can
        // be reconciled by retrying this exact value, rather than inventing a new revision.
        var durable = temporary with { IsTemporary = false };
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = "SELECT * FROM conversations WHERE id=$id;";
            existing.Parameters.AddWithValue("$id", temporary.Id.ToString());
            var rows = await ReadConversationsAsync(existing, cancellationToken).ConfigureAwait(false);
            if (rows.Count != 0)
            {
                if (rows.Count != 1 || rows[0] != durable)
                    throw new InvalidOperationException("This conversation ID already belongs to a different saved state.");
                await using var readHistory = connection.CreateCommand();
                readHistory.Transaction = transaction;
                readHistory.CommandText = "SELECT * FROM messages WHERE conversation_id=$id ORDER BY created_at;";
                readHistory.Parameters.AddWithValue("$id", temporary.Id.ToString());
                var saved = await ReadMessagesFromReaderAsync(readHistory, cancellationToken).ConfigureAwait(false);
                var byId = history.ToDictionary(message => message.Id);
                if (saved.Count != history.Length || saved.Any(message => !byId.TryGetValue(message.Id, out var expected) || message != expected))
                    throw new InvalidOperationException("The saved history differs from this temporary conversation; it was not overwritten.");
                // Current branch versions can change without replacing the original raw
                // message rows. An uncertain save must not acknowledge those stale rows
                // as though the visible saved history still matched this session.
                var currentHistory = await ReadMessagesCoreAsync(connection, transaction, temporary.Id, true, cancellationToken).ConfigureAwait(false);
                if (!currentHistory.SequenceEqual(history))
                    throw new InvalidOperationException("The saved branch has changed since this temporary history was saved; it was not overwritten.");
                var savedContext = await GetContextEntriesCoreAsync(connection, transaction, temporary.Id, cancellationToken).ConfigureAwait(false);
                var contextById = context.ToDictionary(entry => entry.Id);
                if (savedContext.Count != context.Length || savedContext.Any(entry => !contextById.TryGetValue(entry.Id, out var expected) || entry != expected))
                    throw new InvalidOperationException("The saved context differs from this temporary conversation; it was not overwritten.");
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return rows[0];
            }
        }

        foreach (var message in history)
        {
            await using var collision = connection.CreateCommand();
            collision.Transaction = transaction;
            collision.CommandText = "SELECT 1 FROM messages WHERE id=$id LIMIT 1;";
            collision.Parameters.AddWithValue("$id", message.Id.ToString());
            if (await collision.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                throw new InvalidOperationException("A message ID already belongs to saved history; it was not replaced.");
        }
        foreach (var entry in context)
        {
            await using var collision = connection.CreateCommand();
            collision.Transaction = transaction;
            collision.CommandText = "SELECT 1 FROM conversation_context WHERE id=$id LIMIT 1;";
            collision.Parameters.AddWithValue("$id", entry.Id.ToString());
            if (await collision.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                throw new InvalidOperationException("A context ID already belongs to saved history; it was not replaced.");
        }
        await UpsertConversationCoreAsync(connection, transaction, durable, cancellationToken).ConfigureAwait(false);
        foreach (var message in history)
            await AddMessageCoreAsync(connection, transaction, message, cancellationToken).ConfigureAwait(false);
        foreach (var entry in context)
            await AddContextEntryCoreAsync(connection, transaction, entry, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return durable;
    }
}
