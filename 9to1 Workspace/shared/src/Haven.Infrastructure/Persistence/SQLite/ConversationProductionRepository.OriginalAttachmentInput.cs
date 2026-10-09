using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class ConversationProductionRepository
{
    private readonly ConditionalWeakTable<OriginalAttachmentInputSnapshot, ICanonicalAttachmentConversationRead> _attachmentInputs = new();
    public sealed class OriginalAttachmentInputSnapshot
    {
        public OriginalAttachmentDraft OriginalDraft { get; }
        public IReadOnlyList<MessageAttachment> Attachments { get; }
        public IReadOnlyList<string> OriginalImportReceipts { get; }
        public string SnapshotSha256 { get; }
        internal OriginalAttachmentInputSnapshot(OriginalAttachmentDraft draft,
            MessageAttachment[] attachments, string[] receipts)
        {
            OriginalDraft = draft; Attachments = Array.AsReadOnly(attachments); OriginalImportReceipts = Array.AsReadOnly(receipts);
            SnapshotSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(new { draft.Draft, draft.RawUpdatedAt, Attachments, OriginalImportReceipts })));
        }
    }
    public bool IsIssuedOriginalAttachmentInputSnapshot(OriginalAttachmentInputSnapshot snapshot,
        ICanonicalAttachmentConversationRead read) => snapshot is not null &&
        _attachmentInputs.TryGetValue(snapshot, out var issued) && ReferenceEquals(issued, read) &&
        _attachmentProducer?.IsIssuedOriginalConversationRead(read) == true;

    public async Task<OriginalAttachmentInputSnapshot> ReadOriginalAttachmentInputAsync(
        CanonicalSqliteOriginalStoreLease lease, ICanonicalAttachmentConversationRead read,
        string prompt, IReadOnlyList<Guid> attachmentIds, CancellationToken token)
    {
        DemandAttachmentRead(lease, read);
        var ids = attachmentIds.ToArray();
        if (ids.Length is < 1 or > 64 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new UnauthorizedAccessException("Use the exact nonempty saved attachment selection.");
        if (await ReadOriginalAttachmentCurrentBranchAsync(lease, read, token).ConfigureAwait(false) != read.BranchId)
            throw new UnauthorizedAccessException("The selected saved-draft branch changed before input was accepted.");
        var draft = await ReadOriginalAttachmentDraftAsync(lease, read, token).ConfigureAwait(false);
        if (draft.Draft is not { } saved || saved.Content != prompt ||
            !ids.SequenceEqual(JsonSerializer.Deserialize<Guid[]>(saved.AttachmentIdsJson) ?? []))
            throw new UnauthorizedAccessException("Save the current prompt and exact attachment selection before sending.");
        var (attachments, receipts) = await ReadOriginalAttachmentInputRowsAsync(lease, read, ids, token).ConfigureAwait(false);
        var final = await ReadOriginalAttachmentDraftAsync(lease, read, token).ConfigureAwait(false);
        if (final.Draft != draft.Draft || final.RawUpdatedAt != draft.RawUpdatedAt)
            throw new UnauthorizedAccessException("The saved draft changed during attachment input preparation.");
        var result = new OriginalAttachmentInputSnapshot(draft, attachments, receipts);
        _attachmentInputs.Add(result, read); return result;
    }

    // The caller must first prove the SAME private Chat acceptance. This method
    // supplies protected row/history currentness, never reconstructed acceptance.
    public async Task<OriginalAttachmentInputSnapshot> ReadOriginalAcceptedAttachmentInputAsync(
        CanonicalSqliteOriginalStoreLease lease, ICanonicalAttachmentConversationRead read,
        OriginalAttachmentInputSnapshot original, ChatMessage acceptedMessage, CancellationToken token)
    {
        DemandAttachmentRead(lease, read);
        if (!IsIssuedOriginalAttachmentInputSnapshot(original, read) || acceptedMessage.Role != MessageRole.User ||
            acceptedMessage.ConversationId != read.OriginalConversation.Id || acceptedMessage.Content != original.OriginalDraft.Draft?.Content)
            throw new UnauthorizedAccessException("Use the SAME protected input snapshot and actual accepted message.");
        await DemandOriginalAttachmentConversation(lease, read, token).ConfigureAwait(false);
        var currentBranch = await ReadOriginalAttachmentCurrentBranchAsync(lease, read, token).ConfigureAwait(false);
        if (currentBranch is null || read.BranchId is { } originalBranch && currentBranch != originalBranch)
            throw new UnauthorizedAccessException("The accepted attachment message requires its unique current branch.");
        await DemandOriginalAttachmentAcceptedMessageAsync(lease, read, acceptedMessage, token).ConfigureAwait(false);
        var (attachments, receipts) = await ReadOriginalAttachmentInputRowsAsync(lease, read,
            original.Attachments.Select(item => item.Id).ToArray(), token).ConfigureAwait(false);
        var result = new OriginalAttachmentInputSnapshot(original.OriginalDraft, attachments, receipts);
        if (result.SnapshotSha256 != original.SnapshotSha256)
            throw new UnauthorizedAccessException("The accepted attachment rows or original import receipts changed.");
        _attachmentInputs.Add(result, read); return result;
    }
    private async Task<(MessageAttachment[] Attachments, string[] Receipts)> ReadOriginalAttachmentInputRowsAsync(
        CanonicalSqliteOriginalStoreLease lease, ICanonicalAttachmentConversationRead read, Guid[] ids, CancellationToken token)
    {
        var attachments = new List<MessageAttachment>(); var receipts = new List<string>();
        foreach (var id in ids)
        {
            var attachment = await ReadOriginalAttachmentAsync(lease, read, id, token).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("An exact saved draft attachment is unavailable.");
            if (attachment.ProcessingState != AttachmentProcessingState.Ready ||
                attachment.Kind is not (MessageAttachmentKind.PlainText or MessageAttachmentKind.SourceCode or MessageAttachmentKind.Word or
                    MessageAttachmentKind.Spreadsheet or MessageAttachmentKind.PowerPoint) ||
                attachment.StoredName.Length != 0 || attachment.ExtractedText.Length > 500_000)
                throw new UnauthorizedAccessException("This attachment has no supported approved text input.");
            var receipt = await ReadOriginalAttachmentImportReceipt(lease, read, attachment, token).ConfigureAwait(false);
            attachments.Add(attachment); receipts.Add(receipt);
        }
        return (attachments.ToArray(), receipts.ToArray());
    }
    private static async Task DemandOriginalAttachmentAcceptedMessageAsync(CanonicalSqliteOriginalStoreLease lease,
        ICanonicalAttachmentConversationRead read, ChatMessage expected, CancellationToken token)
    {
        SqliteCommand? command = null; SqliteDataReader? reader = null; var errors = new List<Exception>();
        try
        {
            command = lease.CreateOriginalCommand();
            lease.InvokeOriginalSource(() =>
            {
                // Require the exact current branch mapping and unchanged current
                // version. A fork/edited message cannot borrow the original text.
                command.CommandText = """
                    SELECT m.id,m.conversation_id,m.role,v.content,m.agent_name,m.model_name,
                           COALESCE(v.metadata_json,m.metadata_json) AS metadata_json,m.created_at,m.is_compacted
                    FROM conversation_branches b JOIN conversation_branch_messages bm ON bm.branch_id=b.id
                    JOIN messages m ON m.id=bm.message_id
                    JOIN message_versions v ON v.message_id=m.id AND v.branch_id=b.id AND v.is_current=1
                    WHERE b.conversation_id=$conversation AND b.is_current=1 AND m.conversation_id=$conversation AND m.id=$message
                    AND (($branch IS NOT NULL AND b.id=$branch) OR ($branch IS NULL AND b.parent_branch_id IS NULL AND b.forked_from_message_id IS NULL))
                    AND v.version_number=1 AND v.content=m.content AND v.metadata_json IS m.metadata_json LIMIT 2;
                    """;
                command.Parameters.AddWithValue("$conversation", expected.ConversationId.ToString());
                command.Parameters.AddWithValue("$message", expected.Id.ToString());
                command.Parameters.AddWithValue("$branch", (object?)read.BranchId?.ToString() ?? DBNull.Value);
            });
            reader = await lease.ReadOriginalSourceAsync(() => command.ExecuteReaderAsync(token)).ConfigureAwait(false);
            if (!await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The exact accepted attachment message is no longer current.");
            var actual = lease.InvokeOriginalSource(() => new ChatMessage(reader.Guid("id"), reader.Guid("conversation_id"),
                (MessageRole)reader.Int32("role"), reader.String("content"), reader.NullableString("agent_name"),
                reader.NullableString("model_name"), reader.NullableString("metadata_json"), reader.DateTimeOffset("created_at"), reader.Boolean("is_compacted")));
            if (actual != expected || await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The actual accepted attachment message changed or became ambiguous.");
        }
        catch (Exception cause) { errors.Add(cause); }
        if (reader is not null) try { await lease.CloseOriginalResourceAsync(reader).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        if (command is not null) try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("The actual accepted message could not be independently verified.", errors);
    }

    private static async Task<string> ReadOriginalAttachmentImportReceipt(CanonicalSqliteOriginalStoreLease lease,
        ICanonicalAttachmentConversationRead read, MessageAttachment attachment, CancellationToken token)
    {
        SqliteCommand? command = null; SqliteDataReader? reader = null;
        string? receipt = null; var errors = new List<Exception>();
        try
        {
            command = lease.CreateOriginalCommand();
            lease.InvokeOriginalSource(() =>
            {
                command.CommandText = """
                    SELECT key,value FROM settings WHERE key GLOB 'assistant.attachment.operation.v1.*'
                    AND CASE WHEN json_valid(value) THEN json_extract(value,'$.Attachment.Id') END=$id
                    AND CASE WHEN json_valid(value) THEN json_extract(value,'$.Mutation') END=0 LIMIT 2;
                    """;
                command.Parameters.AddWithValue("$id", attachment.Id.ToString());
            });
            reader = await lease.ReadOriginalSourceAsync(() => command.ExecuteReaderAsync(token)).ConfigureAwait(false);
            if (!await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The actual approved attachment import receipt is unavailable.");
            lease.InvokeOriginalSource(() =>
            {
                var key = reader.GetString(0); receipt = reader.GetString(1);
                if (receipt.Length > 2_000_000) throw new InvalidDataException("The original import receipt exceeds its input bound.");
                using var document = JsonDocument.Parse(receipt); var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() != 1) ||
                    root.GetProperty("Schema").GetInt32() != 1 ||
                    root.GetProperty("Mutation").GetInt32() != (int)CanonicalAttachmentDraftMutation.Import ||
                    key != "assistant.attachment.operation.v1." + root.GetProperty("OperationId").GetGuid().ToString("N") ||
                    root.GetProperty("OriginalStoreIdentity").Deserialize<ResourceStoreIdentity>() != read.OriginalStoreIdentity ||
                    root.GetProperty("Attachment").Deserialize<MessageAttachment>() != attachment)
                    throw new UnauthorizedAccessException("The original import receipt does not match the actual attachment row and store.");
                var accepted = root.GetProperty("CandidateDraft").Deserialize<ConversationDraft>();
                var before = root.GetProperty("OriginalDraft").Deserialize<ConversationDraft>();
                var beforeIds = before is null ? [] : JsonSerializer.Deserialize<Guid[]>(before.AttachmentIdsJson) ?? [];
                if (accepted is null || accepted.ConversationId != read.OriginalConversation.Id || accepted.BranchId != read.BranchId ||
                    before is not null && (before.ConversationId != accepted.ConversationId || before.BranchId != accepted.BranchId) ||
                    !(JsonSerializer.Deserialize<Guid[]>(accepted.AttachmentIdsJson) ?? []).SequenceEqual(beforeIds.Append(attachment.Id)))
                    throw new UnauthorizedAccessException("The original import did not establish this exact draft attachment.");
            });
            if (await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The attachment has ambiguous original import receipts.");
        }
        catch (Exception cause) { errors.Add(cause); }
        if (reader is not null) try { await lease.CloseOriginalResourceAsync(reader).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        if (command is not null) try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("The actual original attachment import receipt could not be read.", errors);
        return receipt ?? throw new InvalidOperationException("No original attachment receipt was retained.");
    }
}
