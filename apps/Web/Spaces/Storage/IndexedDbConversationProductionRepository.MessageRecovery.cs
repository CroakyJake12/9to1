using System.Text.Json;
using Haven.Core;

namespace NineToOne.Web.Spaces.Storage;

public sealed partial class IndexedDbConversationProductionRepository
{
    private const string PendingMessageKey = "pendingOriginalMessageWrite";
    private sealed record PendingMessage(Guid MessageId, Guid BranchId, string PayloadHash);
    internal async Task ReserveOriginalMessageWriteAsync(ChatMessage message, CancellationToken token)
    {
        await EnsureRootBranchAsync(message.ConversationId, token);
        await Change(message.ConversationId, (state, _) =>
        {
            if (state.Extensions?.ContainsKey(PendingMessageKey) == true)
                throw new InvalidOperationException("The original message write is awaiting exact acknowledgement or recovery; it cannot be replayed.");
            var branch = state.Branches.Single(item => item.IsCurrent);
            if (state.Messages.Values.Any(ids => ids.Contains(message.Id))) throw new InvalidOperationException("An original message identity already exists; use message versioning instead.");
            var extensions = state.Extensions is null ? new Dictionary<string, JsonElement>() : new(state.Extensions);
            extensions[PendingMessageKey] = JsonSerializer.SerializeToElement(new PendingMessage(message.Id, branch.Id,
                BrowserConversationProductionStore.Hash(JsonSerializer.Serialize(message, BrowserConversationProductionStore.Json))));
            return (state with { Extensions = extensions }, true);
        }, token);
    }
    private static State AcknowledgeMessage(State state, ChatMessage message)
    {
        if (state.Extensions is null || !state.Extensions.TryGetValue(PendingMessageKey, out var pendingJson))
            throw new InvalidOperationException("No exact original message write was reserved.");
        var pending = pendingJson.Deserialize<PendingMessage>() ?? throw new InvalidDataException("The pending original message record is invalid.");
        if (pending.MessageId != message.Id || pending.PayloadHash != BrowserConversationProductionStore.Hash(JsonSerializer.Serialize(message, BrowserConversationProductionStore.Json)))
            throw new InvalidDataException("The observed original message does not match the reserved payload.");
        var extensions = new Dictionary<string, JsonElement>(state.Extensions); extensions.Remove(PendingMessageKey);
        return Append(state, pending.BranchId, message) with { Extensions = extensions };
    }
    public async Task<bool> RecoverOriginalMessageWriteAsync(Guid conversationId, CancellationToken token = default)
    {
        return await Change(conversationId, (state, messages) =>
        {
            if (state.Extensions is null || !state.Extensions.TryGetValue(PendingMessageKey, out var pendingJson)) return (state, true);
            var pending = pendingJson.Deserialize<PendingMessage>() ?? throw new InvalidDataException("The pending original message record is invalid.");
            var message = messages.SingleOrDefault(item => item.Id == pending.MessageId);
            if (message is null) return (state, false); // Absence never proves an unknown write cannot still arrive.
            return (AcknowledgeMessage(state, message), true);
        }, token);
    }
}
