using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;

namespace Haven.Core.Tests;

public sealed partial class ChatSessionToolLoopTests
{
    [Fact]
    public async Task AskApproval_PausesExactOriginalCallBeforeDispatch_AndAdaptsDeniedSameTurn()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var rig = CreateApprovalRig();
        await using var session = rig.Service.CreateToolApprovalSession(rig.Conversation.Id, lifetime.Token);
        var shown = new TaskCompletionSource<ChatToolApprovalLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closePrompt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = rig.Service.CreateToolApprovalRequest(session, rig.Conversation, "Use the attached connection", lifetime.Token,
            ChatToolApprovalMode.Ask, async (lease, token) => { shown.SetResult(lease); await closePrompt.Task.WaitAsync(token); });
        var running = DrainApprovalSend(rig, request, lifetime.Token);
        var pending = await shown.Task.WaitAsync(lifetime.Token);
        Assert.True(pending.IsPending);
        Assert.Equal(0, rig.Client.InvocationCount);
        Assert.Equal(rig.Conversation.Id, pending.ConversationId);
        Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
        Assert.True(pending.TryResolve(ChatToolApprovalDecision.Deny));
        Assert.False(pending.TryResolve(ChatToolApprovalDecision.Allow));
        closePrompt.SetResult();
        await running;
        Assert.Equal(0, rig.Client.InvocationCount);
        Assert.Contains(rig.Ollama.Requests[1].Messages, turn => turn.Role == "tool" && turn.Content.Contains("declined", StringComparison.Ordinal));
        Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
        Assert.False(pending.IsPending);
        Assert.False(pending.TryResolve(ChatToolApprovalDecision.AllowForSession));
    }

    [Fact]
    public async Task AskApproval_AllowRunsExactToolOnce_AndKeepsProviderReceiptPrivate()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var rig = CreateApprovalRig();
        await using var session = rig.Service.CreateToolApprovalSession(rig.Conversation.Id, lifetime.Token);
        var shown = 0;
        var request = rig.Service.CreateToolApprovalRequest(session, rig.Conversation, "Use the attached connection", lifetime.Token,
            ChatToolApprovalMode.Ask, (lease, _) => { shown++; Assert.True(lease.TryResolve(ChatToolApprovalDecision.Allow)); return Task.CompletedTask; });
        await DrainApprovalSend(rig, request, lifetime.Token);
        Assert.Equal(1, shown);
        Assert.Equal(1, rig.Client.InvocationCount);
        Assert.All(rig.Ollama.Requests, value => Assert.Null(value.Options?.OriginalToolApprovalRequest));
    }

    [Fact]
    public async Task AskApproval_AlwaysAllowIsSameChatSessionOnly_AcknowledgedPromptMustComplete()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var rig = CreateApprovalRig();
        var prompts = 0;
        Task Present(ChatToolApprovalLease lease, CancellationToken _)
        { prompts++; Assert.True(lease.TryResolve(ChatToolApprovalDecision.AllowForSession)); return Task.CompletedTask; }
        await using (var first = rig.Service.CreateToolApprovalSession(rig.Conversation.Id, lifetime.Token))
        {
            for (var turn = 0; turn < 2; turn++)
                await DrainApprovalSend(rig, rig.Service.CreateToolApprovalRequest(first, rig.Conversation,
                    "Use the attached connection", lifetime.Token, ChatToolApprovalMode.Ask, Present), lifetime.Token);
            Assert.Equal(1, prompts);
            Assert.Equal(2, rig.Client.InvocationCount);
        }
        await using var reopened = rig.Service.CreateToolApprovalSession(rig.Conversation.Id, lifetime.Token);
        await DrainApprovalSend(rig, rig.Service.CreateToolApprovalRequest(reopened, rig.Conversation,
            "Use the attached connection", lifetime.Token, ChatToolApprovalMode.Ask, Present), lifetime.Token);
        Assert.Equal(2, prompts);
        Assert.Equal(3, rig.Client.InvocationCount);
    }

    [Fact]
    public async Task AskApproval_AllowDoesNotBypassActualMcpResourceDenial()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var rig = CreateApprovalRig(denyResource: true);
        await using var session = rig.Service.CreateToolApprovalSession(rig.Conversation.Id, lifetime.Token);
        var request = rig.Service.CreateToolApprovalRequest(session, rig.Conversation, "Use the attached connection", lifetime.Token,
            ChatToolApprovalMode.Ask, (lease, _) => { Assert.True(lease.TryResolve(ChatToolApprovalDecision.Allow)); return Task.CompletedTask; });
        await DrainApprovalSend(rig, request, lifetime.Token);
        Assert.Equal(0, rig.Client.InvocationCount);
        Assert.Contains(rig.Ollama.Requests[1].Messages, turn => turn.Role == "tool" && turn.Content.Contains("did not authorise this exact MCP connection", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AskApproval_CancelWaitPreservesOriginalSendTokenAndCannotDispatch()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var send = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var rig = CreateApprovalRig();
        await using var session = rig.Service.CreateToolApprovalSession(rig.Conversation.Id, lifetime.Token);
        var shown = new TaskCompletionSource<ChatToolApprovalLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = rig.Service.CreateToolApprovalRequest(session, rig.Conversation, "Use the attached connection", send.Token,
            ChatToolApprovalMode.Ask, async (lease, token) => { shown.SetResult(lease); await Task.Delay(Timeout.InfiniteTimeSpan, token); });
        var running = DrainApprovalSend(rig, request, send.Token);
        var pending = await shown.Task.WaitAsync(lifetime.Token);
        await send.CancelAsync();
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(send.Token, failure.CancellationToken);
        Assert.False(pending.TryResolve(ChatToolApprovalDecision.Allow));
        Assert.Equal(0, rig.Client.InvocationCount);
    }

    [Fact]
    public async Task AskApproval_CloseJoinsActualPresenterBeforeReportingDrain()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var rig = CreateApprovalRig();
        var session = rig.Service.CreateToolApprovalSession(rig.Conversation.Id, lifetime.Token);
        var shown = new TaskCompletionSource<ChatToolApprovalLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = rig.Service.CreateToolApprovalRequest(session, rig.Conversation, "Use the attached connection", lifetime.Token,
            ChatToolApprovalMode.Ask, async (lease, _) => { shown.SetResult(lease); await release.Task; });
        var running = DrainApprovalSend(rig, request, lifetime.Token);
        var pending = await shown.Task.WaitAsync(lifetime.Token);
        var closing = session.CloseAsync();
        Assert.False(closing.IsCompleted);
        release.SetResult();
        await closing;
        await running;
        Assert.False(pending.TryResolve(ChatToolApprovalDecision.Allow));
        Assert.Equal(0, rig.Client.InvocationCount);
    }

    [Fact]
    public async Task AskApproval_PresenterFailureAfterAllowCreatesNoSessionGrantAndRetainsExactCause()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var rig = CreateApprovalRig();
        var session = rig.Service.CreateToolApprovalSession(rig.Conversation.Id, lifetime.Token);
        var original = new IOException("original UI response failure");
        var request = rig.Service.CreateToolApprovalRequest(session, rig.Conversation, "Use the attached connection", lifetime.Token,
            ChatToolApprovalMode.Ask, (lease, _) => { Assert.True(lease.TryResolve(ChatToolApprovalDecision.AllowForSession)); return Task.FromException(original); });
        Assert.Same(original, await Assert.ThrowsAsync<IOException>(() => DrainApprovalSend(rig, request, lifetime.Token)));
        Assert.Equal(0, rig.Client.InvocationCount);
        Assert.Same(original, await Assert.ThrowsAsync<IOException>(() => session.CloseAsync()));
    }

    [Theory]
    [InlineData(ChatToolApprovalMode.FullAccess)]
    public async Task ApprovalModesWithoutActualRuntimeEvidenceRefuseBeforeAcceptingUserTurn(ChatToolApprovalMode mode)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var rig = CreateApprovalRig();
        await using var session = rig.Service.CreateToolApprovalSession(rig.Conversation.Id, lifetime.Token);
        Assert.False(rig.Service.GetToolApprovalAvailability().Single(item => item.Mode == mode).Available);
        var request = rig.Service.CreateToolApprovalRequest(session, rig.Conversation, "Use the attached connection", lifetime.Token,
            mode, (_, _) => throw new InvalidOperationException("Unavailable policy must not present a false approval."));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DrainApprovalSend(rig, request, lifetime.Token));
        Assert.Contains("Ask for approval", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(rig.Conversations.Messages);
        Assert.Empty(rig.Ollama.Requests);
        Assert.Equal(0, rig.Client.InvocationCount);
    }

    [Fact]
    public async Task AskApproval_ReceiptIsSingleUseAndRestoredPolicyCannotRestoreLiveApproval()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var rig = CreateApprovalRig();
        await using var session = rig.Service.CreateToolApprovalSession(rig.Conversation.Id, lifetime.Token);
        var request = rig.Service.CreateToolApprovalRequest(session, rig.Conversation, "Use the attached connection", lifetime.Token,
            ChatToolApprovalMode.Ask, (lease, _) => { lease.TryResolve(ChatToolApprovalDecision.Deny); return Task.CompletedTask; });
        var serialized = JsonSerializer.Serialize(new GenerationOptions { RequestedToolApprovalMode = request.Mode, OriginalToolApprovalRequest = request });
        var restored = JsonSerializer.Deserialize<GenerationOptions>(serialized)!;
        Assert.Null(restored.OriginalToolApprovalRequest);
        Assert.Equal(request.Mode, restored.RequestedToolApprovalMode);
        await DrainApprovalSend(rig, request, lifetime.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => DrainApprovalSend(rig, request, lifetime.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => DrainApprovalSend(rig, null, lifetime.Token, restored));
        Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
    }

    [Theory]
    [InlineData(ChatToolApprovalMode.ApproveForMe)]
    [InlineData(ChatToolApprovalMode.RunAutomatically)]
    public async Task AutomaticModes_UnknownExternalRuntimeStillAsksBeforeDispatch(ChatToolApprovalMode mode)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var rig = CreateApprovalRig();
        await using var session = rig.Service.CreateToolApprovalSession(rig.Conversation.Id, lifetime.Token);
        var prompts = 0;
        var request = rig.Service.CreateToolApprovalRequest(session, rig.Conversation, "Use the attached connection", lifetime.Token,
            mode, (lease, _) => { prompts++; lease.TryResolve(ChatToolApprovalDecision.Deny); return Task.CompletedTask; });
        await DrainApprovalSend(rig, request, lifetime.Token);
        Assert.Equal(1, prompts);
        Assert.Equal(0, rig.Client.InvocationCount);
    }

    [Fact]
    public async Task AskApproval_PresenterCannotJoinItself()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var rig = CreateApprovalRig();
        var session = rig.Service.CreateToolApprovalSession(rig.Conversation.Id, lifetime.Token);
        var request = rig.Service.CreateToolApprovalRequest(session, rig.Conversation, "Use the attached connection", lifetime.Token,
            ChatToolApprovalMode.Ask, async (_, _) => await session.CloseAsync());
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => DrainApprovalSend(rig, request, lifetime.Token));
        Assert.Contains("cannot join its own", failure.Message);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => session.CloseAsync()));
        Assert.Equal(0, rig.Client.InvocationCount);
    }

    [Theory]
    [InlineData(ChatToolApprovalMode.Ask, "read_file", "sample.txt", false, true)]
    [InlineData(ChatToolApprovalMode.ApproveForMe, "read_file", "sample.txt", false, false)]
    [InlineData(ChatToolApprovalMode.ApproveForMe, "read_file", ".env", false, true)]
    [InlineData(ChatToolApprovalMode.ApproveForMe, "read_file", "sample.txt", true, true)]
    [InlineData(ChatToolApprovalMode.ApproveForMe, "write_file", "new.txt", false, true)]
    [InlineData(ChatToolApprovalMode.RunAutomatically, "write_file", "new.txt", false, false)]
    [InlineData(ChatToolApprovalMode.RunAutomatically, "read_file", "credentials.txt", false, true)]
    [InlineData(ChatToolApprovalMode.RunAutomatically, "run_command", "unused", false, true)]
    public async Task AutomaticModes_UseActualWorkspaceEffectsAndArguments(ChatToolApprovalMode mode,
        string toolName, string path, bool unknownArgument, bool mustPrompt)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var model = new ModelDescriptor("workspace-approval", 1, "test", "test", "test",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UtcNow);
        var arguments = new Dictionary<string, JsonElement>();
        if (toolName == "run_command") arguments["command"] = JsonSerializer.SerializeToElement("echo approval");
        else arguments["path"] = JsonSerializer.SerializeToElement(path);
        if (toolName == "write_file") arguments["content"] = JsonSerializer.SerializeToElement("approved file effect");
        if (unknownArgument) arguments["unexpected_command"] = JsonSerializer.SerializeToElement("unclassified payload");
        if (toolName == "read_file") await File.WriteAllTextAsync(Path.Combine(_root, path), "private source content", lifetime.Token);
        var ollama = new ApprovalOllama(model, toolName, arguments);
        var service = new ChatSessionService(new FakeConversations(recordMessages: true), ollama, new CapabilityPreflightService(),
            new PermitSafety(), new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(new TestComputerTools()));
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Studio, ConversationKind.StudioChat, "Policy", null, null, false, false, now, now);
        await using var session = service.CreateToolApprovalSession(conversation.Id, lifetime.Token);
        var prompts = 0;
        const string prompt = "Inspect the selected workspace operation";
        var request = service.CreateToolApprovalRequest(session, conversation, prompt, lifetime.Token, mode,
            (lease, _) => { prompts++; lease.TryResolve(ChatToolApprovalDecision.Deny); return Task.CompletedTask; });
        var output = new List<ChatStreamEvent>();
        await foreach (var item in service.SendAsync(conversation, prompt, model, EffortLevel.Medium, [], "Default", "",
            DuoMode.Solo, _root, "", "", null, lifetime.Token,
            generationOptions: new GenerationOptions { RequestedToolApprovalMode = mode, OriginalToolApprovalRequest = request },
            explicitCapabilities: [ToolCapability.Tools])) output.Add(item);
        Assert.Equal(mustPrompt ? 1 : 0, prompts);
        var actualTool = Assert.Single(output, item => item.Kind == ChatStreamEventKind.ToolActivity).ToolActivity!;
        Assert.Equal(!mustPrompt, actualTool.Succeeded);
        if (toolName == "write_file") Assert.Equal(!mustPrompt, File.Exists(Path.Combine(_root, path)));
        if (toolName == "read_file") Assert.Equal(!mustPrompt, ollama.Requests[1].Messages.Any(
            turn => turn.Role == "tool" && turn.Content.Contains("private source content", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TemporaryOrdinaryChatDoesNotPersistExecutionContent(bool temporary)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var model = new ModelDescriptor("event-privacy", 1, "test", "test", "test",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools, ToolCapability.Browser }, DateTimeOffset.UtcNow);
        var browser = new RecordingOrdinaryBrowser();
        var sink = new ChatRecordingSink();
        var service = new ChatSessionService(new FakeConversations(), new SingleToolOllama(model, "browser_read_page"),
            new CapabilityPreflightService(), new PermitSafety(), new WorkspaceToolRuntime(new TestWorkspaceTools()),
            new ComputerToolRuntime(new TestComputerTools()), browserTools: new BrowserToolRuntime(browser, browser), executionEvents: sink);
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Private", null, null, false, temporary, now, now);
        await foreach (var _ in service.SendAsync(conversation, "private original prompt", model, EffortLevel.Medium,
            [ActiveCapability.FromDefinition(CapabilityRegistryCatalog.BuiltIns.Single(value => value.Key == "web-search"))],
            "Default", "", DuoMode.Solo, null, null, null, null, lifetime.Token,
            explicitCapabilities: new[] { ToolCapability.Tools, ToolCapability.Browser })) { }
        Assert.Equal(1, browser.Reads);
        if (temporary) Assert.Empty(sink.Events);
        else Assert.Contains(sink.Events, item => item.ActionType == ExecutionActionType.UserPrompt);
    }

    [Fact]
    public async Task AskApproval_FaultedPresenterCancellationRemainsFaultWithOriginalCause()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var rig = CreateApprovalRig();
        var session = rig.Service.CreateToolApprovalSession(rig.Conversation.Id, lifetime.Token);
        var original = new OperationCanceledException("faulted presenter cancellation", lifetime.Token);
        var request = rig.Service.CreateToolApprovalRequest(session, rig.Conversation, "Use the attached connection", lifetime.Token,
            ChatToolApprovalMode.Ask, (_, _) => Task.FromException(original));
        var running = DrainApprovalSend(rig, request, lifetime.Token);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => running);
        Assert.True(running.IsFaulted);
        Assert.Same(original, Assert.Single(failure.InnerExceptions));
        var closeFailure = await Assert.ThrowsAsync<AggregateException>(() => session.CloseAsync());
        Assert.Same(original, Assert.Single(closeFailure.InnerExceptions));
        Assert.Equal(0, rig.Client.InvocationCount);
    }

    [Theory]
    [InlineData("service")]
    [InlineData("conversation")]
    [InlineData("prompt")]
    [InlineData("token")]
    public async Task AskApproval_ReceiptCannotBeReboundToDifferentOriginal(string changed)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var different = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var rig = CreateApprovalRig();
        await using var session = rig.Service.CreateToolApprovalSession(rig.Conversation.Id, lifetime.Token);
        var sourceConversation = changed == "conversation" ? rig.Conversation with { Title = "other snapshot" } : rig.Conversation;
        var request = rig.Service.CreateToolApprovalRequest(session, sourceConversation,
            changed == "prompt" ? "other exact prompt" : "Use the attached connection",
            changed == "token" ? different.Token : lifetime.Token, ChatToolApprovalMode.Ask,
            (_, _) => throw new InvalidOperationException("Wrong source must not present."));
        var actual = changed == "service" ? rig with { Service = CreateApprovalRig().Service } : rig;
        await Assert.ThrowsAsync<InvalidOperationException>(() => DrainApprovalSend(actual, request, lifetime.Token));
        Assert.Empty(rig.Conversations.Messages);
        Assert.Empty(rig.Ollama.Requests);
        Assert.Equal(0, rig.Client.InvocationCount);
    }

    private sealed record ApprovalRig(ChatSessionService Service, Conversation Conversation, ModelDescriptor Model,
        ActiveCapability Capability, ApprovalOllama Ollama, ChatMcpClient Client, FakeConversations Conversations);
    private static ApprovalRig CreateApprovalRig(bool denyResource = false)
    {
        var model = new ModelDescriptor("approval-tools-model", 1, "test", "test", "test",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UtcNow);
        var connection = ChatMcpRepository.ReadyConnection();
        var client = new ChatMcpClient();
        var runtime = new McpToolRuntime(new ChatMcpRepository(connection), client,
            denyResource ? new DenyApprovalMcpResource() : new ExactMcpAdmission(connection.Id));
        var ollama = new ApprovalOllama(model, McpToolRuntime.LocalToolName(connection.Id, "write_item"));
        var conversations = new FakeConversations(recordMessages: true);
        var service = new ChatSessionService(conversations, ollama, new CapabilityPreflightService(), new PermitSafety(),
            new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(new TestComputerTools()), mcpTools: runtime);
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Approval", null, null, false, false, now, now);
        var active = new ActiveCapability(ExternalConnectionNaming.CapabilityKey(connection.Id), ExternalConnectionNaming.PluginName(connection.Name),
            "connection", "Use connection", "connection.mcp", "haven.connections");
        return new(service, conversation, model, active, ollama, client, conversations);
    }
    private static async Task DrainApprovalSend(ApprovalRig rig, ChatToolApprovalRequest? request, CancellationToken token,
        GenerationOptions? options = null)
    {
        options ??= new GenerationOptions { RequestedToolApprovalMode = request!.Mode, OriginalToolApprovalRequest = request };
        await foreach (var _ in rig.Service.SendAsync(rig.Conversation, "Use the attached connection", rig.Model,
            EffortLevel.Medium, [rig.Capability], "Default", "", DuoMode.Solo, null, "", "", null, token, generationOptions: options)) { }
    }
    private sealed class DenyApprovalMcpResource : IMcpInvocationAuthorizer
    {
        public ValueTask<bool> AuthorizeAsync(ExternalConnection connection, string toolName, JsonElement arguments, CancellationToken token) => ValueTask.FromResult(false);
    }
    private sealed class ApprovalOllama(ModelDescriptor model, string toolName, IReadOnlyDictionary<string, JsonElement>? arguments = null) : IOllamaClient
    {
        internal readonly List<OllamaToolRequest> Requests = new();
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => Task.FromResult(string.Empty);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        { await Task.CompletedTask; yield break; }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        {
            Requests.Add(request);
            return Task.FromResult(Requests.Count % 2 == 1
                ? new OllamaToolResponse("", [new(toolName, arguments ?? new Dictionary<string, JsonElement>(), Guid.NewGuid().ToString("N"))])
                : new OllamaToolResponse("The tool outcome was observed.", []));
        }
    }
}
