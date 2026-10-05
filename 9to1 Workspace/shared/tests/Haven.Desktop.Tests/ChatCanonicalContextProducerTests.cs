using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual Chat producer and coordinator/frame owner; controlled repository/actor/provider only, never cloud or installed authority proof.</summary>
public sealed class ChatCanonicalContextProducerTests : IDisposable
{
    private static readonly AsyncLocal<ChatCanonicalContextProducerTests?> OriginalFixtureOwner = new();
    private readonly List<string> _ownedWorkspaces = [];
    public ChatCanonicalContextProducerTests() => OriginalFixtureOwner.Value = this;
    public void Dispose()
    {
        foreach (var root in _ownedWorkspaces) Directory.Delete(root, recursive: true);
        if (ReferenceEquals(OriginalFixtureOwner.Value, this)) OriginalFixtureOwner.Value = null;
    }

    [Fact]
    public async Task Persisted_capture_uses_actual_saved_conversation_and_original_selected_history_on_same_request()
    {
        var h = Harness.Create(temporary: false);
        var old = new ChatMessage(Guid.NewGuid(), h.Conversation.Id, MessageRole.User, new string('x', 9000),
            null, null, null, h.Conversation.CreatedAt);
        var recent = new ChatMessage(Guid.NewGuid(), h.Conversation.Id, MessageRole.Assistant, "original selected record",
            "actual-agent", h.Model.Name, "{\"source\":\"original\"}", h.Conversation.CreatedAt.AddMinutes(1));
        h.Conversations.Messages.AddRange([old, recent]);
        await h.RunAsync("hello", new GenerationOptions(ContextLimit: 2000));
        var capture = Assert.Single(h.Capture.Chat);
        Assert.Same(h.Provider.ChatRequest, capture.Request);
        Assert.Same(h.Conversations.Current, capture.Inventory.OriginalConversation);
        Assert.NotEqual(h.Conversation.UpdatedAt, capture.Inventory.OriginalConversation.UpdatedAt);
        Assert.Contains(capture.Inventory.OriginalHistory, value => ReferenceEquals(value, recent));
        Assert.DoesNotContain(capture.Inventory.OriginalHistory, value => value.Id == old.Id);
        Assert.Contains(capture.Request.Messages, value => value.Content == recent.Content);
        Assert.Empty(capture.Inventory.SelectedBackgroundLearning);
        Assert.Null(capture.Inventory.OriginalBackgroundScopes);
        Assert.Equal(h.Service.CurrentCanonicalTask!.TaskId, capture.Current.TaskId);
        Assert.Equal(TaskExecutionLifecycle.Completed, h.Service.CurrentCanonicalTask.State);
        Assert.Equal(1, h.Provider.Frames);
        Assert.Equal(1, h.Authority.Lease!.Disposes);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Temporary_agent_context_remains_actual_transient_object_without_invented_persistence()
    {
        var h = Harness.Create(temporary: true);
        await h.RunAsync("temporary context");
        var capture = Assert.Single(h.Capture.Chat);
        Assert.Same(h.Conversation, capture.Inventory.OriginalConversation);
        Assert.True(capture.Inventory.OriginalConversation.IsTemporary);
        Assert.Equal(0, h.Conversations.Writes);
        Assert.Equal(0, h.Conversations.Reads);
        Assert.Same(h.Provider.ChatRequest, capture.Request);
        Assert.Equal(TaskExecutionLifecycle.Completed, h.Service.CurrentCanonicalTask!.State);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Unknown_effective_model_refuses_canonical_tool_calls_before_any_owner_or_workspace_effect()
    {
        var h = Harness.Create(temporary: true, tools: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.RunAsync("Create the file"));
        var capture = Assert.Single(h.Capture.Tools);
        Assert.Same(h.Provider.ToolRequest, capture.Request);
        Assert.NotNull(capture.Request.ExecutionContext);
        Assert.Equal(h.Service.CurrentCanonicalTask!.TaskId, capture.Request.ExecutionContext!.TaskId);
        Assert.Equal(0, h.ToolOwner.Preparations);
        Assert.Equal(0, h.Workspace.Effects);
        Assert.Equal(0, h.Provider.CompatibilityCompletions);
        // This source proves the pre-effect refusal, not yet the separate orchestration suspension successor.
        Assert.NotEqual(TaskExecutionLifecycle.Completed, (await h.Coordinator.GetAsync(capture.Current.TaskId, default))!.State);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Context_owner_refusal_prevents_actual_provider_body_and_preserves_original_failure()
    {
        var h = Harness.Create(temporary: true);
        var failure = new UnauthorizedAccessException("actual controlled context-owner refusal");
        h.Capture.Refusal = failure;
        var observed = await Record.ExceptionAsync(() => h.RunAsync("private context"));
        Assert.Same(failure, observed);
        Assert.Equal(0, h.Provider.Frames);
        Assert.Null(h.Provider.ChatRequest);
        Assert.Empty(h.Service.CurrentCanonicalTask!.Attempts);
        Assert.Equal(0, h.Workspace.Effects);
        await h.Runtime.CloseAndDrainAsync();
    }

    private sealed class Harness
    {
        public required string WorkspaceRoot;
        public required Conversation Conversation;
        public required ModelDescriptor Model;
        public required Conversations Conversations;
        public required TaskExecutionCoordinator Coordinator;
        public required TaskRunOriginalFrameOwner Runtime;
        public required Authority Authority;
        public required Capture Capture;
        public required Provider Provider;
        public required ToolsOwner ToolOwner;
        public required Workspace Workspace;
        public required ChatSessionService Service;
        public static Harness Create(bool temporary, bool tools = false)
        {
            var fixtureOwner = OriginalFixtureOwner.Value ?? throw new InvalidOperationException("The actual test workspace owner is unavailable.");
            var actualWorkspaceRoot = Path.Combine(Path.GetTempPath(), "astra-task-context-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(actualWorkspaceRoot);
            fixtureOwner._ownedWorkspaces.Add(actualWorkspaceRoot);
            var now = DateTimeOffset.UtcNow.AddHours(-1);
            var conversation = new Conversation(Guid.NewGuid(), HavenMode.Studio, ConversationKind.StudioChat,
                "controlled original", null, null, false, temporary, now, now);
            var model = new ModelDescriptor("synthetic-model", 1, "controlled", "controlled", "controlled",
                tools ? new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }
                    : new HashSet<ToolCapability> { ToolCapability.Text }, now);
            var authority = new Authority();
            TaskExecutionCoordinator? coordinator = null;
            var runtime = new TaskRunOriginalFrameOwner((task, run, attempt, token) =>
                coordinator!.TryGetIssuedAttemptAsync(task, run, attempt, token));
            coordinator = new TaskExecutionCoordinator(new Tasks(), new Sink(), admissionAuthority: authority, runtimeSettlement: runtime);
            var capture = new Capture(); var conversations = new Conversations(); var workspace = new Workspace(); var owner = new ToolsOwner();
            var provider = new Provider(model, coordinator, runtime, capture);
            return new Harness
            {
                WorkspaceRoot = actualWorkspaceRoot,
                Conversation = conversation, Model = model, Conversations = conversations, Coordinator = coordinator,
                Runtime = runtime, Authority = authority, Capture = capture, Provider = provider, ToolOwner = owner, Workspace = workspace,
                Service = new ChatSessionService(conversations, provider, new CapabilityPreflightService(), new Safety(),
                    new WorkspaceToolRuntime(workspace), new ComputerToolRuntime(new Computer()),
                    taskCoordinator: coordinator, taskToolOwner: owner, taskProviderContextCapture: capture)
            };
        }
        public async Task RunAsync(string prompt, GenerationOptions? options = null)
        {
            await foreach (var _ in Service.SendAsync(Conversation, prompt, Model, EffortLevel.Medium, [], "controlled", "",
                DuoMode.Solo, WorkspaceRoot, null, null, null, default, generationOptions: options,
                taskExecutionIntent: TaskRunExecutionIntent.CanonicalAgenticTask)) { }
        }
    }

    private sealed class Capture : ITaskRunProviderContextCapture
    {
        public sealed record ChatItem(TaskExecutionSnapshot Current, OllamaChatRequest Request, TaskRunContextInventory Inventory);
        public sealed record ToolItem(TaskExecutionSnapshot Current, OllamaToolRequest Request, TaskRunContextInventory Inventory);
        public List<ChatItem> Chat { get; } = []; public List<ToolItem> Tools { get; } = [];
        public Exception? Refusal;
        public ValueTask CaptureOriginalAsync(TaskExecutionSnapshot current, OllamaChatRequest request, TaskRunContextInventory inventory, CancellationToken token)
        { if (Refusal is { } failure) return ValueTask.FromException(failure); Chat.Add(new(current, request, inventory)); return ValueTask.CompletedTask; }
        public ValueTask CaptureOriginalAsync(TaskExecutionSnapshot current, OllamaToolRequest request, TaskRunContextInventory inventory, CancellationToken token)
        { if (Refusal is { } failure) return ValueTask.FromException(failure); Tools.Add(new(current, request, inventory)); return ValueTask.CompletedTask; }
    }
    private sealed class Provider(ModelDescriptor model, TaskExecutionCoordinator coordinator, TaskRunOriginalFrameOwner runtime, Capture capture) : IOllamaClient
    {
        public OllamaChatRequest? ChatRequest; public OllamaToolRequest? ToolRequest; public int Frames; public int CompatibilityCompletions;
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]);
        private async Task<TaskRunAttemptAdmission> OpenAsync(ProviderExecutionContext? context, CancellationToken token)
        {
            var exact = context ?? throw new InvalidOperationException("Missing canonical context");
            var admission = await coordinator.StartAttemptAsync(exact.TaskId, exact.ExecutionId,
                new TaskRunRouteCandidate("synthetic-route", 1, "synthetic", model.Name, null, false, ["Text"]), token);
            await runtime.RegisterOriginalAttemptAsync(admission, token);
            await coordinator.MarkAttemptRunningAsync(exact.TaskId, exact.ExecutionId, admission.AttemptId, token);
            return admission;
        }
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            Assert.Same(request, Assert.Single(capture.Chat).Request); ChatRequest = request;
            var admission = await OpenAsync(request.ExecutionContext, token);
            var original = runtime.StartOriginalFrameAsync(admission, _ => { Frames++; return Task.FromResult("actual controlled reply"); }, token);
            yield return await original;
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token)
        { CompatibilityCompletions++; throw new InvalidOperationException("No unowned canonical compatibility call"); }
        public async Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        {
            Assert.Same(request, Assert.Single(capture.Tools).Request); ToolRequest = request;
            var admission = await OpenAsync(request.ExecutionContext, token);
            return await runtime.StartOriginalFrameAsync(admission, _ =>
            {
                Frames++;
                using var json = JsonDocument.Parse("{\"path\":\"actual.txt\",\"content\":\"actual\"}");
                var args = json.RootElement.EnumerateObject().ToDictionary(value => value.Name, value => value.Value.Clone());
                return Task.FromResult(new OllamaToolResponse("", [new OllamaToolCall("write_file", args)]));
            }, token);
        }
    }
    private sealed class Tasks : ITaskExecutionRepository
    {
        private readonly Dictionary<Guid, TaskExecutionSnapshot> _rows = [];
        public Task UpsertAsync(TaskExecutionSnapshot next, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_rows.TryGetValue(next.TaskId, out var prior) ? prior.PersistenceRevision != next.PersistenceRevision - 1 : next.PersistenceRevision != 1)
                throw new TaskExecutionRevisionConflictException(next.TaskId, next.PersistenceRevision - 1, next.PersistenceRevision);
            _rows[next.TaskId] = next; return Task.CompletedTask;
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(_rows.GetValueOrDefault(id));
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token) => Task.FromResult(_rows.Values.FirstOrDefault(row => row.ContextId == id));
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(_rows.Values.ToArray());
    }
    private sealed class Conversations : IConversationRepository
    {
        public Conversation? Current; public int Writes; public int Reads; public List<ChatMessage> Messages { get; } = [];
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) { Reads++; return Task.FromResult(Current?.Id == id ? Current : null); }
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int count, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>(Current is null ? [] : [Current]);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(value => value.ConversationId == id).ToArray());
        public Task UpsertConversationAsync(Conversation value, CancellationToken token) { Writes++; Current = value; return Task.CompletedTask; }
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) { Writes++; Messages.Add(message); return Task.CompletedTask; }
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Authority : ITaskRunCommandAuthority
    {
        public Lease? Lease;
        public Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(TaskExecutionSnapshot current, CancellationToken token) => Task.FromResult(
            new TaskExecutionOwnerBinding(current.TaskId, current.ContextId, current.ExecutionId, "controlled-actor", "controlled-profile", null, null, "controlled-auth", "controlled-start"));
        public Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(TaskExecutionSnapshot current, Guid id, TaskRunRouteCandidate candidate, Guid? old, CancellationToken token)
        { Lease = new Lease(current.OwnerBinding!, id, candidate); return Task.FromResult<ITaskRunAdmissionLease>(Lease); }
        public Task ValidateTaskCommandAsync(TaskExecutionSnapshot current, string command, CancellationToken token) => Task.CompletedTask;
        public Task ValidateAcceptedActionAsync(TaskExecutionSnapshot current, Guid attempt, Guid action, string receipt, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Lease(TaskExecutionOwnerBinding owner, Guid id, TaskRunRouteCandidate candidate) : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => owner; public Guid AttemptId => id; public TaskRunRouteCandidate Candidate => candidate;
        public string ReceiptReference => "controlled-live-lease"; public int Disposes;
        public ValueTask RevalidateAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); if (Disposes != 0) throw new ObjectDisposedException(nameof(Lease)); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Disposes++; return ValueTask.CompletedTask; }
    }
    private sealed class ToolsOwner : ITaskRunToolActionOwner
    {
        public int Preparations;
        public bool SupportsCanonicalInvocation(ToolRuntimeKind runtime, string name) => runtime == ToolRuntimeKind.Workspace;
        public Task<ITaskRunToolActionPreparation> PrepareOriginalAsync(TaskRunAttemptAdmission original, TaskExecutionSnapshot current, Guid action,
            OllamaToolCall call, ToolRuntimeKind runtime, PermissionMode permission, string? root, CancellationToken token)
        { Preparations++; throw new InvalidOperationException("Unexpected pre-effect dispatch"); }
        public Task<TaskRunToolActionResult> ExecuteOriginalAsync(ITaskRunToolActionPreparation original, Func<CancellationToken, Task<WorkspaceToolResult>> body, CancellationToken token) => throw new NotSupportedException();
        public ValueTask ValidateOriginalPreparationAsync(ITaskRunToolActionPreparation original, TaskExecutionSnapshot current, CancellationToken token) => throw new NotSupportedException();
        public ValueTask ValidateOriginalResultAsync(ITaskRunToolActionPreparation original, TaskRunToolActionResult result, CancellationToken token) => throw new NotSupportedException();
        public ValueTask RetireAcknowledgedOriginalAsync(ITaskRunToolActionPreparation original, TaskExecutionSnapshot current, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Workspace : IWorkspaceToolService
    {
        public int Effects;
        public string ResolveWorkspacePath(string root, string path) => Path.Combine(root, path);
        public Task<string> ReadTextAsync(string root, string path, CancellationToken token) { Effects++; throw new InvalidOperationException("Unexpected effect"); }
        public Task WriteTextAtomicAsync(string root, string path, string body, CancellationToken token) { Effects++; throw new InvalidOperationException("Unexpected effect"); }
        public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string pattern, CancellationToken token) { Effects++; throw new InvalidOperationException("Unexpected effect"); }
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) { Effects++; throw new InvalidOperationException("Unexpected effect"); }
    }
    private sealed class Computer : IComputerToolService
    {
        public bool IsSupported => false;
        public Task<string> SnapshotAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<string> ListWindowsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<string> LaunchAppAsync(string name, CancellationToken token) => throw new NotSupportedException();
        public Task<string> FocusWindowAsync(string title, CancellationToken token) => throw new NotSupportedException();
        public Task<string> InvokeAsync(string window, string name, string id, CancellationToken token) => throw new NotSupportedException();
        public Task<string> ClickAsync(string window, int x, int y, string button, CancellationToken token) => throw new NotSupportedException();
        public Task<string> TypeAsync(string window, string text, CancellationToken token) => throw new NotSupportedException();
        public Task<string> PressAsync(string window, string keys, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CloseWindowAsync(string title, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Safety : IConversationSafetyService
    {
        public Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid id, CancellationToken token) => Task.FromResult(new ConversationSafetySnapshot(id, 0, ConversationSafetyState.Active, null, 0));
        public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid id, ConfirmedSafetyFlag flag, CancellationToken token) => throw new NotSupportedException();
        public Task EnsureMayActAsync(Guid id, string operation, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class Sink : IExecutionEventSink { public bool TryPublish(ExecutionEvent value) => true; }
}
