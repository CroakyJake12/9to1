using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed partial class ConversationProductionRepositoryTests
{
    [Fact]
    public async Task Temporary_save_retry_refuses_reordered_branch_without_overwriting_it()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ConversationRepository(database);
        var production = CreateProduction(database, repository);
        var temporary = ConversationAt(DateTimeOffset.UtcNow) with { IsTemporary = true };
        var first = new ChatMessage(Guid.NewGuid(), temporary.Id, MessageRole.User, "First", null, null, null, temporary.CreatedAt);
        var second = new ChatMessage(Guid.NewGuid(), temporary.Id, MessageRole.Assistant, "Second", null, null, null, temporary.CreatedAt.AddSeconds(1));
        await repository.SaveTemporaryLocallyAsync(temporary, [first, second], default);
        var branch = await production.EnsureRootBranchAsync(temporary.Id, default);
        await using (var connection = await database.OpenAsync(default))
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await using var reorder = connection.CreateCommand();
            reorder.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            // Model another writer changing only the branch sequence. Preserve every
            // message/version field and avoid collisions with the unique sequence index.
            reorder.CommandText = "UPDATE conversation_branch_messages SET sequence=sequence+100 WHERE branch_id=$branch;";
            reorder.Parameters.AddWithValue("$branch", branch.Id.ToString());
            await reorder.ExecuteNonQueryAsync();
            reorder.CommandText = "UPDATE conversation_branch_messages SET sequence=CASE WHEN message_id=$first THEN 2 ELSE 1 END WHERE branch_id=$branch;";
            reorder.Parameters.AddWithValue("$first", first.Id.ToString());
            await reorder.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        Assert.Equal(new[] { second, first }, await repository.GetMessagesAsync(temporary.Id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveTemporaryLocallyAsync(temporary, [first, second], default));
        Assert.Equal(new[] { second, first }, await repository.GetMessagesAsync(temporary.Id, default));
    }

    [Fact]
    public async Task Temporary_save_retry_refuses_newer_branch_versions_and_preserves_them()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ConversationRepository(database);
        var production = CreateProduction(database, repository);
        var versioning = new ConversationVersioningService(repository, production);
        var temporary = ConversationAt(DateTimeOffset.UtcNow) with { IsTemporary = true };
        var message = new ChatMessage(Guid.NewGuid(), temporary.Id, MessageRole.User, "Original", null, null, null, temporary.CreatedAt);
        await repository.SaveTemporaryLocallyAsync(temporary, [message], default);
        await versioning.EditUserMessageAsync(temporary.Id, message.Id, "Newer branch edit", MessageEditMode.NewBranch, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveTemporaryLocallyAsync(temporary, [message], default));
        Assert.Equal("Newer branch edit", Assert.Single(await repository.GetMessagesAsync(temporary.Id, default)).Content);
    }

    [Fact]
    public async Task Temporary_save_keeps_compaction_summary_and_rejects_different_context_on_retry()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ConversationRepository(database);
        var temporary = ConversationAt(DateTimeOffset.UtcNow) with { IsTemporary = true, CompactedAt = DateTimeOffset.UtcNow };
        var message = new ChatMessage(Guid.NewGuid(), temporary.Id, MessageRole.User, "Original requirement", null, null, null, temporary.CreatedAt, true);
        var summary = new ConversationContextEntry(Guid.NewGuid(), temporary.Id, ContextEntryKind.CompactSummary,
            "Session summary", "The original requirement must be retained.", "Original local compaction", temporary.UpdatedAt);
        var saved = await repository.SaveTemporaryLocallyAsync(temporary, [message], [summary], default);
        var reopened = new ConversationRepository(database);
        Assert.Equal(saved, await reopened.GetAsync(temporary.Id, default));
        Assert.Equal(message, Assert.Single(await reopened.GetMessagesAsync(temporary.Id, default)));
        Assert.Empty(await reopened.GetContextMessagesAsync(temporary.Id, default));
        Assert.Equal(summary, Assert.Single(await reopened.GetContextEntriesAsync(temporary.Id, default)));
        Assert.Equal(saved, await reopened.SaveTemporaryLocallyAsync(temporary, [message], [summary], default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reopened.SaveTemporaryLocallyAsync(temporary, [message], [summary with { Content = "Different context" }], default));
        Assert.Equal(summary, Assert.Single(await reopened.GetContextEntriesAsync(temporary.Id, default)));
    }

    [Fact]
    public async Task Temporary_save_rolls_back_history_if_context_cannot_commit()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ConversationRepository(database);
        var temporary = ConversationAt(DateTimeOffset.UtcNow) with { IsTemporary = true };
        var message = new ChatMessage(Guid.NewGuid(), temporary.Id, MessageRole.User, "Original", null, null, null, temporary.CreatedAt);
        var context = new ConversationContextEntry(Guid.NewGuid(), temporary.Id, ContextEntryKind.CompactSummary,
            "Summary", "Original context", "", temporary.CreatedAt);
        await using (var connection = await database.OpenAsync(default))
        {
            await using var trigger = connection.CreateCommand();
            trigger.CommandText = "CREATE TRIGGER fail_temporary_context BEFORE INSERT ON conversation_context BEGIN SELECT RAISE(ABORT, 'controlled context failure'); END;";
            await trigger.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            repository.SaveTemporaryLocallyAsync(temporary, [message], [context], default));
        Assert.Null(await repository.GetAsync(temporary.Id, default));
        Assert.Empty(await repository.GetMessagesAsync(temporary.Id, default));
        Assert.Empty(await repository.GetContextEntriesAsync(temporary.Id, default));
    }

    [Fact]
    public async Task Temporary_save_reopens_same_ids_history_and_branch_without_replaying_turns()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ConversationRepository(database);
        var temporary = ConversationAt(DateTimeOffset.UtcNow) with { IsTemporary = true, SpaceId = SpaceRegistry.ChatSpaceId };
        var user = new ChatMessage(Guid.NewGuid(), temporary.Id, MessageRole.User, "Original question", null, null, null, temporary.CreatedAt);
        var reply = new ChatMessage(Guid.NewGuid(), temporary.Id, MessageRole.Assistant, "Original answer", "specialist", "configured-model",
            "{\"provenance\":\"original-turn\"}", temporary.CreatedAt.AddSeconds(1));
        var saved = await repository.SaveTemporaryLocallyAsync(temporary, [user, reply], default);

        var reopened = new ConversationRepository(database);
        Assert.Equal(temporary with { IsTemporary = false }, saved);
        Assert.Equal(saved, await reopened.GetAsync(temporary.Id, default));
        Assert.Equal(new[] { user, reply }, await reopened.GetMessagesAsync(temporary.Id, default));
        Assert.Equal(saved, await reopened.SaveTemporaryLocallyAsync(temporary, [user, reply], default));
        Assert.Equal(2, await CountAsync("messages"));
        Assert.Equal(2, await CountAsync("message_versions"));
        Assert.Equal(1, await CountAsync("conversation_branches"));
        Assert.Equal(1, await CountAsync("conversation_turns"));

        async Task<long> CountAsync(string table)
        {
            await using var connection = await database.OpenAsync(default);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM " + table + ";";
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
    }

    [Fact]
    public async Task Temporary_save_rolls_back_all_history_when_a_later_message_write_fails()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ConversationRepository(database);
        var temporary = ConversationAt(DateTimeOffset.UtcNow) with { IsTemporary = true };
        var first = new ChatMessage(Guid.NewGuid(), temporary.Id, MessageRole.User, "Retain this in memory", null, null, null, temporary.CreatedAt);
        var second = new ChatMessage(Guid.NewGuid(), temporary.Id, MessageRole.Assistant, "The second insert fails", null, null, null, temporary.CreatedAt.AddSeconds(1));
        await using (var connection = await database.OpenAsync(default))
        {
            await using var trigger = connection.CreateCommand();
            trigger.CommandText = $"CREATE TRIGGER fail_temporary_insert BEFORE INSERT ON messages WHEN NEW.id='{second.Id}' BEGIN SELECT RAISE(ABORT, 'controlled save failure'); END;";
            await trigger.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            repository.SaveTemporaryLocallyAsync(temporary, [first, second], default));
        Assert.Null(await repository.GetAsync(temporary.Id, default));
        Assert.Empty(await repository.GetMessagesAsync(temporary.Id, default));
        await using (var connection = await database.OpenAsync(default))
        {
            await using var check = connection.CreateCommand();
            check.CommandText = "SELECT (SELECT COUNT(*) FROM conversation_branches) + (SELECT COUNT(*) FROM message_versions) + (SELECT COUNT(*) FROM conversation_turns);";
            Assert.Equal(0L, Convert.ToInt64(await check.ExecuteScalarAsync()));
            check.CommandText = "DROP TRIGGER fail_temporary_insert;";
            await check.ExecuteNonQueryAsync();
        }
        await repository.SaveTemporaryLocallyAsync(temporary, [first, second], default);
        Assert.Equal(new[] { first, second }, await new ConversationRepository(database).GetMessagesAsync(temporary.Id, default));
    }

    [Fact]
    public async Task Temporary_save_refuses_existing_conversation_or_foreign_message_identity()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ConversationRepository(database);
        var existing = ConversationAt(DateTimeOffset.UtcNow);
        await repository.UpsertConversationAsync(existing, default);
        var originalMessage = new ChatMessage(Guid.NewGuid(), existing.Id, MessageRole.User, "Existing content", null, null, null, existing.CreatedAt);
        await repository.AddMessageAsync(originalMessage, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveTemporaryLocallyAsync(
            existing with { IsTemporary = true, Title = "Overwritten" }, [], default));

        var temporary = ConversationAt(existing.CreatedAt) with { IsTemporary = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveTemporaryLocallyAsync(
            temporary, [originalMessage with { ConversationId = temporary.Id }], default));
        Assert.Null(await repository.GetAsync(temporary.Id, default));
        Assert.Equal(existing, await repository.GetAsync(existing.Id, default));
        Assert.Equal(originalMessage, Assert.Single(await repository.GetMessagesAsync(existing.Id, default)));
    }

    [Fact]
    public async Task Temporary_save_retry_refuses_changed_history_and_invalid_membership()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ConversationRepository(database);
        var temporary = ConversationAt(DateTimeOffset.UtcNow) with { IsTemporary = true };
        var message = new ChatMessage(Guid.NewGuid(), temporary.Id, MessageRole.User, "Original", null, null, null, temporary.CreatedAt);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveTemporaryLocallyAsync(temporary, [message, message], default));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveTemporaryLocallyAsync(temporary, [message with { ConversationId = Guid.NewGuid() }], default));
        Assert.Null(await repository.GetAsync(temporary.Id, default));
        await repository.SaveTemporaryLocallyAsync(temporary, [message], default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveTemporaryLocallyAsync(temporary, [message with { Content = "Changed" }], default));
        Assert.Equal(message, Assert.Single(await repository.GetMessagesAsync(temporary.Id, default)));
    }
}
