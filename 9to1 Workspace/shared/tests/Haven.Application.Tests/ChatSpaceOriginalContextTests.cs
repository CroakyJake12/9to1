using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

// Controlled issuer/recipient tests of the actual owning Chat consumer. These do not stand
// in for the native Notes/Home approval window or physical local-provider acceptance.
public sealed partial class ChatPersistentMemoryRequestConstraintTests
{
    [Fact]
    public async Task Captured_closed_corpus_reaches_actual_chat_request_without_legacy_history_or_memory()
    {
        var snapshot = ContextSnapshot("actual reviewed notebook body", sourceOnly: true);
        var rig = new ContextRig(new ContextPolicySource(snapshot));
        await rig.SendAsync();
        var request = Assert.Single(rig.Transport.Requests);
        Assert.Contains("actual reviewed notebook body", request.SystemPrompt ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("UNQUALIFIED_HISTORY_SENTINEL", string.Join(" ", request.Messages.Select(value => value.Content)), StringComparison.Ordinal);
        Assert.Equal("Hello", Assert.Single(request.Messages).Content);
        Assert.Equal(snapshot.CapturedContext!.Sha256, request.Options!.RequestedSpaceContextSha256);
        Assert.False(request.Options.RequestedRoutingConstraints!.AllowCloud);
        Assert.False(request.Options.RequestedContextConstraints!.AllowPersistentMemoryRead);
        Assert.Equal(0, rig.LegacyMemory.Reads);
        Assert.True(rig.Source.Validations > 0);
    }

    [Fact]
    public void Denied_selection_cannot_be_relabelled_as_captured_source()
    {
        var snapshot = ContextSnapshot("reviewed source", includeSelection: false);
        var selection = new OriginalSpaceContextItem(Guid.NewGuid(), OriginalSpaceContextItemKind.CurrentSelection,
            "overlay", "explicit-selection", "1", "Selection", "PRIVATE_SELECTION_SENTINEL");
        var invalid = new OriginalSpacePolicySnapshot(snapshot.Policy, snapshot.Lineage,
            new(snapshot.Lineage.SpaceId, "Original Space configuration", [selection]));
        var error = Assert.Throws<SpaceRequestPolicyBlockedException>(() => SpaceRequestPolicyConstraints.BindContext(new(), invalid));
        Assert.Equal("SPACE_CONTEXT_SELECTION_DENIED", error.Code);
    }

    [Fact]
    public void Real_item_count_cannot_exceed_a_zero_retrieval_budget()
    {
        var snapshot = ContextSnapshot("reviewed source", maximum: 0);
        var error = Assert.Throws<SpaceRequestPolicyBlockedException>(() => SpaceRequestPolicyConstraints.BindContext(new(), snapshot));
        Assert.Equal("SPACE_CONTEXT_BUDGET_EXCEEDED", error.Code);
        var empty = new OriginalSpacePolicySnapshot(snapshot.Policy, snapshot.Lineage,
            new(snapshot.Lineage.SpaceId, "Original Space configuration", [], omittedSourceCount: 1));
        var options = SpaceRequestPolicyConstraints.BindContext(new(), empty);
        Assert.Equal(empty.CapturedContext!.Sha256, options.RequestedSpaceContextSha256);
        Assert.Equal(1, empty.CapturedContext.OmittedSourceCount);
    }

    [Fact]
    public async Task Foreign_configured_source_is_refused_before_actual_transport()
    {
        var snapshot = ContextSnapshot("reviewed source");
        var foreign = snapshot.CapturedContext!.Items[0] with { ContextId = Guid.NewGuid() };
        snapshot = new(snapshot.Policy, snapshot.Lineage, new(snapshot.Lineage.SpaceId, "Original Space configuration", [foreign]));
        var rig = new ContextRig(new ContextPolicySource(snapshot));
        var error = await Record.ExceptionAsync(() => rig.SendAsync());
        Assert.NotNull(error);
        Assert.True(ContainsSpaceCode(error!, "SPACE_CONTEXT_SOURCE_CHANGED"));
        Assert.Empty(rig.Transport.Requests);
        Assert.Equal(0, rig.LegacyMemory.Reads);
    }

    [Fact]
    public async Task Cold_digest_requires_exact_original_source_content_in_fresh_capture()
    {
        var snapshot = ContextSnapshot("first reviewed source");
        var source = new ContextPolicySource(snapshot);
        var rig = new ContextRig(source);
        await rig.SendAsync();
        var first = Assert.Single(rig.Transport.Requests).Options!;
        var payload = JsonSerializer.Serialize(first);
        using var json = JsonDocument.Parse(payload);
        Assert.Equal(snapshot.CapturedContext!.Sha256, json.RootElement.GetProperty(nameof(GenerationOptions.RequestedSpaceContextSha256)).GetString());
        Assert.False(json.RootElement.TryGetProperty(nameof(GenerationOptions.OriginalSpacePolicy), out _));
        var restored = JsonSerializer.Deserialize<GenerationOptions>(payload)!;
        var changed = snapshot.CapturedContext.Items[0] with { Text = "different source body under same policy revision" };
        source.Snapshot = new(snapshot.Policy, snapshot.Lineage, new(snapshot.Lineage.SpaceId, "Original Space configuration", [changed]));
        var error = await Record.ExceptionAsync(() => rig.SendAsync(restored));
        Assert.NotNull(error);
        Assert.True(ContainsSpaceCode(error!, "SPACE_CONTEXT_CHANGED"));
        Assert.Single(rig.Transport.Requests);
    }

    [Fact]
    public async Task Captured_private_content_cannot_use_a_cloud_physical_recipient()
    {
        var rig = new ContextRig(new ContextPolicySource(ContextSnapshot("private reviewed source")), localRecipient: false);
        var error = await Record.ExceptionAsync(() => rig.SendAsync());
        Assert.NotNull(error);
        Assert.True(ContainsSpaceCode(error!, "SPACE_CONTEXT_LOCAL_PROVIDER_REQUIRED"));
        Assert.Empty(rig.Transport.Requests);
    }

    [Fact]
    public async Task Original_context_source_refusal_at_dispatch_prevents_physical_body()
    {
        var source = new ContextPolicySource(ContextSnapshot("reviewed source"));
        var rig = new ContextRig(source);
        rig.Transport.BeforePhysicalDispatch = () => source.Revoked = true;
        var error = await Record.ExceptionAsync(() => rig.SendAsync());
        Assert.NotNull(error);
        Assert.True(ContainsSpaceCode(error!, "SPACE_CONTEXT_SOURCE_REVOKED"));
        Assert.Empty(rig.Transport.Requests);
    }

    [Fact]
    public void Captured_context_withholds_tools_without_qualified_result_or_budget_contract()
    {
        var snapshot = ContextSnapshot("reviewed source");
        var definition = new OllamaToolDefinition("read_file", "Read", new Dictionary<string, object>(), []);
        ActiveCapability[] active = [new("read-file", "Read", "", "", "workspace.read-file", "Files")];
        var plan = ToolAvailabilityPlanner.Default.Create(new(HavenMode.Tasks, "/fixture", active,
            PermissionMode.FullAccess, PermissionMode.FullAccess, PermissionMode.FullAccess, false, false, false, false),
            new([definition], [], [], [], [], []));
        var selection = SpaceRequestPolicyConstraints.IntersectTools(snapshot, plan, plan.Definitions, active, [], new(["read_file"]));
        Assert.Empty(selection.AllowedToolNames);
        Assert.Equal("{\"Temperature\":0.7,\"ContextLimit\":32768,\"ActionLimit\":24}", JsonSerializer.Serialize(new GenerationOptions()));
    }

    private static OriginalSpacePolicySnapshot ContextSnapshot(string text, bool sourceOnly = false,
        bool includeSelection = false, int? maximum = null)
    {
        var id = Guid.NewGuid(); var contextId = Guid.NewGuid(); var notebookId = Guid.NewGuid().ToString("D");
        var reference = new SpaceContextReference(contextId, SpaceContextReferenceKind.BoardPage, "boards", notebookId,
            "7", SpaceContextPermission.Read, SpaceContextIndexState.Ready, true, DateTimeOffset.UtcNow);
        var policy = new EffectiveSpacePolicy(id, new(null), new(SpaceWebAccessPolicy.Disabled, sourceOnly, includeSelection, maximum),
            new([SpaceMemoryScope.Conversation]), [], [reference], [id]);
        var context = new OriginalSpaceCapturedContext(id, "Original Space configuration",
            [new(contextId, OriginalSpaceContextItemKind.ConfiguredSource, "boards", notebookId, "7", "Actual notebook", text)]);
        return new(policy, new(id, [new(id, 1)]), context);
    }

    private sealed class ContextPolicySource(OriginalSpacePolicySnapshot snapshot) : IOriginalSpaceRequestPolicySource
    {
        private sealed record Input(ContextPolicySource Owner, Guid ConversationId, OriginalSpacePolicySnapshot Snapshot) : IOriginalSpaceRequestPolicyInput;
        internal OriginalSpacePolicySnapshot Snapshot = snapshot;
        internal int Validations;
        internal bool Revoked;
        public Task<IOriginalSpaceRequestPolicyInput> CaptureAsync(Conversation conversation, Action<Action> acquire, CancellationToken token) =>
            Task.FromResult<IOriginalSpaceRequestPolicyInput>(new Input(this, conversation.Id, Snapshot));
        public async Task<OriginalSpacePolicySnapshot> ReadAsync(IOriginalSpaceRequestPolicyInput input, Conversation conversation,
            Action<Action> acquire, CancellationToken token)
        { await ValidateAsync(input, conversation, acquire, token); return ((Input)input).Snapshot; }
        public Task ValidateAsync(IOriginalSpaceRequestPolicyInput input, Conversation conversation, Action<Action> acquire, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Validations++;
            if (input is not Input actual || !ReferenceEquals(actual.Owner, this) || actual.ConversationId != conversation.Id ||
                actual.Snapshot.Lineage.SpaceId != conversation.SpaceId)
                throw new SpaceRequestPolicyBlockedException("SPACE_CONTEXT_INPUT_NOT_ORIGINAL", "Foreign controlled context input.");
            if (Revoked) throw new SpaceRequestPolicyBlockedException("SPACE_CONTEXT_SOURCE_REVOKED", "The original controlled context source was revoked.");
            return Task.CompletedTask;
        }
    }

    private sealed class ContextRig
    {
        private readonly ChatSessionService _chat;
        private readonly Conversation _conversation;
        private readonly ModelDescriptor _model;
        internal readonly ContextPolicySource Source;
        internal readonly ContextTransport Transport;
        internal readonly MemorySource LegacyMemory = new();
        internal ContextRig(ContextPolicySource source, bool localRecipient = true)
        {
            Source = source;
            var now = DateTimeOffset.UtcNow;
            _conversation = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Controlled context", null, null,
                false, false, now, now, SpaceId: source.Snapshot.Lineage.SpaceId);
            _model = new("space-context-model", 1, "fixture", "fixture", "fixture", new HashSet<ToolCapability> { ToolCapability.Text }, now);
            Transport = new(_model, localRecipient);
            _chat = new(new ContextConversations(_conversation), Transport, new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()), memorySource: LegacyMemory);
            _chat.BindOriginalSpacePolicySource(source);
        }
        internal async Task SendAsync(GenerationOptions? options = null)
        {
            options = (options ?? new()) with { RequestedRoutingConstraints = options?.RequestedRoutingConstraints ?? new(AllowCloud: false) };
            var original = _chat.CreateOriginalOrdinaryConversation(_conversation, "Hello", _model, EffortLevel.Medium,
                "Fixture", "", options, TestContext.Current.CancellationToken, body => body());
            var failures = new List<Exception>();
            try { await foreach (var _ in original.ConsumeOriginal()) { } }
            catch (Exception error) { failures.Add(error); }
            try { await original.JoinOriginalSourcesAsync(); }
            catch (Exception error) { failures.Add(error); }
            if (failures.Count > 0) throw new AggregateException("Actual controlled context request and settlement failed.", failures);
        }
    }
    private sealed class ContextConversations(Conversation original) : IConversationRepository
    {
        private readonly List<ChatMessage> _messages = [new(Guid.NewGuid(), original.Id, MessageRole.User,
            "UNQUALIFIED_HISTORY_SENTINEL", null, null, null, DateTimeOffset.UtcNow.AddMinutes(-1))];
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>([original]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<Conversation?>(id == original.Id ? original : null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<ChatMessage>>(_messages.ToArray());
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken token) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) { _messages.Add(message); return Task.CompletedTask; }
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new InvalidOperationException("No delete was requested.");
    }
    private sealed class ContextTransport(ModelDescriptor model, bool localRecipient) : IOriginalPrivateContextModelClient
    {
        internal readonly List<OllamaChatRequest> Requests = [];
        internal Action? BeforePhysicalDispatch;
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            BeforePhysicalDispatch?.Invoke(); BeforePhysicalDispatch = null;
            await OriginalPrivateContextDispatch.ValidateProviderAsync(request.Options, new ContextRecipient(localRecipient), token);
            Requests.Add(request); yield return "Controlled context reply.";
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new InvalidOperationException("No auxiliary provider call is authorized.");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new InvalidOperationException("No canonical tool authority exists in this fixture.");
    }
    private sealed class ContextRecipient(bool local) : IOriginalPrivateContextModelProvider
    {
        public string Id => "fixture-recipient";
        public string DisplayName => "Controlled physical recipient";
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => local;
        public bool CanManageModels => false;
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => throw new InvalidOperationException("No discovery requested.");
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => throw new InvalidOperationException("No discovery requested.");
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new InvalidOperationException("Guard argument only.");
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new InvalidOperationException("Guard argument only.");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new InvalidOperationException("Guard argument only.");
    }
}
