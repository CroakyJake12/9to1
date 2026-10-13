using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed partial class ChatSessionToolLoopTests
{
    [Fact]
    public async Task Temporary_chat_second_turn_uses_first_turn_without_durable_history_or_writes()
    {
        var conversation = HistoryConversation(temporary: true);
        var (service, provider, repository) = HistoryService();
        repository.Messages.Add(HistoryMessage(conversation, MessageRole.User, "durable history must not leak"));
        var history = new List<ChatMessage>();

        await CollectHistoryAsync(HistorySend(service, provider.Model, conversation, "first question", history), history);
        await CollectHistoryAsync(HistorySend(service, provider.Model, conversation, "second question", history), history);

        Assert.Equal(2, provider.Requests.Count);
        Assert.Equal(new[] { ("user", "first question"), ("assistant", "response 1"), ("user", "second question") },
            provider.Requests[1].Messages.Select(message => (message.Role, message.Content)));
        Assert.Equal(4, history.Count);
        Assert.Equal(0, repository.HistoryReads);
        Assert.Equal(0, repository.Writes);
        Assert.Single(repository.Messages);
        Assert.All(provider.Requests, request => Assert.Null(request.ExecutionContext));
    }

    [Fact]
    public async Task Temporary_history_is_snapshotted_before_enumeration_and_filters_compacted_turns()
    {
        var conversation = HistoryConversation(temporary: true);
        var (service, provider, repository) = HistoryService();
        var history = new List<ChatMessage>
        {
            HistoryMessage(conversation, MessageRole.User, "compacted detail") with { IsCompacted = true },
            HistoryMessage(conversation, MessageRole.Assistant, "compacted answer") with { IsCompacted = true },
            HistoryMessage(conversation, MessageRole.User, "repeat this question")
        };
        var stream = HistorySend(service, provider.Model, conversation, "repeat this question", history,
            registeredContext: "in-memory compact summary");
        history.Clear();
        history.Add(HistoryMessage(conversation, MessageRole.System, "late system injection"));
        history.Add(HistoryMessage(conversation with { Id = Guid.NewGuid() }, MessageRole.User, "late foreign turn"));

        var emitted = new List<ChatMessage>();
        await CollectHistoryAsync(stream, emitted);

        var request = Assert.Single(provider.Requests);
        Assert.Equal(new[] { ("user", "repeat this question"), ("user", "repeat this question") },
            request.Messages.Select(message => (message.Role, message.Content)));
        Assert.Contains("in-memory compact summary", request.SystemPrompt);
        Assert.DoesNotContain("compacted detail", request.SystemPrompt);
        Assert.Equal(0, repository.HistoryReads + repository.Writes);
    }

    [Fact]
    public async Task Temporary_legacy_send_without_snapshot_never_reads_a_durable_row_with_same_id()
    {
        var conversation = HistoryConversation(temporary: true);
        var (service, provider, repository) = HistoryService();
        repository.Messages.Add(HistoryMessage(conversation, MessageRole.User, "durable private turn"));
        await CollectHistoryAsync(HistorySend(service, provider.Model, conversation, "new transient turn", null), []);

        Assert.Equal("new transient turn", Assert.Single(Assert.Single(provider.Requests).Messages).Content);
        Assert.Equal(0, repository.HistoryReads + repository.Writes);
    }

    [Fact]
    public async Task Durable_chat_keeps_repository_context_and_persists_new_turns()
    {
        var conversation = HistoryConversation(temporary: false);
        var (service, provider, repository) = HistoryService();
        repository.Messages.Add(HistoryMessage(conversation, MessageRole.User, "stored question"));
        repository.Messages.Add(HistoryMessage(conversation, MessageRole.Assistant, "stored answer"));

        await CollectHistoryAsync(HistorySend(service, provider.Model, conversation, "next question", null), []);

        Assert.Equal(new[] { ("user", "stored question"), ("assistant", "stored answer"), ("user", "next question") },
            Assert.Single(provider.Requests).Messages.Select(message => (message.Role, message.Content)));
        Assert.Equal(1, repository.HistoryReads);
        Assert.Equal(3, repository.Writes);
        Assert.Equal(4, repository.Messages.Count);
    }

    [Theory]
    [InlineData("durable")]
    [InlineData("canonical")]
    [InlineData("system-conversation")]
    [InlineData("execution-context")]
    [InlineData("foreign-conversation")]
    [InlineData("duplicate")]
    [InlineData("empty-message-id")]
    [InlineData("empty-conversation-id")]
    [InlineData("system")]
    [InlineData("tool")]
    [InlineData("unknown-role")]
    public void Transient_history_rejects_invalid_or_authoritative_input_before_any_work(string failure)
    {
        var conversation = HistoryConversation(temporary: failure != "durable");
        if (failure == "empty-conversation-id") conversation = conversation with { Id = Guid.Empty };
        if (failure == "system-conversation") conversation = conversation with { Kind = ConversationKind.Task };
        var (service, provider, repository) = HistoryService();
        var message = HistoryMessage(conversation, MessageRole.User, "prior turn");
        if (failure == "foreign-conversation") message = message with { ConversationId = Guid.NewGuid() };
        if (failure == "empty-message-id") message = message with { Id = Guid.Empty };
        if (failure == "system") message = message with { Role = MessageRole.System };
        if (failure == "tool") message = message with { Role = MessageRole.Tool };
        if (failure == "unknown-role") message = message with { Role = (MessageRole)99 };
        ChatMessage[] history = failure == "duplicate" ? [message, message] : [message];
        ProviderExecutionContext? context = failure == "execution-context"
            ? new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 0) : null;

        Assert.Throws<ArgumentException>(() => service.SendWithTransientHistoryAsync(conversation, "new turn", provider.Model, EffortLevel.Medium,
            [], "Assistant", "", DuoMode.Solo, null, null, null, null, CancellationToken.None,
            executionContext: context,
            taskExecutionIntent: failure == "canonical" ? TaskRunExecutionIntent.CanonicalAgenticTask : TaskRunExecutionIntent.OrdinaryConversation,
            transientHistory: history));
        Assert.Empty(provider.Requests);
        Assert.Equal(0, provider.DiscoveryCalls);
        Assert.Equal(0, repository.HistoryReads + repository.Writes);
    }

    private static Conversation HistoryConversation(bool temporary)
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "context", null, null, false, temporary, now, now);
    }

    private static ChatMessage HistoryMessage(Conversation conversation, MessageRole role, string content) =>
        new(Guid.NewGuid(), conversation.Id, role, content, null, null, null, DateTimeOffset.UtcNow);

    private static (ChatSessionService Service, HistoryProvider Provider, HistoryRepository Repository) HistoryService()
    {
        var provider = new HistoryProvider();
        var repository = new HistoryRepository();
        return (new ChatSessionService(repository, provider, new CapabilityPreflightService(), new PermitSafety(),
            new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(new TestComputerTools())), provider, repository);
    }

    private static IAsyncEnumerable<ChatStreamEvent> HistorySend(ChatSessionService service, ModelDescriptor model,
        Conversation conversation, string prompt, IReadOnlyList<ChatMessage>? history, string? registeredContext = null) =>
        service.SendWithTransientHistoryAsync(conversation, prompt, model, EffortLevel.Medium, [], "Assistant", "", DuoMode.Solo,
            null, null, null, null, CancellationToken.None, registeredContext: registeredContext, transientHistory: history);

    private static async Task CollectHistoryAsync(IAsyncEnumerable<ChatStreamEvent> stream, List<ChatMessage> history)
    {
        await foreach (var item in stream)
            if (item.Kind is ChatStreamEventKind.UserMessage or ChatStreamEventKind.AssistantCompleted && item.Message is { } message)
                history.Add(message);
    }

    private sealed class HistoryProvider : IOllamaClient
    {
        public ModelDescriptor Model { get; } = new("history-test", 1, "test", "test", "test",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Streaming }, DateTimeOffset.UtcNow);
        public List<OllamaChatRequest> Requests { get; } = [];
        public int DiscoveryCalls { get; private set; }
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token)
        { DiscoveryCalls++; return Task.FromResult<IReadOnlyList<ModelDescriptor>>([Model]); }
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            Requests.Add(request);
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            yield return $"response {Requests.Count}";
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => Task.FromResult("test");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) =>
            throw new InvalidOperationException("Plain history test must not invoke tools.");
    }

    private sealed class HistoryRepository : IConversationRepository
    {
        public List<ChatMessage> Messages { get; } = [];
        public int HistoryReads { get; private set; }
        public int Writes { get; private set; }
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) =>
            throw new InvalidOperationException("Unexpected recent-conversation read.");
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) =>
            throw new InvalidOperationException("Unexpected durable conversation read.");
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) =>
            throw new InvalidOperationException("Unexpected full-history read.");
        public Task<IReadOnlyList<ChatMessage>> GetContextMessagesAsync(Guid id, CancellationToken token)
        { HistoryReads++; return Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(message => message.ConversationId == id).ToArray()); }
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken token)
        { Writes++; return Task.CompletedTask; }
        public Task AddMessageAsync(ChatMessage message, CancellationToken token)
        { Writes++; Messages.Add(message); return Task.CompletedTask; }
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new InvalidOperationException("Unexpected deletion.");
    }
}
