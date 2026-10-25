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

    [Fact]
    public async Task More_than_32_small_reviewed_sources_reach_the_actual_transport_with_stable_ids()
    {
        var fixture = CollectionFixture(64, new(131_072));
        foreach (var reference in fixture.Collection.RequestedSources)
            Assert.True(fixture.Collection.TryInclude(Body(reference, "reviewed body " + reference.ContextId)));
        var context = fixture.Collection.Complete();
        Assert.Equal(64, context.Items.Count);
        Assert.Equal(0, context.OmittedSourceCount);
        var snapshot = new OriginalSpacePolicySnapshot(fixture.Policy, fixture.Lineage, context);
        var source = new ContextPolicySource(snapshot);
        var rig = new ContextRig(source);
        await rig.SendAsync(new() { ContextLimit = 262_144 });
        var request = Assert.Single(rig.Transport.Requests);
        Assert.Equal(131_072, source.RequestedBudget!.MaximumUtf8Bytes);
        Assert.All(context.Items, row => Assert.Contains(row.ContextId.ToString(), request.SystemPrompt ?? "", StringComparison.Ordinal));
        Assert.Equal(context.Sha256, request.Options!.RequestedSpaceContextSha256);
    }

    [Fact]
    public void Request_byte_budget_omits_a_whole_large_source_and_still_admits_a_later_small_source()
    {
        var fixture = CollectionFixture(3, new(2_400));
        var sources = fixture.Collection.RequestedSources;
        Assert.True(fixture.Collection.TryInclude(Body(sources[0], "first")));
        Assert.False(fixture.Collection.TryInclude(Body(sources[1], new string('x', 4_000))));
        Assert.True(fixture.Collection.TryInclude(Body(sources[2], "last")));
        var context = fixture.Collection.Complete();
        Assert.Equal(new[] { sources[0].ContextId, sources[2].ContextId }, context.Items.Select(row => row.ContextId));
        var omitted = Assert.Single(context.Omissions);
        Assert.Equal(sources[1].ContextId, omitted.ContextId);
        Assert.Equal(OriginalSpaceContextOmissionReason.RequestByteBudget, omitted.Reason);
        Assert.Equal(1, context.OmittedSourceCount);
        Assert.Equal(1, context.OmittedByByteBudgetCount);
        Assert.True(context.RegisteredByteCount <= 2_400);
        Assert.DoesNotContain(new string('x', 100), context.BuildRegisteredContext(true), StringComparison.Ordinal);
        Assert.Contains("1 source(s) omitted", context.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void Encoded_unicode_and_json_metadata_count_toward_the_actual_byte_allocation()
    {
        var fixture = CollectionFixture(1, new(2_000));
        var reference = Assert.Single(fixture.Collection.RequestedSources);
        var text = new string('漢', 350);
        Assert.True(text.Length < 2_000);
        Assert.False(fixture.Collection.TryInclude(Body(reference, text)));
        var context = fixture.Collection.Complete();
        Assert.Empty(context.Items);
        Assert.Equal(OriginalSpaceContextOmissionReason.RequestByteBudget, Assert.Single(context.Omissions).Reason);
    }

    [Fact]
    public void Item_limit_and_unavailable_owner_have_truthful_separate_outcomes_without_placeholder_bodies()
    {
        var fixture = CollectionFixture(5, new(8_000), maximum: 2);
        Assert.Equal(2, fixture.Collection.RequestedSources.Count);
        Assert.Equal(3, fixture.Collection.OmittedByItemLimit);
        fixture.Collection.OmitUnavailable(fixture.Collection.RequestedSources[0]);
        Assert.True(fixture.Collection.TryInclude(Body(fixture.Collection.RequestedSources[1], "real body")));
        var context = fixture.Collection.Complete();
        Assert.Single(context.Items);
        Assert.Equal(4, context.OmittedSourceCount);
        Assert.Equal(3, context.OmittedByItemLimitCount);
        Assert.Equal(1, context.UnavailableSourceCount);
        Assert.Equal(OriginalSpaceContextOmissionReason.SourceOwnerUnavailable, Assert.Single(context.Omissions).Reason);
        context.ValidateAgainst(new(fixture.Policy, fixture.Lineage, context));
    }

    [Fact]
    public void Incomplete_duplicate_and_foreign_body_outcomes_cannot_complete_a_context_capture()
    {
        var fixture = CollectionFixture(2, new(8_000));
        Assert.Throws<InvalidOperationException>(() => fixture.Collection.Complete());
        var body = Body(fixture.Collection.RequestedSources[0], "first");
        Assert.Throws<InvalidOperationException>(() => fixture.Collection.TryInclude(body with { CanonicalEntityId = Guid.NewGuid().ToString("D") }));
        Assert.True(fixture.Collection.TryInclude(body));
        Assert.Throws<InvalidOperationException>(() => fixture.Collection.TryInclude(body));
        fixture.Collection.OmitUnavailable(fixture.Collection.RequestedSources[1]);
        Assert.Single(fixture.Collection.Complete().Items);
    }

    [Fact]
    public void Configuration_over_the_request_allocation_is_a_structured_refusal_before_any_source_read()
    {
        var error = Assert.Throws<SpaceRequestPolicyBlockedException>(() =>
            new OriginalSpaceContextCollection(Guid.NewGuid(), new string('x', 3_000), [], null, new(2_000)));
        Assert.Equal("SPACE_CONTEXT_REQUEST_BUDGET_EXCEEDED", error.Code);
    }

    [Fact]
    public async Task Legacy_capture_over_a_smaller_request_allocation_is_blocked_before_transport()
    {
        var snapshot = ContextSnapshot(new string('x', 8_000));
        var rig = new ContextRig(new ContextPolicySource(snapshot));
        var error = await Record.ExceptionAsync(() => rig.SendAsync(new() { ContextLimit = 4_096 }));
        Assert.NotNull(error);
        Assert.True(ContainsSpaceCode(error!, "SPACE_CONTEXT_REQUEST_BUDGET_EXCEEDED"));
        Assert.Empty(rig.Transport.Requests);
    }

    [Fact]
    public void Legacy_capture_digest_stays_identical_and_new_capture_binds_omission_decisions()
    {
        var old = ContextSnapshot("same body").CapturedContext!;
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { old.SpaceId, old.Configuration, old.Items, old.OmittedSourceCount }))).ToLowerInvariant();
        Assert.Equal(expected, old.Sha256);
        var fixture = CollectionFixture(2, new(8_000));
        fixture.Collection.OmitUnavailable(fixture.Collection.RequestedSources[0]);
        fixture.Collection.TryInclude(Body(fixture.Collection.RequestedSources[1], "same body"));
        var context = fixture.Collection.Complete();
        var changed = new OriginalSpaceCapturedContext(context.SpaceId, context.Configuration, context.Items,
            context.OmittedSourceCount, context.Budget,
            [context.Omissions[0] with { Reason = OriginalSpaceContextOmissionReason.RequestByteBudget }]);
        Assert.NotEqual(context.Sha256, changed.Sha256);
    }

    private static (OriginalSpaceContextCollection Collection, EffectiveSpacePolicy Policy, OriginalSpacePolicyLineage Lineage)
        CollectionFixture(int count, OriginalSpaceContextBudget budget, int? maximum = null)
    {
        var id = Guid.NewGuid();
        var references = Enumerable.Range(0, count).Select(_ => new SpaceContextReference(Guid.NewGuid(),
            SpaceContextReferenceKind.BoardPage, "boards", Guid.NewGuid().ToString("D"), "7",
            SpaceContextPermission.Read, SpaceContextIndexState.Ready, true, DateTimeOffset.UtcNow)).ToArray();
        var policy = new EffectiveSpacePolicy(id, new(null), new(SpaceWebAccessPolicy.Disabled, true, false, maximum),
            new([SpaceMemoryScope.Conversation]), [], references, [id]);
        return (new(id, "Actual Space configuration", references, maximum, budget), policy, new(id, [new(id, 1)]));
    }
    private static OriginalSpaceContextItem Body(SpaceContextReference reference, string text) =>
        new(reference.ContextId, OriginalSpaceContextItemKind.ConfiguredSource, reference.OwnerAppId,
            reference.CanonicalEntityId, reference.RevisionToken!, "Actual source", text);

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
        internal OriginalSpaceContextBudget? RequestedBudget;
        public Task<IOriginalSpaceRequestPolicyInput> CaptureAsync(Conversation conversation, OriginalSpaceContextBudget budget,
            Action<Action> acquire, CancellationToken token)
        { RequestedBudget = budget; return CaptureAsync(conversation, acquire, token); }
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
