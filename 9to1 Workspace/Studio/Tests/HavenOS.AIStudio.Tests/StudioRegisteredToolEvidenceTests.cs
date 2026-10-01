using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.AIStudio;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HavenOS.AIStudio.Tests;

// Real Agent/Chat/canonical SQLite producer. Model, protocol and authorization are
// explicit controlled test boundaries; this does not prove production Home authorization.
public sealed class StudioRegisteredToolEvidenceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Registered_tool_assertions_require_originating_binding_and_fresh_owning_row(bool invoke, bool changeRow)
    {
        await using var fixture = await Fixture.CreateAsync(invoke ? "read_item" : null);
        var root = Path.Combine(fixture.Graph.GetRequiredService<IAppPaths>().DataDirectory, "studio");
        var adapter = new BoundFixtureRuntime(fixture, changeRow);
        var api = new AIStudioApi(new JsonStudioProjectStore(root), adapter,
            new UnavailableCanonicalAgentBuilderAdapter(), new JsonStudioResourceStore(root),
            new UnavailableStudioPermissionSimulationAdapter(), new UnavailableStudioContextInspectorAdapter(),
            new UnavailableStudioReplayAdapter());
        var project = (await api.CreateHarnessFromTemplateAsync("harness.assistant.v1", "Owning observation fixture")).Value!;
        var toolName = fixture.LocalTool ?? "not-dispatched";
        var expected = JsonSerializer.Serialize(new { @namespace = AgentActivityObservation.ToolIdentityNamespace, toolName });
        var suite = new TestSuite(Guid.NewGuid(), "Registered dispatch", project.ProjectId.ToString(), "Harness", project.Version,
            [new StudioTestCase(Guid.NewGuid(), "Fresh owning source", invoke ? "Read the attached MCP item" : "Say a short sentence", "{}",
                [new("presence", "registeredToolInvoked", "$", expected), new("absence", "registeredToolNotInvoked", "$", expected),
                 new("canonical", "actionCalled", "$", JsonSerializer.Serialize(toolName))],
                project.Version, "Controlled owning source fixture")], null, 1);
        var resource = (await api.CreateTestSuiteAsync(suite.Name, suite)).Value!;
        var result = (await api.RunTestSuiteAsync(resource.ResourceId)).Value!.Results.Single();
        Assert.Equal(!changeRow && invoke, result.Assertions[0].Passed);
        Assert.Equal(!changeRow && !invoke, result.Assertions[1].Passed);
        Assert.False(result.Assertions[2].Passed);
        Assert.Contains("ActionEvidenceUnavailable", result.Assertions[2].Message);
        if (changeRow) Assert.All(result.Assertions.Take(2), item => Assert.Contains("InvocationEvidenceUnavailable", item.Message));
        Assert.Equal(invoke ? 1 : 0, fixture.Protocol.Invocations);
        var issued = adapter.Issued!;
        Assert.Null(await adapter.GetRecordedInvocationEvidenceAsync(issued with { }, CancellationToken.None));
        Assert.Null(await ((IStudioRuntimeAdapter)new UnavailableStudioRuntimeAdapter()).GetRecordedInvocationEvidenceAsync(issued, CancellationToken.None));
    }

    private sealed class BoundFixtureRuntime(Fixture fixture, bool changeRow) : IStudioRuntimeAdapter
    {
        private Guid _owningRunId;
        public StudioRun? Issued { get; private set; }
        public async Task<StudioResult<StudioRun>> RunHarnessAsync(StudioProject project, string input, string? profile, CancellationToken ct)
        {
            var owning = await fixture.Runtime.RunAsync(fixture.Agent.Id, input, ct);
            _owningRunId = owning.Id;
            Issued = new(Guid.NewGuid(), project.ProjectId.ToString(), owning.Status.ToString(), owning.Result,
                "controlled-fixture", "controlled-fixture", "{}", owning.ActivityJson, "{}", "{}", null, null,
                null, null, null, project.Version, profile);
            if (changeRow) await fixture.Runs.UpsertAsync(owning with { Error = "Canonical row changed before evidence read" }, ct);
            return StudioResult<StudioRun>.Success(Issued);
        }
        public Task<RecordedAgentInvocationEvidence?> GetRecordedInvocationEvidenceAsync(StudioRun originatingRun, CancellationToken ct) =>
            ReferenceEquals(originatingRun, Issued)
                ? fixture.Runtime.GetRecordedInvocationEvidenceAsync(_owningRunId, ct)
                : Task.FromResult<RecordedAgentInvocationEvidence?>(null);
        public Task<StudioResult<StudioRun>> RunPlaygroundAsync(PlaygroundConfiguration config, string input, CancellationToken ct) =>
            Task.FromResult(StudioResult<StudioRun>.Failure("RuntimeUnavailable", "No playground fixture binding."));
        public Task<StudioResult<InstallReceipt>> InstallToolAsync(StudioProject project, CancellationToken ct) =>
            Task.FromResult(StudioResult<InstallReceipt>.Failure("RuntimeUnavailable", "No install fixture binding."));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(Paths paths, ServiceProvider graph, ControlledModel model, ControlledProtocol protocol,
            AgentDefinition agent, string? localTool, RemediationCoordinator remediations, ChatSessionService chat)
        {
            _paths = paths; Graph = graph; Model = model; Protocol = protocol; Agent = agent; LocalTool = localTool;
            Remediations = remediations; _chat = chat; Runtime = NewRuntime();
        }
        private readonly Paths _paths;
        private readonly ChatSessionService _chat;
        public ServiceProvider Graph { get; }
        public ControlledModel Model { get; }
        public ControlledProtocol Protocol { get; }
        public AgentDefinition Agent { get; }
        public string? LocalTool { get; }
        public RemediationCoordinator Remediations { get; }
        public AgentTaskRuntimeService Runtime { get; }
        public SqliteDatabase Database => Graph.GetRequiredService<SqliteDatabase>();
        public IAgentRunRepository Runs => Graph.GetRequiredService<IAgentRunRepository>();
        public AgentTaskRuntimeService NewRuntime() => new(Graph.GetRequiredService<ICatalogRepository>(), Runs,
            Model, Graph.GetRequiredService<CapabilityRegistryService>(), _chat, Graph.GetRequiredService<IPermissionDecisionEngine>());

        public static async Task<Fixture> CreateAsync(string? remoteTool = null, bool failStream = false, bool pauseStream = false)
        {
            var paths = new Paths();
            var services = new ServiceCollection().AddHavenInfrastructure().AddHavenPlannerInfrastructure();
            services.AddSingleton<IAppPaths>(paths);
            services.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "home.json")));
            var graph = services.BuildServiceProvider();
            await graph.GetRequiredService<IAppDatabase>().InitializeAsync(CancellationToken.None);
            var connectionId = Guid.NewGuid();
            var localTool = remoteTool is null ? null : McpToolRuntime.LocalToolName(connectionId, remoteTool);
            var model = new ControlledModel(localTool, failStream, pauseStream);
            var protocol = new ControlledProtocol(remoteTool ?? "read_item");
            if (remoteTool is not null)
            {
                var now = DateTimeOffset.UtcNow;
                await graph.GetRequiredService<IExternalConnectionRepository>().UpsertAsync(new(connectionId, "Controlled fixture MCP", "fixture.mcp",
                    ExternalConnectionKind.Mcp, "custom-mcp", true, ExternalConnectionState.Ready, "Controlled fixture metadata",
                    JsonSerializer.Serialize(new McpConnectionConfiguration(McpTransportKind.StreamableHttp, "http://127.0.0.1:8765/mcp", LocalOnly: true)),
                    "fixture", "1", "fixture", now, now), CancellationToken.None);
            }
            var agent = new AgentDefinition(Guid.NewGuid(), "Controlled fixture Agent", "Fixture", "Use only the supplied fixture context",
                "fixture", model.Descriptor.Name, null, "", JsonSerializer.Serialize(new { capabilities = remoteTool is null ? Array.Empty<string>() : new[] { ExternalConnectionNaming.CapabilityKey(connectionId) } }),
                false, true, DateTimeOffset.UtcNow);
            await graph.GetRequiredService<ICatalogRepository>().UpsertAgentAsync(agent, CancellationToken.None);
            var workspace = graph.GetRequiredService<IWorkspaceToolService>();
            var remediations = new RemediationCoordinator(graph.GetRequiredService<IRemediationRepository>(), new UnusedSecrets(),
                graph.GetRequiredService<IExecutionEventSink>(), graph.GetRequiredService<RemediationContinuationRegistry>());
            var chat = new ChatSessionService(graph.GetRequiredService<IConversationRepository>(), model, new CapabilityPreflightService(),
                graph.GetRequiredService<IConversationSafetyService>(), new WorkspaceToolRuntime(workspace),
                new ComputerToolRuntime(graph.GetRequiredService<IComputerToolService>()),
                mcpTools: new McpToolRuntime(graph.GetRequiredService<IExternalConnectionRepository>(), protocol, new ControlledInvocationBoundary()),
                recovery: graph.GetRequiredService<AutonomousRecoveryService>(), remediations: remediations);
            return new(paths, graph, model, protocol, agent, localTool, remediations, chat);
        }
        public async ValueTask DisposeAsync()
        { Remediations.Dispose(); await Graph.DisposeAsync(); SqliteConnection.ClearAllPools(); Directory.Delete(_paths.DataDirectory, true); }
    }

    private sealed class ControlledModel(string? localTool, bool failStream, bool pauseStream) : IOllamaClient
    {
        public ModelDescriptor Descriptor { get; } = new("explicit-controlled-fixture", 1, "fixture", "fixture", "fixture",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UtcNow);
        public TaskCompletionSource StreamEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _toolRequests;
        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([Descriptor]);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken ct) => Task.FromResult("1 minute");
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            StreamEntered.TrySetResult();
            if (pauseStream) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (failStream) throw new IOException("Controlled fixture stream failure");
            cancellationToken.ThrowIfCancellationRequested();
            yield return "Controlled fixture text.";
        }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken ct) => Task.FromResult(
            ++_toolRequests == 1 && localTool is not null
                ? new OllamaToolResponse("", [new(localTool, new Dictionary<string, JsonElement>())])
                : new OllamaToolResponse("Controlled fixture response.", []));
    }
    private sealed class ControlledProtocol(string toolName) : IMcpConnectionClient
    {
        public int Invocations { get; private set; }
        public Task<(McpServerIdentity Identity, IReadOnlyList<McpExternalTool> Tools)> DiscoverAsync(ExternalConnection connection, CancellationToken ct)
        {
            using var schema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
            return Task.FromResult<(McpServerIdentity, IReadOnlyList<McpExternalTool>)>((new("fixture", "1", "fixture", "{}"),
                [new(toolName, "Explicitly controlled fixture operation", schema.RootElement.Clone())]));
        }
        public Task<McpToolInvocationResult> InvokeAsync(ExternalConnection connection, string remoteTool, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken ct)
        { Invocations++; return Task.FromResult(new McpToolInvocationResult(true, "Controlled fixture return", null, "[]")); }
    }
    private sealed class ControlledInvocationBoundary : IMcpInvocationAuthorizer
    {
        public ValueTask<bool> AuthorizeAsync(ExternalConnection connection, string remoteTool, JsonElement arguments, CancellationToken ct) => ValueTask.FromResult(true);
    }
    private sealed class UnusedSecrets : IProviderSecretStore
    {
        public Task SetAsync(string providerId, string secretName, string value, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetAsync(string providerId, string secretName, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, string secretName, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Paths : IAppPaths
    {
        public Paths() { Directory.CreateDirectory(DataDirectory); }
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-agent-invocation-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "canonical.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
