using System.Collections.Frozen;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>Actual registered Chat/Workspace dispatch and model evaluator, with explicit
/// synthetic private admission, provider and owning dispatcher. No real owner receipt is claimed.</summary>
public sealed class ChatOriginalWorkspaceEvidenceTests
{
    [Fact]
    public Task Original_registered_route_without_workspace_reports_exact_runtime_return_and_settles_same_dispatch() =>
        RunAsync(denyModel: false);

    [Fact]
    public Task Configured_EditFiles_model_denial_blocks_original_rename_before_admission_or_owner_dispatch() =>
        RunAsync(denyModel: true);

    private static async Task RunAsync(bool denyModel)
    {
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
            "Original route fixture", null, null, false, true, now, now);
        var model = new ModelDescriptor("original-route-fixture", 1, "fixture", "1B", "fixture",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, now);
        using var admission = new Admission(conversation.Id, model.Name);
        var provider = new Provider(model);
        var dispatcher = new Dispatcher(admission, conversation.Id, model.Name);
        var legacy = new LegacyTools();
        var policy = denyModel ? new ModelPermissionPolicy([ModelPermissionRule.Create(
            ModelPermissionTargetKind.ExactModel, model.Name, ModelPermissionScope.ThisDevice,
            RestrictedModelCapability.EditFiles)]) : ModelPermissionPolicy.Empty;
        var evaluator = new ModelPermissionEvaluator(new PolicyStore(policy));
        var service = new ChatSessionService(new Conversations(), provider, new CapabilityPreflightService(),
            new Safety(), new WorkspaceToolRuntime(legacy, originalTools: dispatcher),
            new ComputerToolRuntime(new UnavailableComputer()), modelPermissions: evaluator,
            originalExecutionAdmissions: admission);
        var events = new List<ChatStreamEvent>();
        await foreach (var item in service.SendAsync(conversation, "Rename the selected Files item", model,
            EffortLevel.Medium, [], "Canonical", "", DuoMode.Solo, null, "", "", null, bound.Token,
            originalExecutionAuthority: admission.Token)) events.Add(item);
        var activity = Assert.Single(events, item => item.Kind == ChatStreamEventKind.ToolActivity).ToolActivity;
        var observed = Assert.Single(Assert.IsType<ToolActivity>(activity).InvocationEvidence!);
        Assert.Equal("files_rename", observed.ToolName);
        Assert.Equal(0, legacy.Calls);
        Assert.Equal(2, provider.Requests);
        if (denyModel)
        {
            Assert.Equal(ToolInvocationObservationStatus.DeniedBeforeDispatch, observed.Status);
            Assert.Equal("MODEL_PERMISSION_DENIED", observed.ReportedFailureCode);
            Assert.Null(observed.ReportedResultSucceeded);
            Assert.Equal(0, admission.Calls); Assert.Empty(admission.Settlements);
            Assert.Equal(0, dispatcher.Executions);
            Assert.Null(dispatcher.ActualDispatch);
        }
        else
        {
            Assert.Equal(ToolInvocationObservationStatus.RuntimeReturned, observed.Status);
            Assert.Equal(ToolRuntimeKind.Workspace.ToString(), observed.RuntimeKey);
            Assert.True(observed.ReportedResultSucceeded);
            Assert.Equal(1, admission.Calls); Assert.Equal(1, dispatcher.Executions);
            Assert.Same(provider.Call, admission.OriginalCall);
            Assert.NotSame(provider.Call, dispatcher.ActualDispatch);
            Assert.IsAssignableFrom<FrozenDictionary<string, JsonElement>>(dispatcher.ActualDispatch!.Arguments);
            Assert.Same(admission.FrozenDispatch, dispatcher.ActualDispatch);
            var settled = Assert.Single(admission.Settlements);
            Assert.Same(provider.Call, settled.Original); Assert.Same(dispatcher.ActualDispatch, settled.Dispatch);
            Assert.Same(dispatcher.Result, settled.Result); Assert.Null(settled.Failure);
            Assert.False(settled.Token.CanBeCanceled);
        }
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
    }

    private sealed class Admission(Guid conversationId, string modelIdentity) : IChatExecutionAdmission, IDisposable
    {
        internal object Token { get; } = new();
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(30));
        internal int Calls;
        internal OllamaToolCall? OriginalCall;
        internal OllamaToolCall? FrozenDispatch;
        internal readonly List<(OllamaToolCall Original, OllamaToolCall Dispatch, WorkspaceToolResult? Result,
            Exception? Failure, CancellationToken Token)> Settlements = [];
        private void Current(object authority, Guid id, string model, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(authority, Token) || id != conversationId || model != modelIdentity ||
                _lifetime.IsCancellationRequested) throw new UnauthorizedAccessException("Synthetic original admission refused.");
        }
        public ValueTask<CancellationToken> GetOriginalLifetimeAsync(object authority, CancellationToken token = default)
        {
            Current(authority, conversationId, modelIdentity, token); return ValueTask.FromResult(_lifetime.Token);
        }
        public ValueTask DemandCurrentAsync(object authority, Guid id, string model, OllamaToolCall? call,
            CancellationToken token = default)
        {
            Current(authority, id, model, token);
            if (call is not null)
            {
                if (OriginalCall is not null || Calls != 0) throw new UnauthorizedAccessException("Only one original dispatch is admitted.");
                Calls++; OriginalCall = call;
                FrozenDispatch = call with { Arguments = call.Arguments.ToFrozenDictionary(
                    item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal) };
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask<OllamaToolCall> GetOriginalDispatchCallAsync(object authority, Guid id, string model,
            OllamaToolCall call, CancellationToken token = default)
        {
            Current(authority, id, model, token);
            if (!ReferenceEquals(call, OriginalCall) || FrozenDispatch is null)
                throw new UnauthorizedAccessException("The same original call is required.");
            return ValueTask.FromResult(FrozenDispatch);
        }
        public ValueTask CompleteOriginalCallAsync(object authority, Guid id, string model, OllamaToolCall original,
            OllamaToolCall dispatch, WorkspaceToolResult? result, Exception? failure, CancellationToken token = default)
        {
            Current(authority, id, model, token);
            if (!ReferenceEquals(original, OriginalCall) || !ReferenceEquals(dispatch, FrozenDispatch) ||
                (result is null) == (failure is null))
                throw new UnauthorizedAccessException("The same original outcome is required.");
            Settlements.Add((original, dispatch, result, failure, token)); return ValueTask.CompletedTask;
        }
        public void Dispose() => _lifetime.Dispose();
    }
    private sealed class Dispatcher(Admission admission, Guid conversation, string model) : IWorkspaceOriginalToolDispatcher
    {
        internal int Executions;
        internal OllamaToolCall? ActualDispatch;
        internal WorkspaceToolResult Result { get; } = new(new ToolActivity(Guid.NewGuid(), "Synthetic rename",
            "Synthetic owner returned", true, TimeSpan.Zero, DateTimeOffset.UtcNow), "Synthetic result");
        public ValueTask<IReadOnlyList<OllamaToolDefinition>> GetOriginalDefinitionsAsync(object authority, Guid id,
            string identity, IReadOnlyCollection<ActiveCapability> capabilities, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(authority, admission.Token) || id != conversation || identity != model)
                throw new UnauthorizedAccessException("The original discovery context is required.");
            return ValueTask.FromResult<IReadOnlyList<OllamaToolDefinition>>([
                new("files_rename", "Synthetic original owning route", new Dictionary<string, object>(), [])]);
        }
        public ValueTask<WorkspaceToolResult> ExecuteOriginalAsync(object authority, Guid id, string identity,
            OllamaToolCall dispatch, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(authority, admission.Token) || id != conversation || identity != model ||
                !ReferenceEquals(dispatch, admission.FrozenDispatch)) throw new UnauthorizedAccessException("The original dispatch is required.");
            Executions++; ActualDispatch = dispatch; return ValueTask.FromResult(Result);
        }
    }
    private sealed class Provider(ModelDescriptor model) : IOllamaClient
    {
        internal int Requests;
        internal OllamaToolCall Call { get; } = new("files_rename", new Dictionary<string, JsonElement>());
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]); }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        { await Task.CompletedTask; token.ThrowIfCancellationRequested(); yield return "Synthetic completed"; }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests++;
            return Task.FromResult(Requests == 1 ? new OllamaToolResponse("", [Call]) : new("Synthetic completed", []));
        }
    }
    private sealed class PolicyStore(ModelPermissionPolicy policy) : IModelPermissionStore
    {
        public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(policy); }
        public Task SavePolicyAsync(ModelPermissionPolicy value, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Safety : IConversationSafetyService
    {
        public Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid id, CancellationToken token) =>
            Task.FromResult(new ConversationSafetySnapshot(id, 0, ConversationSafetyState.Active, null, 0));
        public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid id, ConfirmedSafetyFlag flag, CancellationToken token) =>
            throw new NotSupportedException();
        public Task EnsureMayActAsync(Guid id, string operation, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }
    private sealed class Conversations : IConversationRepository
    {
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken token) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) => Task.CompletedTask;
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class LegacyTools : IWorkspaceToolService
    {
        internal int Calls;
        private Exception Refused() { Calls++; return new InvalidOperationException("No local workspace was selected."); }
        public string ResolveWorkspacePath(string root, string path) => throw Refused();
        public Task<string> ReadTextAsync(string root, string path, CancellationToken token) => throw Refused();
        public Task WriteTextAtomicAsync(string root, string path, string content, CancellationToken token) => throw Refused();
        public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string pattern, CancellationToken token) => throw Refused();
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) => throw Refused();
    }
    private sealed class UnavailableComputer : IComputerToolService
    {
        public bool IsSupported => false;
        public Task<string> SnapshotAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<string> ListWindowsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<string> LaunchAppAsync(string name, CancellationToken token) => throw new NotSupportedException();
        public Task<string> FocusWindowAsync(string title, CancellationToken token) => throw new NotSupportedException();
        public Task<string> InvokeAsync(string title, string name, string id, CancellationToken token) => throw new NotSupportedException();
        public Task<string> ClickAsync(string title, int x, int y, string button, CancellationToken token) => throw new NotSupportedException();
        public Task<string> TypeAsync(string title, string text, CancellationToken token) => throw new NotSupportedException();
        public Task<string> PressAsync(string title, string keys, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CloseWindowAsync(string title, CancellationToken token) => throw new NotSupportedException();
    }
}
