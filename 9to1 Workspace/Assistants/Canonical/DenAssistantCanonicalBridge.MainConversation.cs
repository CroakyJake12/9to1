using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using NineToOne.Dulche.Den;
using MembershipMetadata = HavenOS.Apps.Assistants.Canonical.AssistantCanonicalMembershipSource.MembershipMetadata;

namespace HavenOS.Apps.Assistants.Canonical;

/// <summary>Same Den membership and canonical conversation owner; no Main store, secondary
/// chat engine or inferred runtime identity. Candidate IDs confer no ownership or existence.</summary>
public sealed partial class DenAssistantCanonicalBridge : IAssistantMainConversationOwner
{
    public Task<AssistantConversationBinding> OpenMainConversationAsync(AssistantIdentity identity, CancellationToken token = default) =>
        _originals.Admit(async () =>
        {
            var home = await OpenHomeAsync(token).ConfigureAwait(false);
            var definition = await DefinitionAsync(home, identity, token).ConfigureAwait(false);
            if (Snapshot(home, definition).Kind != ConfiguredIdentityKind.Assistant)
                throw new AssistantCommandRefusedException("Only a real configured Assistant has this protected Main owner.");
            // Existing create-only reservation/publication validates the complete original
            // operation/membership, refuses foreign collisions and NEVER replays pending writes.
            var binding = await CreateConversationOriginalAsync(identity, definition.Revision,
                MainIdentity(identity, "conversation"), "Main", MainIdentity(identity, "operation"), token,
                AssistantConversationKind.Chat, null, originalMain: true).ConfigureAwait(false);
            var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
            if (current.Conversation.IsArchived)
                throw new AssistantCommandRefusedException("Main is archived outside its supported owner. Preserve it and use the original restore path; no replacement was created.");
            return current;
        });

    public Task DemandConversationMayBeRemovedAsync(AssistantConversationBinding binding, CancellationToken token = default) =>
        _originals.Admit(async () =>
        {
            var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
            var home = await OpenHomeAsync(token).ConfigureAwait(false);
            var session = await _originals.Source(() => home.Den.GetAsync<SessionRecord>(current.Definition.Identity.NamespaceId,
                SessionId(current.Conversation.Id), token)).ConfigureAwait(false)
                ?? throw new AssistantCommandRefusedException("The current original conversation membership is unavailable.");
            var membership = ReadMetadata<MembershipMetadata>(session, SessionKey);
            if (membership is null || !MatchesMembership(current.Conversation, membership))
                throw new AssistantCommandRefusedException("The current original conversation membership changed.");
            if (current.Conversation.Id == MainIdentity(current.Definition.Identity, "conversation"))
            {
                if (!IsOriginalMainMembership(current.Definition.Identity, session, membership, current.Conversation))
                    throw new AssistantCommandRefusedException("This reserved Main identity has changed membership and needs original inspection; it cannot be removed as an ordinary thread.");
                throw new AssistantCommandRefusedException("The Assistant's protected Main cannot be deleted or archived as an ordinary thread. Its identity and history are preserved.");
            }
            await ValidateBindingAsync(binding, token).ConfigureAwait(false);
            return true; // Read guard only; no deletion, archive, write or execution authority.
        });

    private static bool IsOriginalMainMembership(AssistantIdentity identity, SessionRecord session,
        MembershipMetadata membership, Conversation conversation) =>
        conversation.Id == MainIdentity(identity, "conversation") && session.Id == SessionId(conversation.Id) &&
        session.ConversationId == conversation.Id.ToString("D") && membership.Schema == 1 && membership.Publication == "ready" &&
        membership.DefinitionId == identity.DefinitionId && membership.CreationOperation == MainIdentity(identity, "operation") &&
        membership.Kind == AssistantConversationKind.Chat && membership.OriginalProjectReference is null &&
        membership.OriginalStudioConversationId is null && membership.OriginalStudioContainerId is null &&
        membership.OriginalTaskContainer is null && membership.OriginalStudioConversation is null &&
        membership.OriginalStudioContainer is null && membership.OriginalCreationIntentSha256 is null && membership.OriginalStoreIdentity is null &&
        conversation.Mode == HavenMode.Chat && conversation.Kind == ConversationKind.Chat && MatchesMembership(conversation, membership);

    private static Guid MainIdentity(AssistantIdentity identity, string purpose)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrWhiteSpace(identity.DenId) || string.IsNullOrWhiteSpace(identity.NamespaceId) || string.IsNullOrWhiteSpace(identity.DefinitionId))
            throw new AssistantCommandRefusedException("Use the actual complete canonical Assistant identity.");
        // Deterministic address candidates remain stable across names/configuration/restarts.
        // Only real acknowledged Den+conversation publication establishes their existence.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]
            { "9to1.assistants.main.v1", purpose, identity.DenId, identity.NamespaceId, identity.DefinitionId })));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
