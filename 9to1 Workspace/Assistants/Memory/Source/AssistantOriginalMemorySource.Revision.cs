using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantOriginalMemorySource
{
    public bool IsOwnedOriginalPublicWriteAcknowledgment(ICanonicalAssistantMemoryWriteIntent intent,
        ICanonicalAssistantMemoryWriteAcknowledgment acknowledgment, Task<ICanonicalAssistantMemoryWriteAcknowledgment> actual) =>
        actual.IsCompletedSuccessfully && ReferenceEquals(actual.Result, acknowledgment) &&
        _writePublicTasks.TryGetValue(actual, out var attempt) && ReferenceEquals(attempt.Driver, actual) &&
        attempt.Invocation.Atomic is { } atomic && IsOwnedOriginalWriteAcknowledgment(intent, acknowledgment, atomic);

    private Task<KnowledgeLibraryService.AssistantMemoryOriginalRecord?> ReadOriginalRevisionSnapshotAsync(
        Input input, Conversation conversation, Guid id, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        RunAsync<KnowledgeLibraryService.AssistantMemoryOriginalRecord?>(scope, retain, async source =>
        {
            await ValidateMembershipAsync(input, conversation, null, source, token).ConfigureAwait(false);
            return await WithLeaseAsync<KnowledgeLibraryService.AssistantMemoryOriginalRecord?>(input.Actor, source, async lease =>
            {
                await DemandPermissionAsync(input, lease, source, token).ConfigureAwait(false);
                var result = await source.Read<KnowledgeLibraryService.AssistantMemoryOriginalRecord?>(() => _knowledge.ReadOriginalAssistantMemoryRecordAsync(lease, input.ReadScope, id, token)).ConfigureAwait(false);
                await DemandPermissionAsync(input, lease, source, token).ConfigureAwait(false);
                await ValidateMembershipAsync(input, conversation, null, source, token).ConfigureAwait(false);
                return result;
            }, token).ConfigureAwait(false);
        });

    internal Task<(ICanonicalAssistantMemoryWriteIntent? Intent, string Reason)> PrepareOriginalRevisionWithinSourceAsync(
        IChatOriginalPersistentMemoryInput sameInput, Conversation sameConversation, KnowledgeRecord sameSelectedRecord,
        CanonicalAssistantMemoryMutationKind kind, string title, string summary, Guid operationId,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        WriteSourceAsync<(ICanonicalAssistantMemoryWriteIntent?, string)>(scope, retain, async source =>
        {
            var input = DemandInput(sameInput);
            if (kind is not (CanonicalAssistantMemoryMutationKind.Correct or CanonicalAssistantMemoryMutationKind.Reject) ||
                sameConversation.Mode != HavenMode.Chat || sameConversation.Kind != ConversationKind.Chat || operationId == Guid.Empty)
                return (null, "Choose an actual memory and an explicit correction or rejection in this ordinary Assistant conversation.");
            if (kind == CanonicalAssistantMemoryMutationKind.Correct &&
                (string.IsNullOrWhiteSpace(title) || title.Length > 160 || string.IsNullOrWhiteSpace(summary) || summary.Length > 4000))
                return (null, "Provide a title of at most 160 characters and memory of at most 4,000 characters.");
            lock (_writeGate)
            {
                if (_writeOperations.TryGetValue(operationId, out var existing))
                {
                    if (ReferenceEquals(existing.Input, input) && existing.OriginalRecord is { } old &&
                        old.Id == sameSelectedRecord.Id && existing.MutationKind == kind &&
                        (kind == CanonicalAssistantMemoryMutationKind.Reject || existing.Candidate.Title == title.Trim() && existing.Candidate.Summary == summary.Trim()))
                        return (existing, "The same original revision preview is retained.");
                    return (null, "This operation already belongs to a different original preview.");
                }
                if (_writeOperations.Count >= 128) return (null, "Reopen this Assistant before preparing more memory operations.");
            }
            var original = await source.Read(() => ReadOriginalRevisionSnapshotAsync(input, sameConversation,
                sameSelectedRecord.Id, source.Run, source.Retain, token)).ConfigureAwait(false);
            if (original is null || JsonSerializer.Serialize(original.Record) != JsonSerializer.Serialize(sameSelectedRecord))
                return (null, "This memory changed, lacks its canonical index metadata, or is no longer permitted. Refresh before reviewing a change.");
            if (original.Record.Scope == "agent")
                return (null, "Preserved saved Agent memory is read-only in this continuation. Its original record and history remain unchanged.");
            var denPermission = await source.Read(() => _ownership.GetVerifiedWithinOriginalSourceAsync(
                "den", input.Binding.Definition.Identity.DenId, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
            if (denPermission?.Receipt is null || denPermission.ResourceKind != "den" || denPermission.StoreId != input.Binding.Definition.Identity.DenId ||
                denPermission.ProfileId != input.Actor.ProfileId || !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(
                    denPermission, input.Actor, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The original current Den receipt is required for this memory change.");
            var now = DateTimeOffset.UtcNow;
            // Match canonical Knowledge correction semantics while retaining old rows/index.
            // Migrate a proved legacy row's NEW correction into full Assistant scope.
            var candidate = kind == CanonicalAssistantMemoryMutationKind.Correct
                ? original.Record with { Id = Guid.NewGuid(), Title = title.Trim(), Summary = summary.Trim(), Confidence = 1,
                    CreatedAt = now, UpdatedAt = now, LastConfirmedAt = now, Status = KnowledgeRecordStatus.Corrected,
                    Origin = KnowledgeOrigin.Explicit, UserCorrection = "User correction", SupersedesId = original.Record.Id,
                    IsUserLocked = true, ExpiresAt = original.Record.Freshness == KnowledgeFreshnessClass.Durable ? original.Record.ExpiresAt : null, PrivacyClass = KnowledgePrivacyClass.Private,
                    Scope = input.ReadScope.StorageScope, AppId = KnowledgeLibraryService.AssistantMemoryApplicationId }
                : original.Record with { Status = KnowledgeRecordStatus.Rejected, UpdatedAt = now, UserCorrection = "User rejection" };
            KnowledgeLibraryService.AssistantMemoryCreatePreparation? prepared = null;
            if (kind == CanonicalAssistantMemoryMutationKind.Correct)
                prepared = await source.Read(() => _knowledge.PrepareOriginalAssistantMemoryCandidateAsync(input.ReadScope,
                    candidate, source, true, token)).ConfigureAwait(false);
            await source.Read(() => ValidateOriginalWithinSourceAsync(input, sameConversation, null, source.Run, source.Retain, token)).ConfigureAwait(false);
            var identity = input.Binding.Definition.Identity;
            var durable = new MemoryWriteReceipt(2, input.Store.StoreId, input.Actor, identity.DenId, identity.NamespaceId,
                identity.DefinitionId, input.Binding.Definition.Revision, input.Binding.DenSessionId, input.Binding.DenSessionRevision,
                sameConversation.Id, operationId, candidate, prepared?.IndexPreparation.Document ?? original.Document,
                kind, original.Record, original.Sha256, original.RawJson);
            var intent = new WriteIntent(this, input, sameConversation, denPermission, prepared, durable, original);
            lock (_writeGate)
            {
                if (_writeOperations.ContainsKey(operationId)) return (null, "This original operation was concurrently prepared. Preserve its existing preview.");
                _writeOperations.Add(operationId, intent);
            }
            return (intent, kind == CanonicalAssistantMemoryMutationKind.Correct
                ? "Correction creates a new memory and retains the superseded original and its index history. Home approval is required."
                : "Rejection stops recall of this memory and retains its record and index history. Home approval is required.");
        });
}
