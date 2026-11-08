using Haven.Application;
using Haven.Application.Automations;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

public sealed class MeshRegistrationTests
{
    [Fact]
    public async Task MeshServicesResolveWithoutCircularDependencyAndJoinProviderCollections()
    {
        // AddHavenInfrastructure registers DataLocalRecordSessionFactory, which requires the composition root's
        // authenticated actor source (added by 2fba86a6, 28 October 2026). Production DI deliberately registers
        // no fallback for IAuthenticatedResourceActorSource because resource authorization must fail closed, so
        // this fixture supplies the same genuine local Home owner pair that HomeLocalDomainComposition maps onto
        // that interface: FileHomeCoreStateStore with HomeLocalProfileIdentity. Local profile state only - this
        // check asserts Mesh DI health, not account, network, Mesh transport or native Desktop readiness.
        var root = Directory.CreateTempSubdirectory("mesh-registration-").FullName;
        try
        {
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var services = new ServiceCollection();
            services.AddSingleton<IHomeCoreStateStore>(home);
            services.AddSingleton(profiles);
            services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
            services.AddHavenInfrastructure();
            services.AddHavenMesh();

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

            Assert.NotNull(provider.GetRequiredService<MeshCoordinator>());
            Assert.Contains(provider.GetServices<IDeviceActionProvider>(), item => item is WindowsComputerDeviceActionProvider);
            Assert.Contains(provider.GetServices<IDeviceActionProvider>(), item => item is MeshDeviceActionProvider);
            Assert.Contains(provider.GetServices<IModelProvider>(), item => item is MeshRemoteModelProvider);
        }
        finally
        {
            try { Directory.Delete(root, true); }
            catch (Exception cleanup) { throw new AggregateException("Mesh registration fixture cleanup failed.", cleanup); }
        }
    }
}
