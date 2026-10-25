using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

// Reuse the existing controlled transport/workspace fixture contracts. These are owning
// Chat/registry/dispatch controls, not a claim of native Home/profile or installer acceptance.
public sealed partial class ChatPersistentMemoryRequestConstraintTests
{
    [Fact]
    public async Task Space_request_uses_original_policy_and_never_substitutes_legacy_personal_memory()
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Owned policy", cancellationToken: TestContext.Current.CancellationToken);
        var source = new ControlledSpacePolicySource(files.Registry);
        var rig = new SpacePolicyRig(source, space.Id);
        await rig.SendAsync(null);
        var actual = Assert.Single(rig.Transport.Requests);
        Assert.True(actual.Options!.RequireOriginalSpacePolicy);
        Assert.Equal(space.Id, actual.Options.RequestedSpacePolicyLineage!.SpaceId);
        Assert.NotNull(actual.Options.OriginalSpacePolicy);
        Assert.False(actual.Options.RequestedContextConstraints!.AllowPersistentMemoryRead);
        Assert.Equal(0, rig.LegacyMemory.Reads);
        Assert.DoesNotContain(MemorySource.Sentinel, actual.SystemPrompt ?? "", StringComparison.Ordinal);
        Assert.True(source.Validations > 0);
        // The separate Space guard permits the controlled cloud recipient; UCML's
        // own local-only contract was not relaxed or reused as a Space permission.
        files.Passed = true;
    }

    [Fact]
    public async Task Parent_revision_changed_at_physical_dispatch_prevents_transport_body()
    {
        using var files = new SpacePolicyFiles();
        var parent = await files.Registry.CreateAsync("Parent", cancellationToken: TestContext.Current.CancellationToken);
        var child = await files.Registry.CreateAsync("Child", null, parent.Id, TestContext.Current.CancellationToken);
        var source = new ControlledSpacePolicySource(files.Registry);
        var rig = new SpacePolicyRig(source, child.Id);
        rig.Transport.BeforePhysicalDispatch = async () =>
        {
            await files.Registry.UpdateAsync(parent with { Instructions = "Changed parent policy" }, parent.Revision, TestContext.Current.CancellationToken);
        };
        var error = await Record.ExceptionAsync(() => rig.SendAsync(null));
        Assert.NotNull(error);
        Assert.True(ContainsSpaceCode(error!, "SPACE_POLICY_CHANGED"));
        Assert.Empty(rig.Transport.Requests);
        files.Passed = true;
    }

    [Fact]
    public async Task Provider_constraint_is_checked_against_the_actual_dispatch_provider()
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Provider restricted", cancellationToken: TestContext.Current.CancellationToken);
        space = await files.Registry.UpdateAsync(space with
        {
            ModelPolicy = new SpaceModelPolicy(null, [], "required-provider")
        }, space.Revision, TestContext.Current.CancellationToken);
        var rig = new SpacePolicyRig(new ControlledSpacePolicySource(files.Registry), space.Id, actualProviderId: "different-provider");

        var error = await Record.ExceptionAsync(() => rig.SendAsync(null));

        Assert.NotNull(error);
        Assert.True(ContainsSpaceCode(error!, "SPACE_PROVIDER_POLICY_REQUIRED"));
        Assert.Empty(rig.Transport.Requests);
        files.Passed = true;
    }

    [Fact]
    public async Task Matching_provider_constraint_allows_the_actual_dispatch_provider()
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Provider allowed", cancellationToken: TestContext.Current.CancellationToken);
        space = await files.Registry.UpdateAsync(space with
        {
            ModelPolicy = new SpaceModelPolicy(null, [], "fixture-provider")
        }, space.Revision, TestContext.Current.CancellationToken);
        var rig = new SpacePolicyRig(new ControlledSpacePolicySource(files.Registry), space.Id, actualProviderId: "fixture-provider");

        await rig.SendAsync(null);

        Assert.Single(rig.Transport.Requests);
        files.Passed = true;
    }

    [Fact]
    public async Task Unsupported_untyped_closed_corpus_policy_blocks_before_memory_or_model()
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Closed corpus", cancellationToken: TestContext.Current.CancellationToken);
        space = await files.Registry.UpdateAsync(space with { ContextPolicy = new(SourceOnly: true) }, space.Revision, TestContext.Current.CancellationToken);
        var rig = new SpacePolicyRig(new ControlledSpacePolicySource(files.Registry), space.Id);
        var error = await Record.ExceptionAsync(() => rig.SendAsync(null));
        Assert.NotNull(error);
        Assert.True(ContainsSpaceCode(error!, "SPACE_TYPED_CONTEXT_CAPTURE_UNAVAILABLE"));
        Assert.Empty(rig.Transport.Requests);
        Assert.Equal(0, rig.LegacyMemory.Reads);
        files.Passed = true;
    }

    [Fact]
    public async Task Durable_lineage_survives_without_a_live_guard_and_fresh_capture_can_rebind_it()
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Restart lineage", cancellationToken: TestContext.Current.CancellationToken);
        var rig = new SpacePolicyRig(new ControlledSpacePolicySource(files.Registry), space.Id);
        await rig.SendAsync(null);
        var initial = Assert.Single(rig.Transport.Requests).Options!;
        var payload = JsonSerializer.Serialize(initial);
        using var json = JsonDocument.Parse(payload);
        Assert.False(json.RootElement.TryGetProperty(nameof(GenerationOptions.OriginalSpacePolicy), out _));
        var restored = JsonSerializer.Deserialize<GenerationOptions>(payload)!;
        Assert.True(restored.RequireOriginalSpacePolicy);
        Assert.Equal(initial.RequestedSpacePolicyLineage, restored.RequestedSpacePolicyLineage);
        Assert.Null(restored.OriginalSpacePolicy);
        Assert.Throws<SpaceRequestPolicyBlockedException>(() => OriginalSpacePolicyDispatch.ValidateRoute(restored));
        await rig.SendAsync(restored); // A new real source capture, never a deserialized grant.
        Assert.Equal(2, rig.Transport.Requests.Count);
        Assert.NotSame(initial.OriginalSpacePolicy, rig.Transport.Requests[1].Options!.OriginalSpacePolicy);
        Assert.Equal("{\"Temperature\":0.7,\"ContextLimit\":32768,\"ActionLimit\":24}", JsonSerializer.Serialize(new GenerationOptions()));
        files.Passed = true;
    }

    [Fact]
    public async Task Existing_ancestry_capture_does_not_seed_a_missing_registry()
    {
        using var files = new SpacePolicyFiles();
        var path = Path.Combine(files.DataDirectory, "settings.json");
        Assert.False(File.Exists(path));
        var error = await Record.ExceptionAsync(() => new SpacePolicyResolver(files.Registry).ResolveExistingAsync(
            Guid.NewGuid(), body => body(), _ => { }, TestContext.Current.CancellationToken));
        Assert.NotNull(error);
        Assert.True(ContainsSpaceCode(error!, "SPACE_POLICY_UNAVAILABLE"));
        Assert.False(File.Exists(path));
        files.Passed = true;
    }

    [Fact]
    public void Actual_runtime_routes_permissions_and_previous_selection_only_narrow_tools()
    {
        using var files = new SpacePolicyFiles();
        ActiveCapability[] active = [new("read-file", "Read", "", "", "workspace.read-file", "Files"),
            new("run-command", "Run", "", "", "workspace.run-command", "Build"),
            new("web-search", "Web", "", "", "browser.search", "Browse")];
        OllamaToolDefinition Def(string name) => new(name, "Fixture definition", new Dictionary<string, object>(), []);
        var read = Def("read_file"); var command = Def("run_command"); var browse = Def("browser_search");
        using var schema = JsonDocument.Parse("{}");
        PluginToolBinding Plugin(string name, ExtensionPermission permissions) => new(Def(name), "fixture-package", name,
            permissions, "extension.fixture." + name, schema.RootElement.Clone(), null, "fixture");
        PluginToolBinding[] plugins = [Plugin("pure_plugin", ExtensionPermission.None), Plugin("network_plugin", ExtensionPermission.NetworkAccess)];
        var plan = ToolAvailabilityPlanner.Default.Create(new(HavenMode.Tasks, files.DataDirectory, active,
            PermissionMode.FullAccess, PermissionMode.FullAccess, PermissionMode.FullAccess, false, true, false, false),
            new([read, command], [], [browse], [], [], [], Plugins: plugins));
        var id = Guid.NewGuid();
        var snapshot = new OriginalSpacePolicySnapshot(new(id, new(null), new(SpaceWebAccessPolicy.Disabled),
            new([SpaceMemoryScope.Conversation]), [], [], [id]), new(id, [new(id, 1)]));
        var selected = SpaceRequestPolicyConstraints.IntersectTools(snapshot, plan, plan.Definitions, active, plugins,
            new([read.Name, command.Name, browse.Name, "pure_plugin", "network_plugin"]));
        Assert.Equal(["pure_plugin", "read_file"], selected.AllowedToolNames);
        var previouslyEmpty = SpaceRequestPolicyConstraints.IntersectTools(snapshot, plan, plan.Definitions, active, plugins, new([]));
        Assert.Empty(previouslyEmpty.AllowedToolNames);
        Assert.False(selected.Allows("network_plugin"));
        Assert.False(selected.Allows("run_command"));
        Assert.False(selected.Allows("browser_search"));
        files.Passed = true;
    }

    private static bool ContainsSpaceCode(Exception error, string code) =>
        error is SpaceRequestPolicyBlockedException blocked && blocked.Code == code ||
        error is AggregateException group && group.InnerExceptions.Any(value => ContainsSpaceCode(value, code)) ||
        error.InnerException is { } inner && ContainsSpaceCode(inner, code);

    private sealed class SpacePolicyRig
    {
        private readonly ChatSessionService _chat;
        private readonly Conversation _conversation;
        private readonly ModelDescriptor _model;
        internal readonly SpacePolicyTransport Transport;
        internal readonly MemorySource LegacyMemory = new();
        internal SpacePolicyRig(IOriginalSpaceRequestPolicySource source, Guid spaceId, string actualProviderId = "fixture-provider")
        {
            var now = DateTimeOffset.UtcNow;
            _conversation = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Controlled Space request", null, null,
                false, false, now, now, SpaceId: spaceId);
            _model = new("space-policy-model", 1, "fixture", "fixture", "fixture", new HashSet<ToolCapability> { ToolCapability.Text }, now);
            Transport = new(_model, new SpacePolicyProvider(actualProviderId));
            _chat = new(new Conversations(), Transport, new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()), memorySource: LegacyMemory);
            _chat.BindOriginalSpacePolicySource(source);
        }
        internal async Task SendAsync(GenerationOptions? options)
        {
            var original = _chat.CreateOriginalOrdinaryConversation(_conversation, "Hello", _model, EffortLevel.Medium,
                "Fixture", "", (options ?? new()) with { RequestedRoutingConstraints = options?.RequestedRoutingConstraints ?? new(AllowCloud: true) },
                TestContext.Current.CancellationToken, body => body());
            var failures = new List<Exception>();
            try { await foreach (var _ in original.ConsumeOriginal()) { } }
            catch (Exception cause) { failures.Add(cause); }
            try { await original.JoinOriginalSourcesAsync(); }
            catch (Exception cause) { failures.Add(cause); }
            if (failures.Count != 0) throw new AggregateException("Actual original Space consumer and settlement failed.", failures);
        }
    }

    private sealed class SpacePolicyProvider(string id) : IModelProvider
    {
        public string Id { get; } = id;
        public string DisplayName => Id;
        public ModelProviderKind Kind => ModelProviderKind.OpenAICompatible;
        public bool IsLocal => false;
        public bool CanManageModels => false;
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => throw new NotSupportedException();
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        { await Task.Yield(); yield break; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class SpacePolicyTransport(ModelDescriptor model, IModelProvider actualProvider) : IOriginalPrivateContextModelClient
    {
        internal readonly List<OllamaChatRequest> Requests = [];
        internal Func<Task>? BeforePhysicalDispatch;
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            if (BeforePhysicalDispatch is { } before) { BeforePhysicalDispatch = null; await before(); }
            // Same maintained dispatch helper as both production routers, before the
            // controlled transport body. This is not a network or native execution claim.
            await OriginalPrivateContextDispatch.ValidateProviderAsync(request.Options, actualProvider, token);
            Requests.Add(request); yield return "Controlled Space reply.";
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new InvalidOperationException("No auxiliary model call is authorized by this fixture.");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new InvalidOperationException("This fixture has no canonical tool execution grant.");
    }

    // A consumer-boundary source over the real file registry, deliberately NOT a substitute
    // for the native Home/ownership adapter's separate integration verification.
    private sealed class ControlledSpacePolicySource(SpaceRegistry registry) : IOriginalSpaceRequestPolicySource
    {
        private sealed record Input(ControlledSpacePolicySource Owner, Guid ConversationId, OriginalSpacePolicySnapshot Snapshot) : IOriginalSpaceRequestPolicyInput;
        internal int Validations;
        public async Task<IOriginalSpaceRequestPolicyInput> CaptureAsync(Conversation conversation, Action<Action> acquire, CancellationToken token) =>
            new Input(this, conversation.Id, await new SpacePolicyResolver(registry).ResolveExistingAsync(conversation.SpaceId!.Value, acquire, _ => { }, token));
        public async Task<OriginalSpacePolicySnapshot> ReadAsync(IOriginalSpaceRequestPolicyInput input, Conversation conversation, Action<Action> acquire, CancellationToken token)
        { await ValidateAsync(input, conversation, acquire, token); return ((Input)input).Snapshot; }
        public async Task ValidateAsync(IOriginalSpaceRequestPolicyInput input, Conversation conversation, Action<Action> acquire, CancellationToken token)
        {
            Validations++;
            if (input is not Input original || !ReferenceEquals(original.Owner, this) || original.ConversationId != conversation.Id || original.Snapshot.Lineage.SpaceId != conversation.SpaceId)
                throw new SpaceRequestPolicyBlockedException("SPACE_POLICY_INPUT_NOT_ORIGINAL", "Foreign original Space input.");
            var current = await new SpacePolicyResolver(registry).ResolveExistingAsync(conversation.SpaceId!.Value, acquire, _ => { }, token);
            if (!current.Lineage.Equals(original.Snapshot.Lineage)) throw new SpaceRequestPolicyBlockedException("SPACE_POLICY_CHANGED", "The parent policy changed.");
        }
    }

    private sealed class SpacePolicyFiles : IAppPaths, IDisposable
    {
        internal bool Passed;
        internal SpaceRegistry Registry { get; }
        public SpacePolicyFiles()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "9to1-space-request-policy-" + Guid.NewGuid().ToString("N"));
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(DataDirectory);
            else Directory.CreateDirectory(DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Registry = new(new VersionedAtomicSettingsStore(this));
        }
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "unused-browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "unused-attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "unused-logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "unused-legacy.json");
        public void Dispose() { if (Passed) Directory.Delete(DataDirectory, true); }
    }
}
