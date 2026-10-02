using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class AutomationGraphOwnerRegistrationTests
{
    [Fact]
    public async Task Opt_in_graph_module_resolves_actual_preparer_owner_broker_lease_and_existing_graph_alias_without_cycle()
    {
        var root = Directory.CreateTempSubdirectory("astra-graph-owner-di-").FullName;
        try
        {
            var services = new ServiceCollection().AddHavenInfrastructure();
            services.AddSingleton<IAppPaths>(new Paths(root));
            services.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            services.AddHavenAutomationDefinitionOwnership();
            services.AddHavenLocalAutomationGraphPublication();
            using var graph = services.BuildServiceProvider();
            var repository = graph.GetRequiredService<IVersionedNodeGraphRepository>();
            Assert.Same(repository, graph.GetRequiredService<HomeVersionedNodeGraphRepository>());
            Assert.Same(graph.GetRequiredService<AutomationGraphOriginAuthority>(), graph.GetRequiredService<IGraphPublicationOriginAuthority>());
            Assert.True(graph.GetRequiredService<AutomationGraphOriginAuthority>().IsBoundToGraphRepository(repository));
            Assert.Same(graph.GetRequiredService<AutomationDefinitionReviewCaller>(), graph.GetRequiredService<IAutomationDefinitionReviewCaller>());
            Assert.Same(graph.GetRequiredService<HomeLocalProfileIdentity>(), graph.GetRequiredService<IAuthenticatedResourceActorSource>());
            Assert.Same(graph.GetRequiredService<HomeResourceStoreOwnershipAuthority>(), graph.GetRequiredService<IResourceStoreOwnershipAuthority>());
            var registry = graph.GetRequiredService<HomeGraphPublicationResourceRegistry>();
            Assert.Same(registry, Assert.Single(graph.GetServices<ICanonicalResourceAccessResolver>(), r => r.ResourceKind == "nodegraph.definition"));
            Assert.NotNull(graph.GetRequiredService<NodeGraphSchemaRegistry>());
            Assert.NotNull(graph.GetRequiredService<NodeGraphAutomationAdapter>());
            Assert.NotNull(graph.GetRequiredService<AutomationGraphPublicationPreparer>());
            Assert.NotNull(graph.GetRequiredService<HomeResourceOperationBroker>());
            Assert.NotNull(graph.GetRequiredService<HomeGraphPublicationOwner>());
            Assert.NotNull(graph.GetRequiredService<HomeGraphSqlAssociationLeaseSource>());
            Assert.Empty(graph.GetServices<AutomationNodeGraphBinding>());
            // The existing HomeAppAiServices factory genuinely initializes the canonical OS-bound profile
            // when eager broker construction resolves permissions. Observe that actual state rather than
            // incorrectly claiming constructor resolution performs no Home initialization.
            var read = await graph.GetRequiredService<IHomeCoreStateStore>().ReadAsync(default);
            Assert.True(read.IsSuccess);
            var state = Assert.IsType<HomeCoreStoredState>(read.State);
            Assert.Single(state.Records, record => record.RecordType == "home.local-profile");
            Assert.DoesNotContain(state.Records, record => record.RecordType == "home.node-graph" || record.RecordType == "home.permissions-trust");
            // No binding catalogue, individual request, graph write, SQL schema or run permission is created.
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_definition_module_or_competing_graph_concrete_refuses_before_descriptor_mutation(bool competing)
    {
        var services = new ServiceCollection().AddHavenInfrastructure();
        if (competing)
        {
            services.AddHavenAutomationDefinitionOwnership();
            services.AddSingleton<HomeVersionedNodeGraphRepository>(_ => throw new InvalidOperationException("Do not resolve competing graph."));
        }
        var before = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => services.AddHavenLocalAutomationGraphPublication());
        Assert.Equal(before.Length, services.Count);
        for (var index = 0; index < before.Length; index++) Assert.Same(before[index], services[index]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Foreign_actor_or_unsupported_store_denies_local_owner_before_owner_IO(bool unsupportedStore)
    {
        var root = Directory.CreateTempSubdirectory("astra-graph-owner-foreign-actor-").FullName;
        try
        {
            var services = new ServiceCollection().AddHavenInfrastructure();
            services.AddSingleton<IAppPaths>(new Paths(root));
            services.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            if (unsupportedStore) services.AddSingleton<IHomeCoreStateStore>(new UnsupportedStore());
            else services.AddSingleton<IAuthenticatedResourceActorSource>(new ForeignActor());
            services.AddHavenAutomationDefinitionOwnership();
            services.AddHavenLocalAutomationGraphPublication();
            using var graph = services.BuildServiceProvider();
            Assert.Throws<NotSupportedException>(() => graph.GetRequiredService<HomeGraphPublicationOwner>());
            Assert.False(File.Exists(Path.Combine(root, "home.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class UnsupportedStore : IHomeCoreStateStore
    {
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("Unsupported store must not be read during composition refusal.");
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken ct = default)
            => throw new InvalidOperationException("Unsupported store must not be written during composition refusal.");
    }
    private sealed class ForeignActor : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
            => throw new InvalidOperationException("Foreign actor must not be read during composition refusal.");
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
