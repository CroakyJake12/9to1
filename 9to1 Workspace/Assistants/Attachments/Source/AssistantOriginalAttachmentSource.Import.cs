using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;

namespace HavenOS.Apps.Assistants.Attachments;

public sealed partial class AssistantOriginalAttachmentSource
{
    public Task<MessageAttachment> ImportOriginalAsync(AssistantConversationBinding binding, string selectedPath,
        Guid? branchId, CancellationToken token) => ChangeDraft(binding, selectedPath, null, branchId, token);
    public Task RemoveOriginalAsync(AssistantConversationBinding binding, Guid attachmentId, CancellationToken token) =>
        ChangeDraft(binding, null, attachmentId, null, token);

    private Task<MessageAttachment> ChangeDraft(AssistantConversationBinding binding, string? selectedPath,
        Guid? detachedId, Guid? branchId, CancellationToken callerToken)
    {
        callerToken.ThrowIfCancellationRequested();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<MessageAttachment>? driver = null;
        driver = _originals.Admit(async () =>
        {
            await start.Task.ConfigureAwait(false);
            // Once admitted, review/import belong to this process owner. View or
            // picker cancellation cannot cancel accepted business or waive faults.
            var token = CancellationToken.None;
            var source = _originals.CreateScope(body => body(), _ => { });
            var errors = new List<Exception>(); string? declined = null;
            ICanonicalAttachmentOriginalReadAdmission? read = null;
            ICanonicalAttachmentOriginalContentLease? content = null;
            CanonicalSqliteOriginalStoreLease? sql = null;
            MessageAttachment? result = null;
            try
            {
                var reads = _reads ?? throw new InvalidOperationException("Home attachment access is not configured.");
                if (_writes is null) throw new InvalidOperationException("Home attachment import is not configured.");
                var original = await PrepareRead(binding, branchId, source, token).ConfigureAwait(false);
                ICanonicalAttachmentOriginalSelection? selected = null;
                OriginalMessageAttachmentProcessingResult? processed = null;
                if (selectedPath is not null)
                {
                    selected = await source.Read(() => _files.ResolveOriginalPickedPathWithinSourceAsync(original.Actor,
                        selectedPath, source.Run, source.Retain, token)).ConfigureAwait(false);
                    var supported = false;
                    if (selected is not null) source.Run(() =>
                    {
                        if (!_files.IsIssuedOriginalSelection(selected)) throw new UnauthorizedAccessException("Retain the actual Files selection.");
                        supported = _processing.CanExtractOriginalLocalFileName(selected.OriginalFile.OriginalName);
                    });
                    if (selected is null) { declined = "Import this file into Files before attaching it."; }
                    else if (!supported) { declined = "Choose a registered text, source, DOCX, PPTX or XLSX file. This format is not supported here yet."; }
                    else
                    {
                        Task<ICanonicalAttachmentOriginalReadAdmission>? approval = null;
                        try { read = await source.Read(() => approval = reads.AcquireOriginalReadWithinSourceAsync(selected,
                            source.Run, source.Retain, token), actual => read = actual).ConfigureAwait(false); }
                        catch
                        {
                            var acknowledged = false;
                            if (approval is not null) source.Run(() => acknowledged = reads.IsAcknowledgedOriginalReadRefusal(approval));
                            if (!acknowledged) throw;
                            source.Run(() => source.AcknowledgeOriginalRefusalOccurrences(reads.IsAcknowledgedOriginalReadRefusal));
                            declined = "Attachment access was declined.";
                        }
                        if (declined is null)
                        {
                            content = await source.Read(() => _content.OpenOriginalContentWithinSourceAsync(selected, read!,
                                source.Run, source.Retain, token), actual => content = actual).ConfigureAwait(false);
                            processed = await source.Read(() => _processing.ProcessOriginalWithinSourceAsync(content,
                                source.Run, source.Retain, token)).ConfigureAwait(false);
                            source.Run(() =>
                            {
                                if (!_processing.IsIssuedOriginalProcessing(content, processed))
                                    throw new UnauthorizedAccessException("The configured attachment service did not issue this result.");
                            });
                        }
                    }
                }
                if (declined is null)
                {
                    await ValidateRead(original, source, token).ConfigureAwait(false);
                    sql = await source.Read(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(original.Actor,
                        false, source.Run, source.Retain, token), actual => sql = actual).ConfigureAwait(false);
                    if (detachedId is not null)
                    {
                        branchId = await source.Read(() => _production.ReadOriginalAttachmentCurrentBranchAsync(sql, original, token)).ConfigureAwait(false);
                        original = new ReadSelection(this, original.Membership, original.Binding, original.Actor,
                            original.OriginalStoreIdentity, original.Permission, original.DenPermission, branchId);
                        _readSelections.Add(original, original);
                    }
                    var snapshot = await source.Read(() => _production.ReadOriginalAttachmentDraftAsync(sql, original, token)).ConfigureAwait(false);
                    var ids = snapshot.Draft is null ? [] : JsonSerializer.Deserialize<Guid[]>(snapshot.Draft.AttachmentIdsJson)
                        ?? throw new InvalidDataException("The saved attachment selection is invalid.");
                    if (ids.Length > 64 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
                        throw new InvalidDataException("Preserve the invalid or ambiguous saved attachment selection.");
                    MessageAttachment attachment;
                    var now = DateTimeOffset.UtcNow;
                    if (detachedId is { } removing)
                    {
                        if (!ids.Contains(removing)) throw new InvalidOperationException("The attachment is not selected in this draft.");
                        attachment = await source.Read(() => _production.ReadOriginalAttachmentAsync(sql, original, removing, token)).ConfigureAwait(false)
                            ?? throw new UnauthorizedAccessException("The original draft attachment is unavailable.");
                        ids = ids.Where(id => id != removing).ToArray();
                    }
                    else
                    {
                        if (ids.Length == 64) throw new InvalidOperationException("Remove an attachment before adding another.");
                        CanonicalAttachmentFileIdentity file = null!;
                        source.Run(() => file = selected!.OriginalFile);
                        attachment = new(Guid.NewGuid(), original.OriginalConversation.Id, null, branchId, file.OriginalName,
                            string.Empty, processed!.MediaType, processed.Kind, file.SizeBytes, file.Sha256["sha256:".Length..].ToLowerInvariant(),
                            processed.State, processed.Method, processed.ExtractedText,
                            JsonSerializer.Serialize(new { schema = 1, source = "canonical.files", file.StoreId, file.FileId,
                                file.RevisionId, file.Sha256, processingNotice = processed.Notice }), now, now);
                        ids = [.. ids, attachment.Id];
                    }
                    var candidate = new ConversationDraft(original.OriginalConversation.Id, branchId,
                        snapshot.Draft?.Content ?? string.Empty, JsonSerializer.Serialize(ids), now);
                    await source.ReadCleanup(() => sql.CloseAndDrainAsync()).ConfigureAwait(false); sql = null;
                    var intent = new WriteIntent(this, original, snapshot, attachment, candidate, selected, processed, content,
                        detachedId is null ? CanonicalAttachmentDraftMutation.Import : CanonicalAttachmentDraftMutation.Detach, Guid.NewGuid());
                    _writeIntents.Add(intent, new());
                    Task<ICanonicalAttachmentImportAcknowledgment>? command = null;
                    try
                    {
                        var ack = await source.Read(() => command = CommitOriginalImportWithinSourceAsync(intent,
                            source.Run, source.Retain, token)).ConfigureAwait(false);
                        source.Run(() => { if (ack.Applied) result = ack.Attachment; else declined = ack.Reason; });
                    }
                    catch when (command is not null && IsAcknowledgedOriginalCommandRefusal(command))
                    { source.AcknowledgeOriginalRefusal(command, IsAcknowledgedOriginalCommandRefusal); declined = "Attachment import was declined."; }
                }
            }
            catch (Exception error) { errors.Add(error); }
            // The original snapshot/file/READ remain borrowed until SQL and Home
            // settlement finish. Close every captured product even on partial failure.
            if (sql is not null) try { await source.ReadCleanup(() => sql.CloseAndDrainAsync()).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            if (content is not null) try { await source.ReadCleanup(() => content.CloseAndDrainOriginalAsync()).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            if (read is not null) try { await source.ReadCleanup(() => read.CloseAndDrainOriginalAsync()).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            try { await source.JoinAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            AssistantMemoryOriginals.Throw(errors);
            if (declined is not null)
            {
                var refusal = new AssistantCommandRefusedException(declined); _refused.Add(driver!, refusal); throw refusal;
            }
            return result ?? throw new InvalidOperationException("No acknowledged attachment result was returned.");
        });
        _originals.RegisterOriginalRefusalProof(driver, IsAcknowledgedOriginalCommandRefusal);
        start.SetResult(); return driver;
    }
}
