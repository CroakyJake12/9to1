using MembershipMetadata = HavenOS.Apps.Assistants.Canonical.AssistantCanonicalMembershipSource.MembershipMetadata;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantCanonicalBridge
{
    private async Task<AssistantConversationBinding> CreateStudioProjectConversationOriginalAsync(
        DenAssistantOriginalDevelopmentOwner actualOwner, AssistantIdentity identity, long expectedDefinitionRevision,
        Guid conversationId, string title, Guid operationId, AssistantOriginalProjectChoice actualChoice, CancellationToken token)
    {
        if (conversationId == Guid.Empty || operationId == Guid.Empty)
            throw new AssistantCommandRefusedException("New canonical conversation and creation operation identities are required.");
        var home = await OpenHomeAsync(token).ConfigureAwait(false);
        var definition = await DefinitionAsync(home, identity, token).ConfigureAwait(false);
        if (definition.Revision != expectedDefinitionRevision)
            throw new DenException(DenErrorCode.Conflict, "The definition changed before compatible Tasks context creation.", recoverable: true);
        var sessionId = SessionId(conversationId);
        var prior = await _originals.Source(() => home.Den.GetAsync<SessionRecord>(identity.NamespaceId, sessionId, token)).ConfigureAwait(false);
        if (prior is not null)
        {
            var metadata = ReadMetadata<MembershipMetadata>(prior, SessionKey);
            if (metadata is null || metadata.CreationOperation != operationId || metadata.DefinitionId != identity.DefinitionId ||
                metadata.Kind != AssistantConversationKind.Task || metadata.OriginalProjectReference != actualChoice.Project.Reference ||
                metadata.OriginalStudioConversationId is null || metadata.OriginalStudioContainerId is null)
                throw new AssistantCommandRefusedException("This canonical conversation identity already has another membership or creation operation.");
            if (metadata.Publication != "ready")
                throw new AssistantCompatibleTaskPublicationPendingException(conversationId, operationId, prior.Revision);
            var selected = await _originals.Source(() => actualOwner.ValidateOriginalProjectChoiceWithinSourceAsync(
                actualChoice, MembershipScope, _originals.Retain, token)).ConfigureAwait(false);
            if (selected.Actor != home.Actor || selected.Context.Id != metadata.OriginalStudioConversationId ||
                selected.Container.Id != metadata.OriginalStudioContainerId || selected.Container.Mode != HavenMode.Studio)
                throw new AssistantCommandRefusedException("The actual source-selected Studio project differs from the saved compatible membership.");
            return await BindAsync(home, identity, prior, token).ConfigureAwait(false);
        }

        SessionRecord? pending = null;
        MembershipMetadata? pendingMetadata = null;
        try
        {
            var observed = await _originals.Source(() => actualOwner.CreateOriginalCompatibleTaskContextWithinSourceAsync(
                actualChoice, conversationId, title, operationId, ReservePendingMembershipAsync,
                MembershipScope, _originals.Retain, token)).ConfigureAwait(false);
            if (pending is null || pendingMetadata is null || observed.Creation.Actor != home.Actor ||
                pendingMetadata.OriginalConversation != observed.Creation.TaskConversation ||
                pendingMetadata.OriginalProjectReference != observed.Project.Reference)
                throw new InvalidOperationException("The exact pending Den membership and actual compatible Tasks receipt differ.");
            // Canonical SQL pair and manual WRITE completed, and finite project READs
            // independently drained. Only then publish the SAME pending Den session.
            var currentHome = await OpenHomeAsync(token).ConfigureAwait(false);
            var currentDefinition = await DefinitionAsync(currentHome, identity, token).ConfigureAwait(false);
            if (currentHome.Actor != observed.Creation.Actor || currentDefinition.Revision != expectedDefinitionRevision)
                throw new DenException(DenErrorCode.Conflict, "Home identity changed after compatible SQL creation; preserve the pending original for inspection.", recoverable: true);
            var ready = pending with { ExtensionData = WriteMetadata(pending.ExtensionData, SessionKey,
                pendingMetadata with { Publication = "ready" }) };
            ready = await _originals.Source(() => currentHome.Den.SaveAsync(ready, pending.Revision,
                Operation(operationId, "conversation.publish"), token)).ConfigureAwait(false);
            return await BindAsync(currentHome, identity, ready, token).ConfigureAwait(false);
        }
        catch (Exception cause) when (pending is not null)
        {
            // SAME actual Den reserve was acknowledged. A known child SQL refusal
            // cannot certify that this enclosing operation had no durable effect.
            throw new AssistantCompatibleTaskPublicationPendingException(conversationId, operationId, pending.Revision, cause);
        }

        async Task ReservePendingMembershipAsync(ICanonicalProjectTaskContextCreationIntent intent)
        {
            var current = await OpenHomeAsync(token).ConfigureAwait(false);
            var row = await DefinitionAsync(current, identity, token).ConfigureAwait(false);
            if (current.Actor != intent.Actor || row.Revision != expectedDefinitionRevision || pending is not null ||
                intent.TaskConversation.Id != conversationId || intent.OperationId != operationId)
                throw new AssistantCommandRefusedException("The actual compatible-context actor/definition/creation original changed before reservation.");
            pendingMetadata = new MembershipMetadata(1, identity.DefinitionId, operationId, AssistantConversationKind.Task,
                "pending", intent.TaskConversation, actualChoice.Project.Reference,
                intent.OriginalStudioConversation.Id, intent.OriginalStudioContainer.Id,
                intent.TaskContainer, intent.OriginalStudioConversation, intent.OriginalStudioContainer,
                actualOwner.GetOriginalCompatibleTaskIntentDigest(intent), intent.OriginalStoreObservation.OriginalStoreIdentity);
            var once = new SessionRecord { Id = sessionId, NamespaceId = identity.NamespaceId,
                ConversationId = conversationId.ToString("D"), ExtensionData = WriteMetadata(null, SessionKey, pendingMetadata) };
            pending = await _originals.Source(() => current.Den.SaveAsync(once, 0,
                Operation(operationId, "conversation.reserve"), token)).ConfigureAwait(false);
            var after = await DefinitionAsync(current, identity, token).ConfigureAwait(false);
            if (after.Revision != expectedDefinitionRevision)
                throw new DenException(DenErrorCode.Conflict, "The definition changed after pending reservation; canonical WRITE remains unentered.", recoverable: true);
        }
    }
}
