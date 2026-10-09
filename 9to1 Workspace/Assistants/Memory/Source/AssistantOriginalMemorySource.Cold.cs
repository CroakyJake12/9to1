using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantOriginalMemorySource : IChatOriginalPersistentMemoryColdSource
{
    public ChatOriginalPersistentMemoryLineage ObserveOriginalLineage(IChatOriginalPersistentMemoryInput input)
    {
        var original = DemandInput(input);
        var definition = original.Binding.Definition;
        return new(1, definition.Identity.DenId, definition.Identity.NamespaceId, definition.Identity.DefinitionId,
            definition.Revision, original.Binding.DenSessionId, original.Binding.DenSessionRevision,
            original.Binding.Conversation.Id, original.Store.StoreId,
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(definition.Configuration, DenJson.Options))));
    }

    /// <summary>Reissues current live input from a genuinely protected canonical cold
    /// acknowledgment. Durable lineage chooses what to verify; it is never permission.
    /// This uses the maintained membership issuer to bind actual persisted rows and
    /// retains no expired cold request as the authority for subsequent live reads.</summary>
    public Task<ChatOriginalPersistentMemoryColdPreparation> PrepareOriginalColdWithinSourceAsync(
        ChatOriginalPersistentMemoryColdRequest request, Action<Action> scope, Action<Task> retain,
        CancellationToken token) => RunAsync<ChatOriginalPersistentMemoryColdPreparation>(scope, retain, async source =>
    {
        if (request is null || !ReferenceEquals(request.OriginalTaskOwner, _tasks) ||
            !request.OriginalChatOwner.IsIssuedOriginalColdMemoryRequest(request, this))
            return new(null, "The SAME configured Chat and actual protected Task restoration must issue this memory request.");
        if (request.OriginalCapsule.Boundary != TaskRunColdBoundaryKind.NeverStartedAcceptedInput)
            return new(null, "Restoring an unfinished response requires its actual historical memory selection and current permission proof before reusing stored prompt content.");
        var expected = request.OriginalLineage;
        if (expected is not { Schema: 1 } || expected.NamespaceId != AssistantCanonicalMembershipSource.PersonalNamespace ||
            expected.DefinitionRevision < 1 || expected.MembershipRevision < 1 || expected.MemoryStoreId == Guid.Empty ||
            expected.ConversationId != request.ActualConversation.Id ||
            expected.MembershipId != AssistantCanonicalMembershipSource.SessionId(expected.ConversationId))
            return new(null, "The original source-issued Assistant memory lineage is missing or unsupported.");
        // Actual current protected journal/Task/source validation precedes Den lookup.
        var acknowledged = await source.Read(() => request.ValidateOriginalWithinSourceAsync(
            source.Run, source.Retain, token)).ConfigureAwait(false);
        if (acknowledged.ContextId != expected.ConversationId || acknowledged.OwnerBinding is null ||
            request.ActualConversation.Mode != HavenMode.Tasks || request.ActualConversation.Kind != ConversationKind.Task)
            return new(null, "This actual restored Task does not acknowledge the original Assistant memory context.");

        // A new live issuer over the SAME canonical owner tuple validates actual Den rows.
        // It creates no definition, session, conversation, Task, memory or resource grant.
        var membership = new AssistantCanonicalMembershipSource(_home, _conversations, [ObserveOriginalMemoryCapability()]);
        var home = await source.Read(() => membership.OpenHomeWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
        if (home.DenId != expected.DenId)
            return new(null, "The current Home Den does not match the saved Assistant memory lineage.");
        if (!await IsOriginalTaskOwnerCurrentAsync(acknowledged, source, token).ConfigureAwait(false))
            return new(null, "The actual restored Task owner is unavailable or no longer current.");
        var identity = new AssistantIdentity(expected.DenId, expected.NamespaceId, expected.DefinitionId);
        var row = await source.Read(() => membership.DefinitionWithinSourceAsync(home, identity,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        var session = await source.Read(() => home.Den.GetAsync<SessionRecord>(expected.NamespaceId,
            expected.MembershipId, token)).ConfigureAwait(false);
        if (row.Revision != expected.DefinitionRevision || session is null || session.Revision != expected.MembershipRevision)
            return new(null, "The current Assistant definition or membership differs from the retained Task lineage.");
        var binding = await source.Read(() => membership.BindWithinSourceAsync(home, identity, session,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        if (binding.Definition.Revision != expected.DefinitionRevision ||
            !SameConversationScope(binding.Conversation, request.ActualConversation) ||
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(binding.Definition.Configuration, DenJson.Options))) != expected.ConfigurationSha256)
            return new(null, "The current configuration or actual conversation changed; the original memory request cannot be restored.");
        var prepared = await source.Read(() => PrepareOriginalAssistantMemoryInputWithinSourceAsync(binding,
            binding.Definition, source.Run, source.Retain, token)).ConfigureAwait(false);
        if (!prepared.IsPrepared) return new(null, prepared.Reason);
        var fresh = prepared.Input!;
        if (ObserveOriginalLineage(fresh) != expected)
            return new(null, "The current authorized memory store does not preserve the original complete source lineage.");
        var context = new ProviderExecutionContext(acknowledged.TaskId, acknowledged.ContextId, acknowledged.ExecutionId,
            acknowledged.Attempts.LastOrDefault()?.Id, acknowledged.PersistenceRevision);
        await source.Read(() => ValidateOriginalWithinSourceAsync(fresh, binding.Conversation, context,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        var final = await source.Read(() => request.ValidateOriginalWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
        if (!request.OriginalChatOwner.IsIssuedOriginalColdMemoryRequest(request, this) ||
            JsonSerializer.Serialize(final) != JsonSerializer.Serialize(acknowledged))
            return new(null, "The actual protected Task restoration changed before fresh memory publication.");
        return new(fresh, "Fresh current Assistant memory permission was prepared from the original protected Task lineage.");
    });
}
