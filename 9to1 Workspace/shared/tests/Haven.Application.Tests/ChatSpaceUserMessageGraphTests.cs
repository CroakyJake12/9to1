using Haven.Application.Automations;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

public sealed partial class ChatPersistentMemoryRequestConstraintTests
{
    [Fact]
    public async Task Space_user_message_graph_uses_shared_registration_and_preserves_unicode_input_identity()
    {
        var id = Guid.NewGuid();
        var layout = UserMessageLayout(2);
        var graph = SpaceUserMessageGraph.Compile(id, 7, layout);
        var input = new ChatMessage(Guid.NewGuid(), Guid.NewGuid(), MessageRole.User,
            "A👩‍💻e\u0301Z", null, null, null, DateTimeOffset.UtcNow);

        var result = await graph.ExecuteAsync(input, TestContext.Current.CancellationToken);

        Assert.Equal("A👩‍💻", result.DerivedText);
        Assert.Equal("A👩‍💻e\u0301Z", input.Content);
        Assert.Equal(input.Id, result.UserMessageId);
        Assert.Equal(id, result.SpaceId);
        Assert.Equal(7, result.SpaceRevision);
        Assert.Equal(layout.GraphId, result.GraphId);
        Assert.Equal(2, result.OmittedTextElements);
        Assert.Equal(3, result.Trace.Count);
        Assert.All(result.Trace, item => Assert.Equal("Succeeded", item.State));
        Assert.NotEqual(result.InputSha256, result.OutputSha256);
        Assert.Equal(3, CanonicalAutomationCoreNodes.CreateRegistry().ListNodeTypes(SpaceUserMessageGraph.ProfileId).Count);
        Assert.Equal(2, CanonicalAutomationCoreNodes.CreateRegistry().ListNodeTypes(CanonicalAutomationCoreNodes.ProfileId).Count);
    }

    [Fact]
    public async Task Space_graph_compilation_snapshots_editable_configuration_and_repeated_execution_keeps_original_input()
    {
        var layout = UserMessageLayout(3);
        var graph = SpaceUserMessageGraph.Compile(Guid.NewGuid(), 2, layout);
        ((Dictionary<string, string>)layout.Nodes[1].Metadata)["maximumTextElements"] = "1";
        var first = new ChatMessage(Guid.NewGuid(), Guid.NewGuid(), MessageRole.User,
            "abcdef", null, null, null, DateTimeOffset.UtcNow);
        var second = first with { Id = Guid.NewGuid(), Content = "XYZ123" };

        var a = await graph.ExecuteAsync(first, TestContext.Current.CancellationToken);
        var b = await graph.ExecuteAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal("abc", a.DerivedText);
        Assert.Equal("XYZ", b.DerivedText);
        Assert.Equal(first.Id, a.UserMessageId);
        Assert.Equal(second.Id, b.UserMessageId);
        Assert.NotEqual(a.InputSha256, b.InputSha256);
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("port-type")]
    [InlineData("port-direction")]
    [InlineData("branch")]
    [InlineData("orphan")]
    [InlineData("configuration")]
    [InlineData("cycle")]
    public void Space_graph_rejects_unimplemented_nodes_and_invalid_typed_flow_before_execution(string defect)
    {
        var layout = UserMessageLayout(3);
        var nodes = layout.Nodes.ToArray();
        var edges = layout.Edges.ToArray();
        if (defect == "unsupported") nodes[1] = nodes[1] with { TypeId = "spaces.model.imaginary" };
        if (defect == "port-type") nodes[1] = nodes[1] with { Ports = nodes[1].Ports.Select(port => port with { DataType = "flow" }).ToArray() };
        if (defect == "port-direction") nodes[0] = nodes[0] with { Ports = [nodes[0].Ports[0] with { Direction = (SpaceLayoutPortDirection)99 }] };
        if (defect == "branch") edges[0] = edges[0] with { Branch = "true" };
        if (defect == "orphan") nodes = [.. nodes, nodes[1] with { Id = Guid.NewGuid() }];
        if (defect == "configuration") nodes[1] = nodes[1] with { Metadata = new Dictionary<string, string> { ["maximumTextElements"] = "0" } };
        if (defect == "cycle") edges = [edges[0], edges[1] with { ToNodeId = nodes[0].Id, ToPortId = "out" }];

        var error = Assert.Throws<SpaceRequestPolicyBlockedException>(() =>
            SpaceUserMessageGraph.Compile(Guid.NewGuid(), 1, layout with { Nodes = nodes, Edges = edges }));

        Assert.Equal("SPACE_USER_MESSAGE_GRAPH_INVALID", error.Code);
    }

