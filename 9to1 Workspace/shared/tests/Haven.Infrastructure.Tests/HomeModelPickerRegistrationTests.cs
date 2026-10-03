using Dulche.Runtime;
using Haven.Application;
using Haven.Application.NodeGraph;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class HomeModelPickerRegistrationTests
{
    [Fact]
    public async Task Native_actual_OS_profile_composes_one_owned_route_repository_and_live_feature_service()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-model-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var registrations = new ServiceCollection().AddHavenInfrastructure();
            registrations.AddSingleton<IAppPaths>(new Paths(root));
            registrations.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            using var services = registrations.BuildServiceProvider();
            var provider = Assert.IsType<HomeModelPickerFeatureProvider>(services.GetRequiredService<IHomeModelPickerFeatureProvider>());
            var service = Assert.IsType<HomeModelPickerCoreService>(Assert.Single(services.GetServices<IHomeCoreService>(),
                candidate => candidate.Descriptor.ServiceId == "models.routes"));
            await service.StartAsync();
            var snapshot = (await provider.GetSnapshotAsync("User", "Active")).Value!;
            var actor = await services.GetRequiredService<HomeLocalProfileIdentity>().GetCurrentAsync(default);
            Assert.NotNull(actor); Assert.Null(actor.AccountId); Assert.Null(actor.OrganisationId);
            Assert.Equal(actor.ProfileId, Assert.Single(snapshot.Routes).ScopeId);
            Assert.IsType<HomeVersionedModelRouteRepository>(services.GetRequiredService<IVersionedModelRouteRepository>());
            Assert.Empty(await services.GetRequiredService<IVersionedModelRouteRepository>().ListAsync(default));
            Assert.Contains("permissions.trust", service.Dependencies);
            Assert.IsType<HomeTerminalAdviceService>(services.GetRequiredService<ITerminalAdviceService>());
            Assert.IsType<HomeVersionedNodeGraphRepository>(services.GetRequiredService<IVersionedNodeGraphRepository>());
            Assert.NotNull(services.GetRequiredService<NodeGraphRuntimeRegistry>());
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "data.db");
        public string BrowserProfileDirectory => root;
        public string AttachmentsDirectory => root;
        public string LogsDirectory => root;
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
