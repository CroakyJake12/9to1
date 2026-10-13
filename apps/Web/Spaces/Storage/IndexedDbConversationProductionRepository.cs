using System.Runtime.Versioning;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>Canonical branches/versions/drafts over the same profile-bound browser
/// owner. Source messages retain their identities; branch views apply saved versions.
/// This is device-local persistence, not remote synchronization or sharing transport.</summary>
[SupportedOSPlatform("browser")]
public sealed partial class IndexedDbConversationProductionRepository : IConversationProductionRepository
{
    private readonly BrowserConversationProductionStore _store;
    private readonly IndexedDbConversationRepository _conversations;
    private readonly SemaphoreSlim _gate = new(1, 1);
    internal IndexedDbConversationProductionRepository(BrowserConversationProductionStore store, IndexedDbConversationRepository conversations)
    { _store = store; _conversations = conversations; }
    private sealed record State(int SchemaVersion, Guid ConversationId, ConversationBranch[] Branches,
        Dictionary<Guid, Guid[]> Messages, ConversationTurn[] Turns, MessageVersion[] Versions, MessageAttachment[] Attachments,
        ConversationDraft[] Drafts, MessageBookmark[] Bookmarks, SharedSession[] Shares)
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, JsonElement>? Extensions { get; init; }
        internal static State Empty(Guid id) => new(1, id, [], [], [], [], [], [], [], []);
    }
    private static void DemandNoPendingMessage(State state)
    { if (state.Extensions?.ContainsKey(PendingMessageKey) == true) throw new InvalidOperationException("The exact pending original message write must be recovered before changing this branch."); }
    private static void Id(Guid id) { if (id == Guid.Empty) throw new ArgumentException("A canonical identity is required."); }
    private static State Decode(Guid id, string? raw)
    {
        var state = raw is null ? State.Empty(id) : JsonSerializer.Deserialize<State>(raw, BrowserConversationProductionStore.Json)
            ?? throw new InvalidDataException("Missing conversation production data.");
        if (state.SchemaVersion != 1 || state.ConversationId != id || state.Branches is null || state.Messages is null || state.Turns is null ||
            state.Versions is null || state.Attachments is null || state.Drafts is null || state.Bookmarks is null || state.Shares is null ||
            state.Branches.Select(branch => branch.Id).Distinct().Count() != state.Branches.Length || state.Branches.Count(branch => branch.IsCurrent) > 1 ||
            state.Branches.Any(branch => branch.Id == Guid.Empty || branch.ConversationId != id || !Enum.IsDefined(branch.Reason)) ||
            state.Messages.Any(pair => !state.Branches.Any(branch => branch.Id == pair.Key) || pair.Value is null || pair.Value.Any(message => message == Guid.Empty) || pair.Value.Distinct().Count() != pair.Value.Length) ||
            state.Versions.Any(version => version.Id == Guid.Empty || version.MessageId == Guid.Empty || version.VersionNumber < 1 || version.Content is null ||
                !Enum.IsDefined(version.Kind) || !state.Branches.Any(branch => branch.Id == version.BranchId)) ||
            state.Versions.GroupBy(version => (version.MessageId, version.BranchId)).Any(group => group.Count(version => version.IsCurrent) > 1) ||
            state.Attachments.Any(item => item.Id == Guid.Empty || item.ConversationId != id || item.SizeBytes < 0 || !Enum.IsDefined(item.Kind) || !Enum.IsDefined(item.ProcessingState)) ||
            state.Drafts.Any(item => item.ConversationId != id) || state.Bookmarks.Any(item => item.ConversationId != id) || state.Shares.Any(item => item.ConversationId != id))
            throw new InvalidDataException("Conversation production identity/branch/version state requires recovery.");
        return state;
    }
    private async Task<(State State, string? Raw)> Load(Guid id, CancellationToken token)
    {
        Id(id);
        var conversation = await _conversations.GetAsync(id, token) ?? throw new InvalidOperationException("The original conversation must exist.");
        if (conversation.IsTemporary) throw new InvalidOperationException("Temporary conversation production state cannot be persisted.");
        var rows = await _store.ExportAsync(id, token);
        var raw = rows.SingleOrDefault().Json;
        return (Decode(id, raw), raw);
    }
    private async Task<T> Change<T>(Guid id, Func<State, IReadOnlyList<ChatMessage>, (State State, T Result)> mutation, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var (state, raw) = await Load(id, token);
            var messages = await _conversations.GetRawMessagesAsync(id, false, token);
            var (next, result) = mutation(state, messages);
            if (ReferenceEquals(state, next)) return result;
            var json = JsonSerializer.Serialize(next, BrowserConversationProductionStore.Json);
            _ = Decode(id, json);
            await _store.CompareExchangeAsync(id, raw, json, token); // One CAS only. Never replay an unknown reply.
            return result;
        }
        finally { _gate.Release(); }
    }
    private async Task<IReadOnlyList<State>> All(CancellationToken token)
    {
        var rows = await _store.ExportAsync(null, token); var results = new List<State>();
        foreach (var row in rows)
            if (await _conversations.GetAsync(row.Id, token) is { IsTemporary: false }) results.Add(Decode(row.Id, row.Json));
        return results;
    }
    private async Task<State> ForBranch(Guid branchId, CancellationToken token) =>
        (await All(token)).SingleOrDefault(state => state.Branches.Any(branch => branch.Id == branchId))
        ?? throw new InvalidOperationException("The branch is not available in this profile.");
    private async Task<State> ForMessage(Guid messageId, CancellationToken token) =>
        (await All(token)).SingleOrDefault(state => state.Messages.Values.Any(ids => ids.Contains(messageId)))
        ?? throw new InvalidOperationException("The message is not mapped to a current original production record.");
    private static State Append(State state, Guid branchId, ChatMessage message)
    {
        if (state.Messages.GetValueOrDefault(branchId, []).Contains(message.Id)) return state;
        var map = state.Messages.ToDictionary(pair => pair.Key, pair => pair.Value);
        map[branchId] = map.GetValueOrDefault(branchId, []).Append(message.Id).ToArray();
        var versions = state.Versions.Append(new(Guid.NewGuid(), message.Id, branchId, 1, MessageVersionKind.Original,
            message.Content, message.MetadataJson, true, message.CreatedAt)).ToArray();
        var turns = state.Turns.ToList();
        if (message.Role == MessageRole.User)
            turns.Add(new(Guid.NewGuid(), state.ConversationId, branchId, turns.Count(turn => turn.BranchId == branchId) + 1, message.Id, null, message.CreatedAt));
        else if (message.Role == MessageRole.Assistant && turns.LastOrDefault(turn => turn.BranchId == branchId && turn.AssistantMessageId is null) is { } open)
            turns[turns.IndexOf(open)] = open with { AssistantMessageId = message.Id };
        return state with { Messages = map, Versions = versions, Turns = turns.ToArray() };
    }
    public Task<ConversationBranch> EnsureRootBranchAsync(Guid id, CancellationToken token) => Change(id, (state, messages) =>
    {
        if (state.Branches.SingleOrDefault(branch => branch.IsCurrent) is { } current) return (state, current);
        var now = DateTimeOffset.UtcNow; var branch = new ConversationBranch(Guid.NewGuid(), id, null, null, "Main", ConversationBranchReason.Root, true, now, now);
        var next = state with { Branches = state.Branches.Select(item => item with { IsCurrent = false }).Append(branch).ToArray() };
        foreach (var message in messages) next = Append(next, branch.Id, message);
        return (next, branch);
    }, token);
    public async Task<IReadOnlyList<ConversationBranch>> GetBranchesAsync(Guid id, CancellationToken token) => (await Load(id, token)).State.Branches;
    public async Task<ConversationBranch?> GetCurrentBranchAsync(Guid id, CancellationToken token) => (await Load(id, token)).State.Branches.SingleOrDefault(branch => branch.IsCurrent);
    public async Task<ConversationBranch> CreateBranchAsync(Guid id, Guid parent, Guid? fork, string name, ConversationBranchReason reason, CancellationToken token)
    {
        var context = await _conversations.GetContextEntriesAsync(id, token);
        if (context.Any(entry => entry.Kind == ContextEntryKind.CompactSummary))
            throw new NotSupportedException("This history has a conversation-scoped compact summary; it cannot be applied to a fork.");
        return await Change(id, (state, messages) =>
        {
        DemandNoPendingMessage(state);
        if (messages.Any(message => message.IsCompacted)) throw new NotSupportedException("This saved history has compacted context; branch-scoped summary projection is required before it can be forked.");
        if (!Enum.IsDefined(reason) || !state.Branches.Any(branch => branch.Id == parent)) throw new ArgumentException("The actual parent branch is required.");
        var ids = state.Messages.GetValueOrDefault(parent, []);
        if (fork is { } message) { var index = Array.IndexOf(ids, message); if (index < 0) throw new InvalidOperationException("The fork message does not belong to the parent branch."); ids = ids.Take(index + 1).ToArray(); }
        var now = DateTimeOffset.UtcNow; var branch = new ConversationBranch(Guid.NewGuid(), id, parent, fork, string.IsNullOrWhiteSpace(name) ? "Branch" : name.Trim(), reason, true, now, now);
        var map = state.Messages.ToDictionary(pair => pair.Key, pair => pair.Value); map[branch.Id] = ids.ToArray();
        var versions = state.Versions.Concat(state.Versions.Where(version => version.BranchId == parent && ids.Contains(version.MessageId))
            .Select(version => version with { Id = Guid.NewGuid(), BranchId = branch.Id })).ToArray();
        var turns = state.Turns.Concat(state.Turns.Where(turn => turn.BranchId == parent && turn.UserMessageId is { } user && ids.Contains(user))
            .Select(turn => turn with { Id = Guid.NewGuid(), BranchId = branch.Id, AssistantMessageId = turn.AssistantMessageId is { } assistant && ids.Contains(assistant) ? assistant : null })).ToArray();
        return (state with { Branches = state.Branches.Select(item => item with { IsCurrent = false }).Append(branch).ToArray(), Messages = map, Versions = versions, Turns = turns }, branch);
        }, token);
    }
    public Task SetCurrentBranchAsync(Guid id, Guid branchId, CancellationToken token) => Change(id, (state, _) =>
    {
        DemandNoPendingMessage(state);
        if (!state.Branches.Any(branch => branch.Id == branchId)) throw new InvalidOperationException("The selected branch belongs to another conversation.");
        return (state with { Branches = state.Branches.Select(branch => branch with { IsCurrent = branch.Id == branchId }).ToArray() }, true);
    }, token);
    public async Task<IReadOnlyList<ConversationTurn>> GetTurnsAsync(Guid id, Guid branchId, CancellationToken token) =>
        (await Load(id, token)).State.Turns.Where(turn => turn.BranchId == branchId).OrderBy(turn => turn.Sequence).ToArray();
    public async Task<IReadOnlyList<MessageVersion>> GetVersionsAsync(Guid messageId, Guid branchId, CancellationToken token) =>
        (await ForBranch(branchId, token)).Versions.Where(version => version.MessageId == messageId && version.BranchId == branchId).OrderBy(version => version.VersionNumber).ToArray();
    public async Task<MessageVersion?> GetCurrentVersionAsync(Guid messageId, Guid branchId, CancellationToken token) =>
        (await GetVersionsAsync(messageId, branchId, token)).SingleOrDefault(version => version.IsCurrent);
    public async Task<MessageVersion> AddVersionAsync(Guid messageId, Guid branchId, MessageVersionKind kind, string content, string? metadataJson, bool current, CancellationToken token)
    {
        var owner = await ForBranch(branchId, token);
        return await Change(owner.ConversationId, (state, _) => AddVersion(state, messageId, branchId, kind, content, metadataJson, current), token);
    }
    private static (State, MessageVersion) AddVersion(State state, Guid messageId, Guid branchId, MessageVersionKind kind, string content, string? metadata, bool current)
    {
        DemandNoPendingMessage(state);
        if (!Enum.IsDefined(kind) || content is null || !state.Messages.GetValueOrDefault(branchId, []).Contains(messageId)) throw new InvalidOperationException("The original mapped message and valid version are required.");
        var number = state.Versions.Where(version => version.MessageId == messageId && version.BranchId == branchId).Select(version => version.VersionNumber).DefaultIfEmpty().Max() + 1;
        var added = new MessageVersion(Guid.NewGuid(), messageId, branchId, number, kind, content, metadata, current, DateTimeOffset.UtcNow);
        return (state with { Versions = state.Versions.Select(version => current && version.MessageId == messageId && version.BranchId == branchId ? version with { IsCurrent = false } : version).Append(added).ToArray() }, added);
    }
    public async Task ReplaceMessageContentAsync(Guid messageId, string content, string? metadataJson, CancellationToken token)
    {
        var owner = await ForMessage(messageId, token);
        await Change(owner.ConversationId, (state, _) =>
        {
            var branch = state.Branches.Single(item => item.IsCurrent);
            var version = state.Versions.SingleOrDefault(item => item.MessageId == messageId && item.BranchId == branch.Id && item.IsCurrent);
            if (version?.Content == content && version.MetadataJson == metadataJson) return (state, true);
            var changed = AddVersion(state, messageId, branch.Id, MessageVersionKind.UserEdit, content, metadataJson, true); return (changed.Item1, true);
        }, token);
    }
    public async Task RemoveBranchMessagesAfterAsync(Guid branchId, Guid messageId, CancellationToken token)
    {
        var owner = await ForBranch(branchId, token);
        await Change(owner.ConversationId, (state, _) =>
        {
            DemandNoPendingMessage(state);
            var ids = state.Messages.GetValueOrDefault(branchId, []); var index = Array.IndexOf(ids, messageId);
            if (index < 0) throw new InvalidOperationException("The branch cutoff message is unavailable.");
            var retained = ids.Take(index + 1).ToArray(); var map = state.Messages.ToDictionary(pair => pair.Key, pair => pair.Value); map[branchId] = retained;
            return (state with { Messages = map, Turns = state.Turns.Where(turn => turn.BranchId != branchId || turn.UserMessageId is { } user && retained.Contains(user))
                .Select(turn => turn.BranchId == branchId && turn.AssistantMessageId is { } assistant && !retained.Contains(assistant) ? turn with { AssistantMessageId = null } : turn).ToArray() }, true);
        }, token);
    }
    internal async Task ObserveCommittedMessageAsync(ChatMessage message, CancellationToken token)
    {
        await Change(message.ConversationId, (state, _) => (AcknowledgeMessage(state, message), true), token);
    }
    internal async Task<IReadOnlyList<ChatMessage>> ProjectCurrentAsync(Guid id, IReadOnlyList<ChatMessage> originals, CancellationToken token)
    {
        var state = (await Load(id, token)).State;
        if (state.Extensions?.ContainsKey(PendingMessageKey) == true) throw new InvalidOperationException("The original message write needs exact recovery before the conversation can continue.");
        var branch = state.Branches.SingleOrDefault(item => item.IsCurrent);
        if (branch is null) return originals;
        var ids = state.Messages.GetValueOrDefault(branch.Id, []); var byId = originals.ToDictionary(message => message.Id);
        var missing = originals.Where(message => !state.Messages.Values.Any(mapped => mapped.Contains(message.Id)) &&
            !state.Versions.Any(version => version.MessageId == message.Id)).ToArray();
        if (missing.Length != 0) throw new InvalidOperationException("Committed messages have no acknowledged branch mapping; use conversation recovery before continuing.");
        if (ids.Any(messageId => !byId.ContainsKey(messageId))) throw new InvalidDataException("A saved branch refers to missing original messages; recovery is required.");
        return ids.Select(messageId =>
        {
            var original = byId[messageId]; var version = state.Versions.SingleOrDefault(item => item.MessageId == messageId && item.BranchId == branch.Id && item.IsCurrent);
            return version is null ? original : original with { Content = version.Content, MetadataJson = version.MetadataJson };
        }).ToArray();
    }
    public async Task<IReadOnlyList<MessageAttachment>> GetAttachmentsAsync(Guid id, Guid? messageId, CancellationToken token) =>
        (await Load(id, token)).State.Attachments.Where(item => messageId is null || item.MessageId == messageId).ToArray();
    public async Task<IReadOnlyList<MessageAttachment>> GetRecentAttachmentsAsync(int limit, CancellationToken token) =>
        (await All(token)).SelectMany(state => state.Attachments).OrderByDescending(item => item.UpdatedAt).Take(Math.Max(0, limit)).ToArray();
    public Task<MessageAttachment> UpsertAttachmentAsync(MessageAttachment attachment, CancellationToken token) => Change(attachment.ConversationId, (state, messages) =>
    {
        Id(attachment.Id);
        if (attachment.MessageId is { } message && !messages.Any(item => item.Id == message) || attachment.BranchId is { } branch && !state.Branches.Any(item => item.Id == branch))
            throw new InvalidOperationException("The attachment belongs to an unavailable message or branch.");
        return (state with { Attachments = state.Attachments.Where(item => item.Id != attachment.Id).Append(attachment).ToArray() }, attachment);
    }, token);
    public async Task DeleteAttachmentAsync(Guid attachmentId, CancellationToken token)
    {
        var owner = (await All(token)).SingleOrDefault(state => state.Attachments.Any(item => item.Id == attachmentId));
        if (owner is not null) await Change(owner.ConversationId, (state, _) => (state with { Attachments = state.Attachments.Where(item => item.Id != attachmentId).ToArray() }, true), token);
    }
    public async Task<ConversationDraft?> GetDraftAsync(Guid id, Guid? branchId, CancellationToken token) => (await Load(id, token)).State.Drafts.SingleOrDefault(item => item.BranchId == branchId);
    public Task SaveDraftAsync(ConversationDraft draft, CancellationToken token) => Change(draft.ConversationId, (state, _) =>
    {
        if (draft.BranchId is { } branch && !state.Branches.Any(item => item.Id == branch)) throw new InvalidOperationException("The draft branch is unavailable.");
        var ids = JsonSerializer.Deserialize<Guid[]>(draft.AttachmentIdsJson) ?? throw new ArgumentException("The draft attachment identities are invalid.");
        if (ids.Distinct().Count() != ids.Length || ids.Any(id => !state.Attachments.Any(item => item.Id == id))) throw new InvalidOperationException("The draft refers to unavailable attachments.");
        return (state with { Drafts = state.Drafts.Where(item => item.BranchId != draft.BranchId).Append(draft).ToArray() }, true);
    }, token);
    public Task DeleteDraftAsync(Guid id, Guid? branchId, CancellationToken token) => Change(id, (state, _) => (state with { Drafts = state.Drafts.Where(item => item.BranchId != branchId).ToArray() }, true), token);
    public async Task<IReadOnlyList<MessageBookmark>> GetBookmarksAsync(Guid id, CancellationToken token) => (await Load(id, token)).State.Bookmarks;
    public Task UpsertBookmarkAsync(MessageBookmark bookmark, CancellationToken token) => Change(bookmark.ConversationId, (state, messages) =>
    {
        Id(bookmark.Id); if (!messages.Any(message => message.Id == bookmark.MessageId)) throw new InvalidOperationException("The bookmarked message is unavailable.");
        return (state with { Bookmarks = state.Bookmarks.Where(item => item.Id != bookmark.Id).Append(bookmark).ToArray() }, true);
    }, token);
    public async Task DeleteBookmarkAsync(Guid bookmarkId, CancellationToken token)
    {
        var owner = (await All(token)).SingleOrDefault(state => state.Bookmarks.Any(item => item.Id == bookmarkId));
        if (owner is not null) await Change(owner.ConversationId, (state, _) => (state with { Bookmarks = state.Bookmarks.Where(item => item.Id != bookmarkId).ToArray() }, true), token);
    }
    public async Task<IReadOnlyList<ConversationSearchResult>> SearchAsync(string query, Guid? conversationId, int limit, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0) return [];
        var conversations = conversationId is { } id ? new[] { await _conversations.GetAsync(id, token) }.OfType<Conversation>() : await _conversations.GetRecentAsync(null, int.MaxValue, token);
        var found = new List<ConversationSearchResult>();
        foreach (var conversation in conversations.Where(item => !item.IsTemporary))
        {
            foreach (var message in await _conversations.GetMessagesAsync(conversation.Id, token))
            {
                var offset = message.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase); if (offset < 0) continue;
                var start = Math.Max(0, offset - 60); var snippet = message.Content.Substring(start, Math.Min(200, message.Content.Length - start));
                found.Add(new(conversation.Id, message.Id, conversation.Title, snippet, message.CreatedAt, 0));
            }
        }
        return found.OrderByDescending(item => item.Timestamp).Take(Math.Min(limit, 1000)).ToArray();
    }
    public async Task<SharedSession?> GetActiveShareAsync(Guid id, CancellationToken token) => (await Load(id, token)).State.Shares.SingleOrDefault(item => item.State == SharedSessionState.Active && item.ExpiresAt > DateTimeOffset.UtcNow);
    public Task UpsertShareAsync(SharedSession session, CancellationToken token) => throw new NotSupportedException("No original browser conversation sharing server is configured; a stored token is not a live share.");
    public Task StopShareAsync(Guid shareId, DateTimeOffset stoppedAt, CancellationToken token) => throw new NotSupportedException("No original browser conversation sharing server is configured.");
    public async Task<ConversationExportDocument> BuildExportAsync(Guid id, CancellationToken token)
    {
        var state = (await Load(id, token)).State; var conversation = await _conversations.GetAsync(id, token) ?? throw new InvalidOperationException("The conversation was removed.");
        return new(conversation, state.Branches, await _conversations.GetRawMessagesAsync(id, false, token), state.Versions, state.Attachments, state.Bookmarks, DateTimeOffset.UtcNow);
    }
}