    [Fact]
    public async Task Space_graph_actual_cancellation_is_preserved_and_not_reported_as_derived_output()
    {
        var graph = SpaceUserMessageGraph.Compile(Guid.NewGuid(), 1, UserMessageLayout(2));
        var input = new ChatMessage(Guid.NewGuid(), Guid.NewGuid(), MessageRole.User,
            "abc", null, null, null, DateTimeOffset.UtcNow);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => graph.ExecuteAsync(input, cancelled.Token));
        Assert.Equal(cancelled.Token, error.CancellationToken);
    }

    [Fact]
    public async Task Space_graph_real_chat_request_uses_derived_text_while_saved_original_and_trace_keep_identity()
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Message derivation", cancellationToken: TestContext.Current.CancellationToken);
        var source = new GraphPolicySource(new ControlledSpacePolicySource(files.Registry), UserMessageLayout(3));
        var rig = new GraphRequestRig(source, space.Id);

        await rig.SendAsync("Hello");

        var wire = Assert.Single(rig.Transport.Requests);
        Assert.Equal("Hel", Assert.Single(wire.Messages.Where(message => message.Role == "user")).Content);
        var persisted = Assert.Single(rig.Repository.Messages.Where(message => message.Role == MessageRole.User));
        Assert.Equal("Hello", persisted.Content);
        var derivation = Assert.Single(rig.Events.Values.Where(item => item.ComponentId == "spaces.user-message-graph"));
        Assert.Equal(persisted.Id.ToString("D"), derivation.SafeMetadata!["userMessageId"]);
        Assert.Equal(space.Id.ToString("D"), derivation.SafeMetadata["spaceId"]);
        Assert.NotEqual(derivation.SafeMetadata["inputSha256"], derivation.SafeMetadata["outputSha256"]);
        Assert.DoesNotContain("Hello", derivation.SafeMetadata["trace"], StringComparison.Ordinal);
        Assert.True(source.Inner.Validations > 0);
        files.Passed = true;
    }

    [Fact]
    public async Task Space_graph_revision_change_at_actual_model_dispatch_blocks_transport_without_rewriting_input()
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Message graph revision", cancellationToken: TestContext.Current.CancellationToken);
        var source = new GraphPolicySource(new ControlledSpacePolicySource(files.Registry), UserMessageLayout(3));
        var rig = new GraphRequestRig(source, space.Id);
        rig.Transport.BeforePhysicalDispatch = async () =>
        {
            await files.Registry.UpdateAsync(space with { Instructions = "Changed after derivation" }, space.Revision,
                TestContext.Current.CancellationToken);
        };

        var error = await Record.ExceptionAsync(() => rig.SendAsync("Hello"));

        Assert.NotNull(error);
        Assert.True(ContainsSpaceCode(error, "SPACE_POLICY_CHANGED"));
        Assert.Empty(rig.Transport.Requests);
        Assert.Equal("Hello", Assert.Single(rig.Repository.Messages.Where(message => message.Role == MessageRole.User)).Content);
        files.Passed = true;
    }

    private static SpaceLayoutDocument UserMessageLayout(int maximum)
    {
        SpaceLayoutNode Node(string type, params SpaceLayoutPort[] ports) => new(Guid.NewGuid(), "Logic", type)
        { TypeId = type, Ports = ports };
        var source = Node(SpaceUserMessageGraph.SourceType, new SpaceLayoutPort("out", "Text", SpaceLayoutPortDirection.Output, "string", false));
        var truncate = Node(SpaceUserMessageGraph.TruncateType,
            new SpaceLayoutPort("in", "Text", SpaceLayoutPortDirection.Input, "string", false),
            new SpaceLayoutPort("out", "Text", SpaceLayoutPortDirection.Output, "string", false)) with
        { Metadata = new Dictionary<string, string> { ["maximumTextElements"] = maximum.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
        var output = Node(SpaceUserMessageGraph.OutputType, new SpaceLayoutPort("in", "Text", SpaceLayoutPortDirection.Input, "string", false));
        return new([source, truncate, output],
            [new(Guid.NewGuid(), source.Id, "out", truncate.Id, "in"), new(Guid.NewGuid(), truncate.Id, "out", output.Id, "in")])
        { GraphId = Guid.NewGuid(), Revision = 1, State = SpaceLayoutRevisionState.Active };
    }

    private sealed class GraphPolicySource(ControlledSpacePolicySource inner, SpaceLayoutDocument layout) : IOriginalSpaceRequestPolicySource
    {
        internal ControlledSpacePolicySource Inner => inner;
        public Task<IOriginalSpaceRequestPolicyInput> CaptureAsync(Conversation conversation, Action<Action> acquire, CancellationToken token) =>
            inner.CaptureAsync(conversation, acquire, token);
        public async Task<OriginalSpacePolicySnapshot> ReadAsync(IOriginalSpaceRequestPolicyInput input, Conversation conversation, Action<Action> acquire, CancellationToken token)
        {
            var original = await inner.ReadAsync(input, conversation, acquire, token);
            return new(original.Policy, original.Lineage, original.CapturedContext,
                SpaceUserMessageGraph.Compile(original.Policy.SpaceId, original.Lineage.Ancestors[^1].Revision, layout));
        }
        public Task ValidateAsync(IOriginalSpaceRequestPolicyInput input, Conversation conversation, Action<Action> acquire, CancellationToken token) =>
            inner.ValidateAsync(input, conversation, acquire, token);
    }
    private sealed class GraphRequestRig
    {
        private readonly ChatSessionService _chat;
        private readonly Conversation _conversation;
        private readonly ModelDescriptor _model;
        internal readonly SpacePolicyTransport Transport;
        internal readonly GraphConversations Repository = new();
        internal readonly GraphEvents Events = new();
        internal GraphRequestRig(IOriginalSpaceRequestPolicySource source, Guid spaceId)
        {
            var now = DateTimeOffset.UtcNow;
            _conversation = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Graph request", null, null,
                false, false, now, now, SpaceId: spaceId);
            _model = new("space-policy-model", 1, "fixture", "fixture", "fixture", new HashSet<ToolCapability> { ToolCapability.Text }, now);
            Transport = new(_model);
            _chat = new(Repository, Transport, new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()), executionEvents: Events);
            _chat.BindOriginalSpacePolicySource(source);
        }
        internal async Task SendAsync(string text)
        {
            var original = _chat.CreateOriginalOrdinaryConversation(_conversation, text, _model, EffortLevel.Medium,
                "Fixture", "", new() { RequestedRoutingConstraints = new(AllowCloud: true) },
                TestContext.Current.CancellationToken, body => body());
            var failures = new List<Exception>();
            try { await foreach (var _ in original.ConsumeOriginal()) { } } catch (Exception cause) { failures.Add(cause); }
            try { await original.JoinOriginalSourcesAsync(); } catch (Exception cause) { failures.Add(cause); }
            if (failures.Count != 0) throw new AggregateException(failures);
        }
    }
    private sealed class GraphEvents : IExecutionEventSink
    {
        internal readonly List<ExecutionEvent> Values = [];
        public bool TryPublish(ExecutionEvent value) { Values.Add(value); return true; }
    }
    private sealed class GraphConversations : IConversationRepository
    {
        internal readonly List<ChatMessage> Messages = [];
        private Conversation? _conversation;
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(_conversation);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.ToArray());
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken token) { _conversation = conversation; return Task.CompletedTask; }
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) { Messages.Add(message); return Task.CompletedTask; }
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new InvalidOperationException("Unexpected delete.");
    }
}
