using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class AgentInvocationSourceHostRegistrationTests
{
    [Fact]
    public async Task Actual_Desktop_registration_aliases_the_same_single_producer_without_creating_ownership()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-agent-source-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var registrations = new ServiceCollection().AddHavenInfrastructure().AddHavenPlannerInfrastructure();
            registrations.AddSingleton<IAppPaths>(new Paths(root));
            registrations.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            registrations.AddSingleton<ChatSessionService>(provider => new ChatSessionService(
                provider.GetRequiredService<IConversationRepository>(),
                provider.GetRequiredService<IOllamaClient>(), new CapabilityPreflightService(),
                provider.GetRequiredService<IConversationSafetyService>(),
                new WorkspaceToolRuntime(provider.GetRequiredService<IWorkspaceToolService>()),
                new ComputerToolRuntime(provider.GetRequiredService<IComputerToolService>())));
            App.AddAgentTaskRuntime(registrations);
            await using var graph = registrations.BuildServiceProvider();
            var actual = graph.GetRequiredService<AgentTaskRuntimeService>();
            var source = Assert.Single(graph.GetServices<IRecordedAgentInvocationSource>());
            Assert.Same(actual, source);
            Assert.Same(source, graph.GetRequiredService<IRecordedAgentInvocationSource>());
            Assert.Single(graph.GetServices<AgentTaskRuntimeService>());
            Assert.Null(await source.GetRecordedInvocationEvidenceAsync(Guid.NewGuid(), token));
            Assert.DoesNotContain((await graph.GetRequiredService<IHomeCoreStateStore>().ReadAsync(token)).State!.Records,
                record => record.RecordType == "home.local-store-ownership");
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "app.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
