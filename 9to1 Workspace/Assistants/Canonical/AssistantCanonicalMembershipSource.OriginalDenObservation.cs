using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Canonical;

internal sealed partial class AssistantCanonicalMembershipSource
{
    // This observation proves only current Den membership and its Home actor.
    // A consumer must separately validate canonical rows through its actual
    // Home-authorized protected store lease before reading content.
    internal sealed record OriginalDenObservation(AuthenticatedResourceActor Actor,
        AssistantDefinitionSnapshot Definition, Conversation Conversation);

    internal async Task<OriginalDenObservation> ValidateOriginalDenWithinSourceAsync(
        AssistantConversationBinding binding, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        await Take(() =>
        {
            if (!IsIssuedOriginalBinding(binding))
                throw new AssistantCommandRefusedException("Only this actual Den source's privately issued binding is accepted.");
            return Task.FromResult(true);
        }, scope, retain).ConfigureAwait(false);
        var home = await OpenHomeWithinSourceAsync(scope, retain, token).ConfigureAwait(false);
        var definition = await DefinitionWithinSourceAsync(home, binding.Definition.Identity, scope, retain, token).ConfigureAwait(false);
        var session = await Take(() => home.Den.GetAsync<SessionRecord>(binding.Definition.Identity.NamespaceId,
            binding.DenSessionId, token), scope, retain).ConfigureAwait(false);
        var observed = await Take(() =>
        {
            var metadata = session is null ? null : ReadMetadata<MembershipMetadata>(session, SessionKey);
            if (session is null || metadata is not { Schema: 1, Publication: "ready" } ||
                definition.Revision != binding.Definition.Revision || session.Revision != binding.DenSessionRevision ||
                metadata.DefinitionId != binding.Definition.Identity.DefinitionId ||
                session.NamespaceId != binding.Definition.Identity.NamespaceId ||
                session.Id != SessionId(binding.Conversation.Id) ||
                !Guid.TryParse(session.ConversationId, out var id) || id != binding.Conversation.Id ||
                id != metadata.OriginalConversation.Id || !MatchesMembership(binding.Conversation, metadata))
                throw new AssistantCommandRefusedException("The exact current Den definition/session membership changed before protected canonical READ.");
            return Task.FromResult(new OriginalDenObservation(home.Actor, Snapshot(home, definition), binding.Conversation));
        }, scope, retain).ConfigureAwait(false);
        var fresh = await OpenHomeWithinSourceAsync(scope, retain, token).ConfigureAwait(false);
        if (fresh.Actor != observed.Actor)
            throw new AssistantCommandRefusedException("The actual Home actor changed during Den membership observation.");
        return observed;
    }
}
