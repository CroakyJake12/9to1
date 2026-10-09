using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class ConversationProductionRepository
{
    private readonly object _attachmentBindingGate = new();
    private CanonicalSqliteOriginalStoreOwner? _attachmentStore;
    private ICanonicalAttachmentImportSource? _attachmentProducer;
    private readonly ConditionalWeakTable<OriginalAttachmentDraft, ICanonicalAttachmentConversationRead> _attachmentDrafts = new();
    public sealed class OriginalAttachmentDraft
    {
        public ConversationDraft? Draft { get; }
        internal string? RawUpdatedAt { get; }
        internal OriginalAttachmentDraft(ConversationDraft? draft, string? rawUpdatedAt)
        { Draft = draft; RawUpdatedAt = rawUpdatedAt; }
    }
    public void BindOriginalAttachmentSource(CanonicalSqliteOriginalStoreOwner store, ICanonicalAttachmentImportSource producer)
    {
        ArgumentNullException.ThrowIfNull(store); ArgumentNullException.ThrowIfNull(producer);
        if (!store.HasOriginalDatabase(factory)) throw new ArgumentException("Retain the SAME conversation SQLite owner.");
        lock (_attachmentBindingGate)
        {
            if (_attachmentStore is not null && (!ReferenceEquals(_attachmentStore, store) || !ReferenceEquals(_attachmentProducer, producer)))
                throw new InvalidOperationException("The conversation attachment owner is already composed.");
            _attachmentStore = store; _attachmentProducer = producer;
        }
    }
    public bool HasOriginalAttachmentComposition(CanonicalSqliteOriginalStoreOwner store, ICanonicalAttachmentImportSource producer,
        IConversationRepository actualConversations) => ReferenceEquals(_attachmentStore, store) &&
        ReferenceEquals(_attachmentProducer, producer) && ReferenceEquals(conversations, actualConversations) && store.HasOriginalDatabase(factory);
    private void DemandAttachmentRead(CanonicalSqliteOriginalStoreLease lease, ICanonicalAttachmentConversationRead read)
    {
        if (_attachmentStore is null || _attachmentProducer is null || !ReferenceEquals(lease.OriginalOwner, _attachmentStore) ||
            !_attachmentStore.IsIssuedOriginalLease(lease) || !_attachmentProducer.IsIssuedOriginalConversationRead(read) ||
            lease.Actor != read.Actor || lease.OriginalIdentity != read.OriginalStoreIdentity)
            throw new UnauthorizedAccessException("Retain the original conversation READ and its actual protected store.");
    }
    public async Task<Guid?> ReadOriginalAttachmentCurrentBranchAsync(CanonicalSqliteOriginalStoreLease lease,
        ICanonicalAttachmentConversationRead read, CancellationToken token)
    {
        DemandAttachmentRead(lease, read); SqliteCommand? command = null; SqliteDataReader? reader = null;
        Guid? result = null; var errors = new List<Exception>();
        try
        {
            command = lease.CreateOriginalCommand(); lease.InvokeOriginalSource(() =>
            {
                command.CommandText = "SELECT id FROM conversation_branches WHERE conversation_id=$conversation AND is_current=1;";
                command.Parameters.AddWithValue("$conversation", read.OriginalConversation.Id.ToString());
            });
            reader = await lease.ReadOriginalSourceAsync(() => command.ExecuteReaderAsync(token)).ConfigureAwait(false);
            if (await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
            {
                result = lease.InvokeOriginalSource(() => Guid.Parse(reader.GetString(0)));
                if (await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
                    throw new InvalidDataException("The conversation has ambiguous current branches.");
            }
        }
        catch (Exception error) { errors.Add(error); }
        if (reader is not null) try { await lease.CloseOriginalResourceAsync(reader).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        if (command is not null) try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("The original attachment branch read failed.", errors);
        return result;
    }
    public async Task<OriginalAttachmentDraft> ReadOriginalAttachmentDraftAsync(CanonicalSqliteOriginalStoreLease lease,
        ICanonicalAttachmentConversationRead read, CancellationToken token)
    {
        DemandAttachmentRead(lease, read);
        await DemandOriginalAttachmentConversation(lease, read, token).ConfigureAwait(false);
        SqliteCommand? command = null; SqliteDataReader? reader = null;
        ConversationDraft? draft = null; string? rawTime = null; var errors = new List<Exception>();
        try
        {
            command = lease.CreateOriginalCommand();
            lease.InvokeOriginalSource(() =>
            {
                command.CommandText = "SELECT content,attachment_ids_json,updated_at FROM conversation_drafts WHERE conversation_id=$conversation AND branch_id IS $branch;";
                command.Parameters.AddWithValue("$conversation", read.OriginalConversation.Id.ToString());
                command.Parameters.AddWithValue("$branch", (object?)read.BranchId?.ToString() ?? DBNull.Value);
            });
            reader = await lease.ReadOriginalSourceAsync(() => command.ExecuteReaderAsync(token)).ConfigureAwait(false);
            if (await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
            {
                draft = lease.InvokeOriginalSource(() =>
                {
                    rawTime = reader.GetString(2);
                    return new ConversationDraft(read.OriginalConversation.Id, read.BranchId, reader.GetString(0), reader.GetString(1),
                        DateTimeOffset.Parse(rawTime, System.Globalization.CultureInfo.InvariantCulture));
                });
                if (await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
                    throw new InvalidDataException("The conversation has ambiguous draft rows; preserve them for recovery.");
            }
        }
        catch (Exception error) { errors.Add(error); }
        if (reader is not null) try { await lease.CloseOriginalResourceAsync(reader).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        if (command is not null) try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("The original conversation draft read failed.", errors);
        var result = new OriginalAttachmentDraft(draft, rawTime); _attachmentDrafts.Add(result, read); return result;
    }
    public async Task<MessageAttachment?> ReadOriginalAttachmentAsync(CanonicalSqliteOriginalStoreLease lease,
        ICanonicalAttachmentConversationRead read, Guid attachmentId, CancellationToken token)
    {
        DemandAttachmentRead(lease, read); SqliteCommand? command = null; SqliteDataReader? reader = null;
        MessageAttachment? result = null; var errors = new List<Exception>();
        try
        {
            command = lease.CreateOriginalCommand();
            lease.InvokeOriginalSource(() =>
            {
                command.CommandText = "SELECT * FROM message_attachments WHERE id=$id AND conversation_id=$conversation AND branch_id IS $branch AND message_id IS NULL;";
                command.Parameters.AddWithValue("$id", attachmentId.ToString());
                command.Parameters.AddWithValue("$conversation", read.OriginalConversation.Id.ToString());
                command.Parameters.AddWithValue("$branch", (object?)read.BranchId?.ToString() ?? DBNull.Value);
            });
            reader = await lease.ReadOriginalSourceAsync(() => command.ExecuteReaderAsync(token)).ConfigureAwait(false);
            if (await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
                result = lease.InvokeOriginalSource(() => ReadCanonicalAttachment(reader));
        }
        catch (Exception error) { errors.Add(error); }
        if (reader is not null) try { await lease.CloseOriginalResourceAsync(reader).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        if (command is not null) try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("The original attachment row read failed.", errors);
        return result;
    }
    /// <summary>Current canonical conversation scope is proved only through the
    /// actual Home-authorized original lease. Den metadata itself grants no SQL read.</summary>
    public async Task ValidateOriginalAttachmentConversationWithinLeaseAsync(CanonicalSqliteOriginalStoreLease lease,
        ICanonicalAttachmentConversationRead read, bool requireExactOriginalRow, CancellationToken token)
    {
        DemandAttachmentRead(lease, read);
        await DemandOriginalAttachmentConversation(lease, read, token).ConfigureAwait(false);
        if (!requireExactOriginalRow) return;
        SqliteCommand? command = null; SqliteDataReader? reader = null; var errors = new List<Exception>();
        try
        {
            command = lease.CreateOriginalCommand(); lease.InvokeOriginalSource(() =>
            {
                command.CommandText = "SELECT * FROM conversations WHERE id=$id LIMIT 2;";
                command.Parameters.AddWithValue("$id", read.OriginalConversation.Id.ToString());
            });
            reader = await lease.ReadOriginalSourceAsync(() => command.ExecuteReaderAsync(token)).ConfigureAwait(false);
            if (!await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false) ||
                lease.InvokeOriginalSource(() => ConversationRepository.MapOriginalDatabaseRow(reader) != read.OriginalConversation) ||
                await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The exact original attachment conversation row changed.");
        }
        catch (Exception cause) { errors.Add(cause); }
        if (reader is not null) try { await lease.CloseOriginalResourceAsync(reader).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        if (command is not null) try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("The actual protected attachment conversation validation failed.", errors);
    }
    private static async Task DemandOriginalAttachmentConversation(CanonicalSqliteOriginalStoreLease lease,
        ICanonicalAttachmentConversationRead read, CancellationToken token)
    {
        var actual = await AttachmentScalar(lease, command =>
        {
            command.CommandText = """
                SELECT COUNT(*) FROM conversations c WHERE c.id=$conversation AND c.mode=$mode AND c.kind=$kind
                AND c.is_temporary=0 AND c.is_archived=0 AND c.container_id IS $container AND c.space_id IS $space AND c.lesson_id IS $lesson
                AND ($branch IS NULL OR EXISTS(SELECT 1 FROM conversation_branches b WHERE b.conversation_id=c.id AND b.id=$branch AND b.is_current=1));
                """;
            var conversation = read.OriginalConversation;
            command.Parameters.AddWithValue("$conversation", conversation.Id.ToString());
            command.Parameters.AddWithValue("$mode", (int)conversation.Mode); command.Parameters.AddWithValue("$kind", (int)conversation.Kind);
            command.Parameters.AddWithValue("$container", (object?)conversation.ContainerId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("$space", (object?)conversation.SpaceId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("$lesson", (object?)conversation.LessonId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("$branch", (object?)read.BranchId?.ToString() ?? DBNull.Value);
        }, token).ConfigureAwait(false);
        if (Convert.ToInt64(actual, System.Globalization.CultureInfo.InvariantCulture) != 1)
            throw new UnauthorizedAccessException("The actual conversation or selected current branch changed.");
    }
    public Task<CanonicalSqliteOriginalStoreLease> AcquireOriginalAttachmentWriterAsync(ICanonicalAttachmentImportIntent intent,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        if (_attachmentStore is null || _attachmentProducer?.IsIssuedOriginalImportIntent(intent) != true)
            throw new UnauthorizedAccessException("Retain the SAME source-issued attachment import intent.");
        return _attachmentStore.AcquireOriginalProtectedWriterPinWithinSourceAsync(intent.Actor, scope, retain, token);
    }
    public void DemandOriginalAttachmentWriter(CanonicalSqliteOriginalStoreLease lease)
    {
        if (!ReferenceEquals(lease.OriginalOwner, _attachmentStore) || _attachmentStore?.IsIssuedOriginalLease(lease) != true)
            throw new UnauthorizedAccessException("Retain the actual configured attachment writer pin.");
        lease.DemandPinnedOriginalPhysical();
    }
    public async Task<bool> CommitOriginalAttachmentDraftAsync(CanonicalSqliteOriginalStoreLease lease,
        ICanonicalAttachmentImportIntent intent, ICanonicalAttachmentConversationRead read,
        OriginalAttachmentDraft originalDraft, CancellationToken token)
    {
        DemandAttachmentRead(lease, read);
        if (_attachmentProducer?.IsIssuedOriginalImportIntent(intent) != true ||
            !_attachmentDrafts.TryGetValue(originalDraft, out var originalRead) || !ReferenceEquals(originalRead, read) ||
            intent.Actor != read.Actor || intent.OriginalStoreIdentity != read.OriginalStoreIdentity ||
            intent.OriginalConversation != read.OriginalConversation || intent.CandidateDraft.BranchId != read.BranchId ||
            intent.CandidateDraft.ConversationId != read.OriginalConversation.Id || !Enum.IsDefined(intent.Mutation) ||
            intent.OriginalDraft != originalDraft.Draft || intent.Attachment.ConversationId != read.OriginalConversation.Id ||
            intent.Attachment.BranchId != read.BranchId || intent.Attachment.MessageId is not null)
            throw new UnauthorizedAccessException("The exact source-issued attachment and draft snapshot are required.");
        void Demand() { _attachmentProducer.DemandOriginalImportCommit(intent); DemandOriginalAttachmentWriter(lease); }
        lease.InvokeOriginalSource(() => { Demand(); lease.Connection.DefaultTimeout = 1; });
        await AttachmentExecute(lease, command => { Demand(); command.CommandText = "PRAGMA busy_timeout=1;"; }, token).ConfigureAwait(false);
        lease.BeginPinnedOriginalWriter(Demand);
        await DemandOriginalAttachmentConversation(lease, read, token).ConfigureAwait(false);
        var current = await ReadOriginalAttachmentDraftAsync(lease, read, token).ConfigureAwait(false);
        if (current.Draft != originalDraft.Draft || current.RawUpdatedAt != originalDraft.RawUpdatedAt)
        {
            await lease.ReadOriginalSourceAsync(() => lease.Transaction.RollbackAsync(token)).ConfigureAwait(false);
            return false;
        }
        if (intent.Mutation == CanonicalAttachmentDraftMutation.Import)
            await AttachmentExecute(lease, command =>
            {
                Demand();
                command.CommandText = """
                    INSERT INTO message_attachments(id,conversation_id,message_id,branch_id,original_name,stored_name,media_type,kind,size_bytes,sha256,processing_state,analysis_method,extracted_text,metadata_json,created_at,updated_at)
                    VALUES($id,$conversationId,$messageId,$branchId,$originalName,$storedName,$mediaType,$kind,$sizeBytes,$sha256,$processingState,$analysisMethod,$extractedText,$metadataJson,$createdAt,$updatedAt);
                    """;
                AddAttachmentParameters(command, intent.Attachment);
            }, token).ConfigureAwait(false);
        else
        {
            var same = await ReadOriginalAttachmentAsync(lease, read, intent.Attachment.Id, token).ConfigureAwait(false);
            if (same != intent.Attachment) throw new UnauthorizedAccessException("The original draft attachment changed.");
        }
        var changed = await AttachmentExecute(lease, command =>
        {
            Demand();
            command.CommandText = originalDraft.Draft is null
                ? "INSERT INTO conversation_drafts(conversation_id,branch_id,content,attachment_ids_json,updated_at) VALUES($conversation,$branch,$content,$attachments,$updated);"
                : "UPDATE conversation_drafts SET content=$content,attachment_ids_json=$attachments,updated_at=$updated WHERE conversation_id=$conversation AND branch_id IS $branch;";
            command.Parameters.AddWithValue("$conversation", intent.CandidateDraft.ConversationId.ToString());
            command.Parameters.AddWithValue("$branch", (object?)read.BranchId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("$content", intent.CandidateDraft.Content);
            command.Parameters.AddWithValue("$attachments", intent.CandidateDraft.AttachmentIdsJson);
            command.Parameters.AddWithValue("$updated", intent.CandidateDraft.UpdatedAt.ToString("O"));
        }, token).ConfigureAwait(false);
        if (changed != 1) throw new InvalidDataException("The original draft update did not affect exactly one row.");
        await AttachmentExecute(lease, command =>
        {
            Demand(); command.CommandText = "INSERT INTO settings(key,value,updated_at) VALUES($key,$value,$at);";
            command.Parameters.AddWithValue("$key", "assistant.attachment.operation.v1." + intent.OperationId.ToString("N"));
            command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(new
            { Schema = 1, intent.OperationId, intent.Mutation, intent.Actor, intent.OriginalStoreIdentity, intent.DenId,
                intent.NamespaceId, intent.DefinitionId, intent.DefinitionRevision, intent.SessionId, intent.SessionRevision,
                intent.Attachment, intent.OriginalDraft, intent.CandidateDraft }));
            command.Parameters.AddWithValue("$at", intent.CandidateDraft.UpdatedAt.ToString("O"));
        }, token).ConfigureAwait(false);
        await lease.CommitPinnedOriginalAsync(Demand, token).ConfigureAwait(false); return true;
    }
    private static async Task<int> AttachmentExecute(CanonicalSqliteOriginalStoreLease lease, Action<SqliteCommand> configure, CancellationToken token)
    {
        SqliteCommand? command = null; int result = 0; var errors = new List<Exception>();
        try { command = lease.CreateOriginalCommand(); lease.InvokeOriginalSource(() => configure(command)); result = await lease.ReadOriginalSourceAsync(() => command.ExecuteNonQueryAsync(token)).ConfigureAwait(false); }
        catch (Exception error) { errors.Add(error); }
        if (command is not null) try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("The original attachment SQL operation failed.", errors);
        return result;
    }
    private static async Task<object?> AttachmentScalar(CanonicalSqliteOriginalStoreLease lease, Action<SqliteCommand> configure, CancellationToken token)
    {
        SqliteCommand? command = null; object? result = null; var errors = new List<Exception>();
        try { command = lease.CreateOriginalCommand(); lease.InvokeOriginalSource(() => configure(command)); result = await lease.ReadOriginalSourceAsync(() => command.ExecuteScalarAsync(token)).ConfigureAwait(false); }
        catch (Exception error) { errors.Add(error); }
        if (command is not null) try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("The original attachment SQL read failed.", errors);
        return result;
    }
}
