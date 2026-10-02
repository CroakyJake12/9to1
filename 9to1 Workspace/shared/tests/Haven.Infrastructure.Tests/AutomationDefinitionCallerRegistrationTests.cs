using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class AutomationDefinitionCallerRegistrationTests
{
    [Fact]
    public void Explicit_owner_graph_has_same_caller_repositories_and_eager_broker_without_cycle()
    {
        var root = Directory.CreateTempSubdirectory("astra-automation-owner-di-").FullName;
        try
        {
            var services = new ServiceCollection().AddHavenInfrastructure();
            services.AddSingleton<IAppPaths>(new Paths(root));
            services.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            services.AddHavenAutomationDefinitionOwnership();
            using var graph = services.BuildServiceProvider();
            Assert.Same(graph.GetRequiredService<AutomationDefinitionReviewCaller>(), graph.GetRequiredService<IAutomationDefinitionReviewCaller>());
            Assert.Same(graph.GetRequiredService<AutomationRepository>(), graph.GetRequiredService<IAutomationRepository>());
            Assert.Same(graph.GetRequiredService<AutomationRepository>(), graph.GetRequiredService<IAutomationOwnerRepository>());
            Assert.Same(graph.GetRequiredService<WorkspaceStateRepository>(), graph.GetRequiredService<IWorkspaceStateRepository>());
            Assert.Same(graph.GetRequiredService<WorkspaceStateRepository>(), graph.GetRequiredService<IReusableTaskOwnerRepository>());
            Assert.NotNull(graph.GetRequiredService<HomeResourceOperationBroker>());
            Assert.NotNull(graph.GetRequiredService<AutomationLocalStoreAuthority>());
            // Constructor graph only: no schema activation, actor issue, Home approval or row mutation proof.
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Competing_caller_registration_is_rejected_before_canonical_descriptors_are_removed()
    {
        var services = new ServiceCollection().AddHavenInfrastructure();
        services.AddSingleton<IAutomationDefinitionReviewCaller>(_ => throw new InvalidOperationException("Do not resolve competing owner."));
        var before = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => services.AddHavenAutomationDefinitionOwnership());
        Assert.Equal(before.Length, services.Count);
        for (var index = 0; index < before.Length; index++) Assert.Same(before[index], services[index]);
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
