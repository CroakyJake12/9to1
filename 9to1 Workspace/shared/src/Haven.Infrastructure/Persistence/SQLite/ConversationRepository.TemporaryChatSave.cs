using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class ConversationRepository
{
    public async Task<TemporaryChatSaveReceipt> SaveTemporaryChatAsync(TemporaryChatSaveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Original);
        ArgumentNullException.ThrowIfNull(request.History);
        ArgumentNullException.ThrowIfNull(request.DraftText);
        ArgumentNullException.ThrowIfNull(request.PendingAttachmentIds);

        var original = request.Original;
        var id = original.Id;
        if (id == Guid.Empty || !original.IsTemporary || original.IsArchived ||
            original.Mode != HavenMode.Chat || original.Kind != ConversationKind.Chat)
            throw new ArgumentException("Only a started ordinary Temporary Chat can be saved here.", nameof(request));

        // Freeze the admitted payload before the first await. Do not fabricate, deduplicate or reorder history.
        var history = request.History.ToArray();
        var attachments = request.PendingAttachmentIds.ToArray();
        if (!history.Any(message => message?.Role == MessageRole.User) ||
            history.Any(message => message is null || message.Id == Guid.Empty || message.ConversationId != id) ||
            history.Select(message => message.Id).Distinct().Count() != history.Length)
            throw new ArgumentException("The started chat must supply its original, uniquely identified history.", nameof(request));
        if (attachments.Any(attachment => attachment == Guid.Empty) || attachments.Distinct().Count() != attachments.Length)
            throw new ArgumentException("Pending attachments must supply their original, unique identities.", nameof(request));
        if (request.ExpectedPlaceholder is { } expected &&
            (expected.Id != id || expected.Mode != original.Mode || expected.Kind != original.Kind ||
             expected.CreatedAt != original.CreatedAt || expected.IsArchived || expected.IsPinned != original.IsPinned ||
             expected.SpaceId != original.SpaceId || expected.ContainerId != original.ContainerId ||
             expected.LessonId != original.LessonId || expected.ParentConversationId != original.ParentConversationId ||
             expected.CompactedAt is not null))
            throw new ArgumentException("The owned placeholder must match the Temporary Chat identity and scope.", nameof(request));
        if (request.ExpectedBranchId is { } expectedBranch && (expectedBranch == Guid.Empty || request.ExpectedPlaceholder is null))
            throw new ArgumentException("The owned branch must belong to the acknowledged placeholder.", nameof(request));
        if (request.ExpectedDraft is { } draft &&
            (request.ExpectedPlaceholder is null || draft.ConversationId != id ||
             draft.BranchId is { } draftBranch && draftBranch != request.ExpectedBranchId))
            throw new ArgumentException("The owned draft must belong to the acknowledged placeholder.", nameof(request));

        cancellationToken.ThrowIfCancellationRequested();
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var stored = await ReadTemporarySavePlaceholderAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false);
        if (stored != request.ExpectedPlaceholder)
            throw new InvalidOperationException("The local conversation changed or is not the owned placeholder.");

        var branchId = await ReadEmptyTemporarySaveBranchAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false);
        if (branchId != request.ExpectedBranchId)
            throw new InvalidOperationException("The local branch changed or is not the owned branch.");
        await CheckTemporarySaveDraftAsync(connection, transaction, id, branchId, request.ExpectedDraft, cancellationToken).ConfigureAwait(false);
        await CheckTemporarySaveAttachmentsAsync(connection, transaction, id, branchId, attachments, cancellationToken).ConfigureAwait(false);

        var saved = original with { IsTemporary = false, UpdatedAt = DateTimeOffset.UtcNow };
        if (stored is null)
        {
            if (!await InsertConversationIfMissingAsync(connection, transaction, saved, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("The local conversation identity is already in use.");
        }
        else
            await UpsertConversationWithinTransactionAsync(connection, transaction, saved, cancellationToken).ConfigureAwait(false);

        var savedBranchId = branchId ?? await EnsureCurrentBranchCoreAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false);
        foreach (var message in history)
            // Bound writes use strict INSERT. A colliding message ID must never replace another chat's content.
            await AddMessageWithinTransactionAsync(connection, transaction, message, savedBranchId, true, cancellationToken).ConfigureAwait(false);

        if (request.ExpectedDraft is not null)
        {
            await using var removal = connection.CreateCommand();
            removal.Transaction = transaction;
            removal.CommandText = "DELETE FROM conversation_drafts WHERE conversation_id=$id;";
            removal.Parameters.AddWithValue("$id", id.ToString());
            if (await removal.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The local draft changed before Save.");
        }

        if (request.DraftText.Length != 0 || attachments.Length != 0)
        {
            var savedDraft = new ConversationDraft(id, savedBranchId, request.DraftText, JsonSerializer.Serialize(attachments), saved.UpdatedAt);
            await ConversationProductionRepository.InsertDraftAsync(connection, transaction, savedDraft, cancellationToken).ConfigureAwait(false);
        }

        // Cancellation is honored before commit; after crossing that boundary, acknowledge the actual result.
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return new TemporaryChatSaveReceipt(saved, savedBranchId);
    }

    private static async Task<Conversation?> ReadTemporarySavePlaceholderAsync(SqliteConnection connection,
        SqliteTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM conversations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? MapOriginalDatabaseRow(reader) : null;
    }

    private static async Task<Guid?> ReadEmptyTemporarySaveBranchAsync(SqliteConnection connection,
        SqliteTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM messages WHERE conversation_id=$id)
                OR EXISTS(SELECT 1 FROM conversation_turns WHERE conversation_id=$id)
                OR EXISTS(SELECT 1 FROM conversation_context WHERE conversation_id=$id)
                OR EXISTS(SELECT 1 FROM response_usage WHERE conversation_id=$id)
                OR EXISTS(SELECT 1 FROM conversation_branch_messages AS link
                    JOIN conversation_branches AS branch ON branch.id=link.branch_id WHERE branch.conversation_id=$id);
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
            throw new InvalidOperationException("A chat with saved history cannot be overwritten by Temporary Chat.");

        command.CommandText = "SELECT * FROM conversation_branches WHERE conversation_id=$id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var branchId = reader.Guid("id");
        if (!reader.Boolean("is_current") || reader.NullableGuid("parent_branch_id") is not null ||
            reader.NullableGuid("forked_from_message_id") is not null ||
            reader.Int32("reason") != (int)ConversationBranchReason.Root ||
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The local chat already has independent branch history.");
        return branchId;
    }

    private static async Task CheckTemporarySaveDraftAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid id, Guid? branchId, ConversationDraft? expected, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM conversation_drafts WHERE conversation_id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        ConversationDraft? actual = null;
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            actual = new ConversationDraft(id, reader.NullableGuid("branch_id"), reader.String("content"),
                reader.String("attachment_ids_json"), reader.DateTimeOffset("updated_at"));
            if (actual.BranchId is { } actualBranch && actualBranch != branchId ||
                await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("The local chat already has a separate draft.");
        }
        if (actual != expected)
            throw new InvalidOperationException("The local draft changed or is not an owned draft.");
    }

    private static async Task CheckTemporarySaveAttachmentsAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid conversationId, Guid? branchId, IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken)
    {
        // The ordinary history attachment reader exposes all rows for this conversation, not
        // just the draft's IDs. Reject unlisted rows instead of implicitly admitting them.
        var expected = attachmentIds.ToHashSet();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id,message_id,branch_id FROM message_attachments WHERE conversation_id=$conversationId;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.NullableGuid("message_id") is not null ||
                reader.NullableGuid("branch_id") is { } attachmentBranch && attachmentBranch != branchId ||
                !expected.Remove(reader.Guid("id")))
                throw new InvalidOperationException("The local chat has an attachment outside the approved pending snapshot.");
        }
        if (expected.Count != 0)
            throw new InvalidOperationException("A submitted attachment is not pending on this local chat.");
    }
}
