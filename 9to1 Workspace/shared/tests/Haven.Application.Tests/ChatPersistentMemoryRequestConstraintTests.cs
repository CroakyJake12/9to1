using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

/// <summary>Runs the actual shared Chat pipeline against controlled source/transport owners.
/// These controls prove request opt-out and preserved source refusal, not an installed app or
/// a new source/actor permission grant.</summary>
public sealed partial class ChatPersistentMemoryRequestConstraintTests
{
    [Fact]
    public async Task Memory_opt_out_never_calls_existing_reader_or_injects_its_record()
    {
        var source = new MemorySource();
        var rig = new Rig(source);

        await rig.SendAsync(new GenerationOptions { RequestedContextConstraints = new(false) });

        Assert.Equal(0, source.Reads);
        var original = Assert.Single(rig.Provider.Requests);
        Assert.DoesNotContain(MemorySource.Sentinel, original.SystemPrompt ?? "", StringComparison.Ordinal);
        Assert.False(original.Options!.RequestedContextConstraints!.AllowPersistentMemoryRead);
    }

    [Fact]
    public async Task Unconstrained_ordinary_chat_preserves_existing_memory_read_and_injection()
    {
        var source = new MemorySource();
        var rig = new Rig(source);

        await rig.SendAsync(null);

        Assert.Equal(1, source.Reads);
        Assert.Contains(MemorySource.Sentinel, Assert.Single(rig.Provider.Requests).SystemPrompt ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_true_preserves_the_actual_source_refusal_and_never_dispatches()
    {
        var refusal = new UnauthorizedAccessException("Controlled original memory policy refuses this reader.");
        var source = new MemorySource { Refusal = refusal };
        var rig = new Rig(source);

        var observed = await Record.ExceptionAsync(() => rig.SendAsync(
            new GenerationOptions { RequestedContextConstraints = new(true) }));

        Assert.Same(refusal, observed);
        Assert.Equal(1, source.Reads);
        Assert.Empty(rig.Provider.Requests);
    }

    [Fact]
    public async Task Explicit_true_does_not_widen_the_existing_source_filtered_result()
    {
        var source = new MemorySource { ReturnEmpty = true };
        var rig = new Rig(source);

        await rig.SendAsync(new GenerationOptions { RequestedContextConstraints = new(true) });

        Assert.Equal(1, source.Reads);
        Assert.DoesNotContain(MemorySource.Sentinel, Assert.Single(rig.Provider.Requests).SystemPrompt ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Old_generation_options_payload_stays_unchanged_and_opt_out_survives_round_trip()
    {
        const string previous = "{\"Temperature\":0.7,\"ContextLimit\":32768,\"ActionLimit\":24}";
        Assert.Equal(previous, JsonSerializer.Serialize(new GenerationOptions()));
        Assert.Null(JsonSerializer.Deserialize<GenerationOptions>(previous)!.RequestedContextConstraints);
        var original = new GenerationOptions { RequestedContextConstraints = new(false) };
        var restored = JsonSerializer.Deserialize<GenerationOptions>(JsonSerializer.Serialize(original))!;
        Assert.False(restored.RequestedContextConstraints!.AllowPersistentMemoryRead);
    }

    private sealed class Rig
    {
        private readonly Conversation _conversation;
        private readonly ChatSessionService _chat;
        internal ChatSessionService OriginalChat => _chat;
        public readonly ModelDescriptor Model;
        public readonly Provider Provider;
        internal readonly Safety OriginalSafety = new();
        internal readonly List<ToolActivity> ToolActivities = [];

        internal Rig(MemorySource actualSource, ScopedMemorySource? scoped = null, string? actualWorkspaceRoot = null)
        {
            var now = DateTimeOffset.UtcNow;
            _conversation = new(Guid.NewGuid(), actualWorkspaceRoot is null ? HavenMode.Chat : HavenMode.Tasks, ConversationKind.Chat,
                "Controlled ordinary conversation", null, null, false, false, now, now);
            Model = new("controlled-memory-model", 1, "controlled", "controlled", "controlled",
                actualWorkspaceRoot is null ? new HashSet<ToolCapability> { ToolCapability.Text }
                    : new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, now);
            Provider = new(Model, actualWorkspaceRoot is not null);
            _chat = new(new Conversations(), Provider, new CapabilityPreflightService(), OriginalSafety,
                new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()),
                memorySource: actualSource, originalPersistentMemorySource: scoped);
        }

        internal async Task SendOwnedAsync(GenerationOptions options, Action<Action>? scope = null)
        {
            var original = _chat.CreateOriginalOrdinaryConversation(_conversation, "Hello", Model,
                EffortLevel.Medium, "Controlled", "", options, TestContext.Current.CancellationToken,
                scope ?? (body => body()));
            var failures = new List<Exception>();
            try { await foreach (var _ in original.ConsumeOriginal()) { } }
            catch (Exception cause) { failures.Add(cause); }
            try { await original.JoinOriginalSourcesAsync(); }
            catch (Exception cause) { failures.Add(cause); }
            if (failures.Count != 0) throw new AggregateException("Actual owned memory source and cleanup failed.", failures);
        }

        internal async Task SendToolSelectionAsync(string actualWorkspaceRoot, GenerationOptions? options)
        {
            ActiveCapability[] observed = [new("read-file", "Read", "", "", "workspace.read-file", "Files")];
            await foreach (var _ in _chat.SendAsync(_conversation, "Inspect the selected source", Model, EffortLevel.Medium,
                observed, "Controlled", "", DuoMode.Solo, actualWorkspaceRoot, null, null, null,
                TestContext.Current.CancellationToken, generationOptions: options,
                filePermission: PermissionMode.Ask, commandPermission: PermissionMode.Ask,
                browserPermission: PermissionMode.Ask, explicitCapabilities: [ToolCapability.Tools],
                availableCapabilities: observed, taskExecutionIntent: TaskRunExecutionIntent.OrdinaryConversation))
            { if (_.ToolActivity is { } activity) ToolActivities.Add(activity); }
        }

        internal async Task SendAsync(GenerationOptions? options)
        {
            await foreach (var _ in _chat.SendAsync(_conversation, "Hello", Model, EffortLevel.Medium,
                [], "Controlled", "", DuoMode.Solo, null, null, null, null,
                TestContext.Current.CancellationToken, generationOptions: options,
                taskExecutionIntent: TaskRunExecutionIntent.OrdinaryConversation)) { }
        }
    }

    private sealed class MemorySource : IMemoryQuerySource
    {
        internal const string Sentinel = "controlled-private-memory-sentinel";
        internal int Reads;
        internal Exception? Refusal;
        internal bool ReturnEmpty;

        public Task<IReadOnlyList<KnowledgeRecord>> GetActiveLearnMeAsync(int limit, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Reads);
            if (Refusal is { } original) return Task.FromException<IReadOnlyList<KnowledgeRecord>>(original);
            if (ReturnEmpty) return Task.FromResult<IReadOnlyList<KnowledgeRecord>>([]);
            var now = DateTimeOffset.UtcNow;
            KnowledgeRecord record = new(Guid.NewGuid(), KnowledgeCategory.LearnMe, "controlled", Sentinel,
                Sentinel, KnowledgePrivacyClass.Normal, 1, true, now, now, null, "controlled source", []);
            return Task.FromResult<IReadOnlyList<KnowledgeRecord>>([record]);
        }
    }

    private sealed class Provider(ModelDescriptor model, bool allowControlledTools = false) : IOllamaClient
    {
        internal readonly List<OllamaChatRequest> Requests = [];
        internal readonly List<OllamaToolRequest> ToolRequests = [];
        internal OllamaToolCall? NextControlledToolCall;
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [EnumeratorCancellation] CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests.Add(request);
            await Task.CompletedTask; yield return "Controlled response.";
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

    private sealed class Conversations : IConversationRepository
    {
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken token) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) => Task.CompletedTask;
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new InvalidOperationException("Unexpected delete.");
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
