using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual Agent/Chat and canonical SQLite workflows; model/protocol responses are explicitly controlled fixtures.</summary>
public sealed class AgentInvocationEvidenceTests
{
    [Fact]
    public async Task ActualProducerAndFreshCanonicalRowRequiredForKnownEmptyObservation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var run = await fixture.Runtime.RunAsync(fixture.Agent.Id, "Say a short sentence", CancellationToken.None);
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        var envelope = JsonSerializer.Deserialize<AgentActivityObservation>(run.ActivityJson)!;
        Assert.True(envelope.ObservationComplete);
        Assert.Equal(run.Id, envelope.AgentRunId);
        var receipt = Assert.IsType<RecordedAgentInvocationEvidence>(await fixture.Runtime.GetRecordedInvocationEvidenceAsync(run.Id));
        Assert.Empty(receipt.Invocations);
        Assert.Equal(AgentActivityObservation.ToolIdentityNamespace, receipt.ToolIdentityNamespace);
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<RecordedAgentInvocationEvidence>(JsonSerializer.Serialize(receipt)));
        Assert.Null(await fixture.NewRuntime().GetRecordedInvocationEvidenceAsync(run.Id));
        var imported = run with { Id = Guid.NewGuid() };
        await fixture.Runs.UpsertAsync(imported, CancellationToken.None);
        Assert.Null(await fixture.Runtime.GetRecordedInvocationEvidenceAsync(imported.Id));
        await fixture.Runs.UpsertAsync(run with { Error = "changed canonical metadata" }, CancellationToken.None);
        Assert.Null(await fixture.Runtime.GetRecordedInvocationEvidenceAsync(run.Id));
    }

    [Fact]
    public async Task ActualTypedDispatcherFactsReachIssuedCanonicalAgentObservation()
    {
        await using var fixture = await Fixture.CreateAsync(remoteTool: "read_item");
        var run = await fixture.Runtime.RunAsync(fixture.Agent.Id, "Read the attached MCP item", CancellationToken.None);
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        var evidence = Assert.IsType<RecordedAgentInvocationEvidence>(await fixture.Runtime.GetRecordedInvocationEvidenceAsync(run.Id));
        var invocation = Assert.Single(evidence.Invocations);
        Assert.Equal(fixture.LocalTool, invocation.ToolName);
        Assert.Equal("Mcp", invocation.RuntimeKey);
        Assert.Equal(ToolInvocationObservationStatus.RuntimeReturned, invocation.Status);
        Assert.True(invocation.ReportedResultSucceeded);
        Assert.Equal(1, fixture.Protocol.Invocations);
        await using var connection = await fixture.Database.OpenAsync(CancellationToken.None);
        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM agent_runs WHERE id=$id;";
        delete.Parameters.AddWithValue("$id", run.Id.ToString());
        Assert.Equal(1, await delete.ExecuteNonQueryAsync());
        Assert.Null(await fixture.Runtime.GetRecordedInvocationEvidenceAsync(run.Id));
    }

    [Fact]
    public async Task DeferredContinuationMakesFinishedStreamUnknownEvenAfterLaterControlledInvocation()
    {
        await using var fixture = await Fixture.CreateAsync(remoteTool: "write_item");
        var run = await fixture.Runtime.RunAsync(fixture.Agent.Id, "Update the attached MCP item", CancellationToken.None);
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        var observation = JsonSerializer.Deserialize<AgentActivityObservation>(run.ActivityJson)!;
        Assert.True(observation.HasDeferredInvocations);
        Assert.False(observation.ObservationComplete);
        Assert.Null(await fixture.Runtime.GetRecordedInvocationEvidenceAsync(run.Id));
        Assert.Equal(0, fixture.Protocol.Invocations);
        var pending = Assert.Single(await fixture.Graph.GetRequiredService<IRemediationRepository>().GetWaitingAsync(CancellationToken.None));
        await fixture.Remediations.ApproveAndResolveAsync(pending.Id, CancellationToken.None);
        Assert.Equal(1, fixture.Protocol.Invocations);
        Assert.Null(await fixture.Runtime.GetRecordedInvocationEvidenceAsync(run.Id));
        Assert.Equal(run.ActivityJson, (await fixture.Runs.GetAsync(run.Id, CancellationToken.None))!.ActivityJson);
    }

    [Fact]
    public async Task FaultedStreamNeverIssuesCompleteAbsenceEvidence()
    {
        await using var fixture = await Fixture.CreateAsync(failStream: true);
        var run = await fixture.Runtime.RunAsync(fixture.Agent.Id, "Say a short sentence", CancellationToken.None);
        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.False(JsonSerializer.Deserialize<AgentActivityObservation>(run.ActivityJson)!.ObservationComplete);
        Assert.Null(await fixture.Runtime.GetRecordedInvocationEvidenceAsync(run.Id));
    }

    [Fact]
    public async Task CancelledActualStreamPersistsPartialObservationAndNoReceipt()
    {
        await using var fixture = await Fixture.CreateAsync(pauseStream: true);
        using var stop = new CancellationTokenSource();
        var execution = fixture.Runtime.RunAsync(fixture.Agent.Id, "Say a short sentence", stop.Token);
        await fixture.Model.StreamEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        var run = await execution;
        Assert.Equal(AgentRunStatus.Cancelled, run.Status);
        Assert.False(JsonSerializer.Deserialize<AgentActivityObservation>(run.ActivityJson)!.ObservationComplete);
        Assert.Null(await fixture.Runtime.GetRecordedInvocationEvidenceAsync(run.Id));
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
