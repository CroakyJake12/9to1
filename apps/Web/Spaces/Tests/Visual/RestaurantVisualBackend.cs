using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;

// Test-only backend. Never included by NineToOne.Web.csproj's **/Tests/**/*.cs exclusion.
// It supplies canonical input records to the unchanged real controller; no binding keys,
// layout, production authentication, provider replies or tool execution are implemented here.
internal sealed record RestaurantVisualInput(string Provenance, string ConversationTitle, string ModelName,
    string SpeakerName, string UserMessage, string AssistantMessage, string Draft, string[] RecentTitles,
    string[] ToolActivityTitles, DateTimeOffset Timestamp, string[] UnsupportedReferenceSemantics);

internal sealed class RestaurantVisualBackend : IChatSpaceBackend
{
    internal RestaurantVisualInput Input { get; }
    internal Conversation Conversation { get; }
    private readonly ModelDescriptor _model;
    private readonly ChatMessage[] _messages;
    private readonly Conversation[] _recent;
    private readonly ConversationBranch _branch;
    private ConversationDraft _draft;
    internal List<string> Calls { get; } = [];
    internal RestaurantVisualBackend(RestaurantVisualInput input)
    {
        Input = input;
        Conversation = new(Id("restaurant"), HavenMode.Chat, ConversationKind.Chat, input.ConversationTitle,
            null, null, false, false, input.Timestamp, input.Timestamp, SpaceId: SpaceRegistry.ChatSpaceId);
        _model = new(input.ModelName, 0, "Visual-only reference model metadata", "Not a runtime inventory", "Not specified",
            new HashSet<ToolCapability> { ToolCapability.Text }, input.Timestamp);
        _branch = new(Id("branch"), Conversation.Id, null, null, "Main", ConversationBranchReason.Root, true, input.Timestamp, input.Timestamp);
        _draft = new(Conversation.Id, _branch.Id, input.Draft, "[]", input.Timestamp);
        var activities = input.ToolActivityTitles.Select((title, index) => new ToolActivity(Id("activity-" + index), title,
            "", true, TimeSpan.Zero, input.Timestamp)).ToArray();
        _messages = [
            new(Id("user"), Conversation.Id, MessageRole.User, input.UserMessage, null, null, null, input.Timestamp),
            new(Id("assistant"), Conversation.Id, MessageRole.Assistant, input.AssistantMessage, input.SpeakerName, input.ModelName,
                JsonSerializer.Serialize(new { toolActivities = activities }), input.Timestamp.AddSeconds(152))
        ];
        _recent = input.RecentTitles.Select((title, index) => Conversation with { Id = Id("recent-" + index), Title = title,
            UpdatedAt = input.Timestamp.AddMinutes(-index - 1) }).Append(Conversation).ToArray();
    }
    private static Guid Id(string name) => new(SHA256.HashData(Encoding.UTF8.GetBytes("9to1-visual-only-" + name)).AsSpan(0, 16));
    private void Read(string name, CancellationToken token) { token.ThrowIfCancellationRequested(); Calls.Add(name); }
    public Task<IReadOnlyList<Conversation>> GetRecentChatsAsync(int limit, CancellationToken token)
    { Read(nameof(GetRecentChatsAsync), token); return Task.FromResult<IReadOnlyList<Conversation>>(_recent.Take(limit).ToArray()); }
    public Task<ChatSpaceModelInventory> GetModelInventoryAsync(CancellationToken token)
    { Read(nameof(GetModelInventoryAsync), token); return Task.FromResult(new ChatSpaceModelInventory([_model], _model.Name)); }
    public Task<ChatSpaceConversationData> GetConversationAsync(Guid id, CancellationToken token)
    {
        Read(nameof(GetConversationAsync), token);
        if (id != Conversation.Id) throw new InvalidOperationException("Only the explicitly selected visual fixture conversation is populated.");
        return Task.FromResult(new ChatSpaceConversationData(Conversation, _messages, [], [_branch], _draft));
    }
    public Task SaveDraftAsync(Guid id, Guid? branch, string text, IReadOnlyList<Guid> attachments, CancellationToken token)
    {
        Read(nameof(SaveDraftAsync), token);
        if (id != Conversation.Id || branch != _branch.Id || attachments.Count != 0) throw new InvalidOperationException("Unknown visual fixture draft.");
        _draft = _draft with { Content = text }; return Task.CompletedTask;
    }
    public Task<Conversation> CreateChatAsync(CancellationToken token) => throw Unsupported();
    public Task<MessageAttachment> ImportAttachmentAsync(Guid id, Guid? branch, string path, CancellationToken token) => throw Unsupported();
    public Task RemoveAttachmentAsync(Guid id, CancellationToken token) => throw Unsupported();
    public Task SendAsync(ChatSpaceTurnRequest request, CancellationToken token) => throw Unsupported();
    public Task<ConversationBranch> CreateBranchAsync(Guid id, Guid message, string? name, CancellationToken token) => throw Unsupported();
    public Task SwitchBranchAsync(Guid id, Guid branch, CancellationToken token) => throw Unsupported();
    public Task RegenerateAsync(Guid id, Guid message, ModelDescriptor model, CancellationToken token) => throw Unsupported();
    private static NotSupportedException Unsupported() => new("Visual-only data fixture: no mutation, provider, tool or attachment operation is implemented.");
}
