using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Http;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class ChatSessionSpaceTurnExecutorTests : IDisposable
{
    private readonly CancellationTokenSource _testLifetime = new(TimeSpan.FromSeconds(30));
    public void Dispose() => _testLifetime.Dispose();
    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Model_discovery_known_outage_returns_no_model_and_retries_actual_discovery(HttpStatusCode code)
    {
        var rig = new Rig();
        rig.Provider.InventoryFailure = new HttpRequestException("controlled provider outage", null, code);
        var catalog = new ChatSessionSpaceModelCatalog(new ChatModelInventoryCache(rig.Provider), _ => Task.FromResult<string?>(rig.Model.Name));
        var unavailable = await catalog.GetInventoryAsync(_testLifetime.Token);
        Assert.Empty(unavailable.Models);
        Assert.Contains("temporarily unavailable", unavailable.UnavailableReason!);
        rig.Provider.InventoryFailure = null;
        var recovered = await catalog.GetInventoryAsync(_testLifetime.Token);
        Assert.Same(rig.Model, Assert.Single(recovered.Models));
        Assert.Null(recovered.UnavailableReason);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Model_discovery_does_not_turn_authentication_failure_into_empty_success(HttpStatusCode code)
    {
        var rig = new Rig();
        var failure = new HttpRequestException("controlled account denial", null, code);
        rig.Provider.InventoryFailure = failure;
        var catalog = new ChatSessionSpaceModelCatalog(new ChatModelInventoryCache(rig.Provider), _ => Task.FromResult<string?>(null));
        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(() => catalog.GetInventoryAsync(_testLifetime.Token)));
    }

    [Fact]
    public async Task Preference_owner_failure_is_never_classified_as_provider_outage()
    {
        var rig = new Rig();
        var failure = new HttpRequestException("actual preference source failed", null, HttpStatusCode.ServiceUnavailable);
        var catalog = new ChatSessionSpaceModelCatalog(new ChatModelInventoryCache(rig.Provider), _ => Task.FromException<string?>(failure));
        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(() => catalog.GetInventoryAsync(_testLifetime.Token)));
    }

    [Fact]
    public async Task Presenter_and_actual_provider_cleanup_faults_are_both_retained_after_original_join()
    {
        var rig = new Rig();
        var observerFailure = new IOException("actual observer failed");
        var cleanupFailure = new IOException("actual provider cleanup failed");
        rig.Provider.ClosingFailure = cleanupFailure;
        var error = await Assert.ThrowsAsync<AggregateException>(() => rig.Executor.ExecuteAsync(
            new(rig.Repository.Current, "hello", rig.Model, []),
            (item, _) => item.Kind == ChatStreamEventKind.AssistantDelta
                ? Task.FromException(observerFailure) : Task.CompletedTask, _testLifetime.Token));
        Assert.True(rig.Provider.Closed);
        Assert.Equal(3, error.InnerExceptions.Count);
        Assert.Single(error.InnerExceptions, cause => ReferenceEquals(cause, observerFailure));
        Assert.Single(error.InnerExceptions, cause => ReferenceEquals(cause, cleanupFailure));
        // The actual shared ledger must refuse a clean-close receipt when its
        // original body disposal failed, even though every source was joined.
        var refusal = Assert.Single(error.InnerExceptions, cause => cause is InvalidOperationException);
        Assert.Equal("Original body/tracker cleanup was not acknowledged.", refusal.Message);
    }

    [Fact]
    public async Task Real_ChatSession_pipeline_waits_for_stream_observer_and_persists_both_actual_turns()
    {
        var rig = new Rig();
        var original = rig.Repository.Current;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<ChatStreamEventKind>();
        var token = _testLifetime.Token;
        var pending = rig.Executor.ExecuteAsync(new(original, "hello actual pipeline", rig.Model, []), async (item, ct) =>
        {
            events.Add(item.Kind);
            if (item.Kind == ChatStreamEventKind.UserMessage)
            { entered.TrySetResult(); await release.Task.WaitAsync(ct); }
        }, token);
        await entered.Task.WaitAsync(token);
        Assert.Empty(rig.Provider.Requests);
        Assert.False(pending.IsCompleted);
        release.TrySetResult();
        await pending;
        Assert.Single(rig.Provider.Requests);
        Assert.Collection(rig.Repository.Messages,
            user => { Assert.Equal(MessageRole.User, user.Role); Assert.Equal(original.Id, user.ConversationId); },
            assistant => { Assert.Equal(MessageRole.Assistant, assistant.Role); Assert.Equal("Controlled response.", assistant.Content); });
        Assert.Contains(ChatStreamEventKind.AssistantDelta, events);
        Assert.Contains(ChatStreamEventKind.AssistantCompleted, events);
        Assert.Equal(original.CreatedAt, rig.Repository.Current.CreatedAt);
        Assert.Equal(original.ParentConversationId, rig.Repository.Current.ParentConversationId);
        Assert.True(rig.Repository.Current.IsPinned);
        Assert.Same(original, rig.Captured!.Conversation);
    }

    [Fact]
    public async Task Stale_conversation_cannot_overwrite_canonical_privacy_or_dispatch_provider()
    {
        var rig = new Rig(); var original = rig.Repository.Current;
        rig.Repository.Current = original with { IsArchived = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Executor.ExecuteAsync(
            new(original, "stale", rig.Model, []), _testLifetime.Token));
        Assert.Empty(rig.Repository.Messages); Assert.Empty(rig.Provider.Requests);
        Assert.Null(rig.Captured);
    }

    [Fact]
    public async Task Selected_attachment_cannot_be_silently_omitted_without_actual_attachment_input()
    {
        var rig = new Rig();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Executor.ExecuteAsync(
            new(rig.Repository.Current, "read this", rig.Model, [Guid.NewGuid()]), _testLifetime.Token));
        Assert.Empty(rig.Repository.Messages); Assert.Empty(rig.Provider.Requests);
    }

    private sealed class Rig
    {
        internal readonly Conversations Repository = new();
        internal readonly ModelDescriptor Model = new("controlled-model", 1, "test", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow);
        internal readonly Provider Provider;
        internal readonly ChatSessionSpaceTurnExecutor Executor;
        internal ChatSpaceTurnRequest? Captured;
        internal Rig()
        {
            Provider = new(Model);
            var session = new ChatSessionService(Repository, Provider, new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()));
            Executor = new(session, Repository, (request, token) =>
            {
                token.ThrowIfCancellationRequested(); Captured = request;
                return Task.FromResult(new ChatSpaceTurnContext(EffortLevel.Medium, "Controlled", "", PermissionMode.Ask, PermissionMode.Ask, PermissionMode.Ask)
                {
                    OriginalSynchronousScope = body => body(),
                    GenerationOptions = new() { RequestedRoutingConstraints = new(false, false) }
                });
            });
        }
    }
    private sealed class Conversations : IConversationRepository
    {
        internal Conversation Current = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Controlled stored Chat",
            null, null, true, false, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow,
            ParentConversationId: Guid.NewGuid(), SpaceId: SpaceRegistry.ChatSpaceId);
        internal readonly List<ChatMessage> Messages = [];
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>([Current]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<Conversation?>(Current.Id == id ? Current : null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.ToArray());
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken token) { Current = conversation; return Task.CompletedTask; }
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) { Messages.Add(message); return Task.CompletedTask; }
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new InvalidOperationException("Unexpected delete.");
    }
    private sealed class Provider(ModelDescriptor model, bool allowControlledTools = false) : IOllamaClient
    {
        internal readonly List<OllamaChatRequest> Requests = [];
        internal Exception? ClosingFailure;
        internal Exception? InventoryFailure;
        internal bool Closed;
        internal readonly List<OllamaToolRequest> ToolRequests = [];
        internal OllamaToolCall? NextControlledToolCall = null;
        internal Action<OllamaChatRequest>? BeforeDispatch = null;
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) =>
            InventoryFailure is { } failure ? Task.FromException<IReadOnlyList<ModelDescriptor>>(failure)
                : Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [EnumeratorCancellation] CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); BeforeDispatch?.Invoke(request); Requests.Add(request);
            try { await Task.CompletedTask; yield return "Controlled response."; }
            finally { Closed = true; if (ClosingFailure is { } failure) throw failure; }
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) =>
            Task.FromResult("1 minute"); // The actual ordinary tracker may request an ETA on a slow runner.
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        {
            if (!allowControlledTools) throw new InvalidOperationException("This controlled ordinary turn offers no tools.");
            token.ThrowIfCancellationRequested(); ToolRequests.Add(request);
            var actualCall = NextControlledToolCall; NextControlledToolCall = null;
            return Task.FromResult(new OllamaToolResponse("Controlled response.", actualCall is null ? [] : [actualCall]));
        }
    }

    private sealed class Safety : IConversationSafetyService
    {
        internal int OriginalToolAdmissions;
        public Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid id, CancellationToken token) =>
            Task.FromResult(new ConversationSafetySnapshot(id, 0, ConversationSafetyState.Active, null, 0));
        public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid id, ConfirmedSafetyFlag flag, CancellationToken token) =>
            throw new InvalidOperationException("Unexpected confirmed safety flag.");
        public Task EnsureMayActAsync(Guid id, string operation, CancellationToken token)
        {
            if (operation.StartsWith("chat.tool.", StringComparison.Ordinal)) OriginalToolAdmissions++;
            return Task.CompletedTask;
        }
    }

    private sealed class Workspace : IWorkspaceToolService
    {
        public string ResolveWorkspacePath(string root, string path) => throw new InvalidOperationException("No workspace grant.");
        public Task<string> ReadTextAsync(string root, string path, CancellationToken token) => throw new InvalidOperationException("No workspace grant.");
        public Task WriteTextAtomicAsync(string root, string path, string content, CancellationToken token) => throw new InvalidOperationException("No workspace grant.");
        public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string pattern, CancellationToken token) => throw new InvalidOperationException("No workspace grant.");
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) => throw new InvalidOperationException("No execution grant.");
    }

    private sealed class Computer : IComputerToolService
    {
        public bool IsSupported => false;
        public Task<string> SnapshotAsync(CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> ListWindowsAsync(CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> LaunchAppAsync(string name, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> FocusWindowAsync(string title, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> InvokeAsync(string title, string name, string id, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> ClickAsync(string title, int x, int y, string button, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> TypeAsync(string title, string text, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> PressAsync(string title, string keys, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> CloseWindowAsync(string title, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
    }
}
