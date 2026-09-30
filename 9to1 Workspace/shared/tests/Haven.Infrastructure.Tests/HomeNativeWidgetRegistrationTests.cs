using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class HomeNativeWidgetRegistrationTests
{
    [Fact]
    public async Task Default_native_graph_shares_registry_but_has_no_installed_widget_authority_or_transport()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-widget-di-" + Guid.NewGuid().ToString("N"));
        try
        {
            var registrations = new ServiceCollection().AddHavenInfrastructure();
            registrations.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            await using var services = registrations.BuildServiceProvider();
            var registry = services.GetRequiredService<HomeNativeWidgetRegistry>();
            Assert.Same(registry, services.GetRequiredService<HomeNativeWidgetRegistry>());
            Assert.IsType<UnavailableHomeNativeInstalledPeerVerifier>(services.GetRequiredService<IHomeNativeInstalledPeerVerifier>());
            Assert.Empty(await registry.ListAsync());
            Assert.Null(await registry.ResolveAsync(new("declared", Guid.NewGuid(), "v1", "widget", "v1")));
            var descriptor = Assert.Single(services.GetRequiredService<HomeCoreRuntime>().Current.Services,
                value => value.ServiceId == HomeNativeWidgetRegistry.ServiceId);
            Assert.False(descriptor.IsAvailable);
            Assert.Equal(HomeServiceLifecycleState.Unavailable, descriptor.State);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Host_verifier_registration_is_retained_by_shared_composition()
    {
        var verifier = new HostVerifier();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IHomeNativeInstalledPeerVerifier>(verifier);
        registrations.AddHavenInfrastructure();
        using var services = registrations.BuildServiceProvider();
        Assert.Same(verifier, services.GetRequiredService<IHomeNativeInstalledPeerVerifier>());
        Assert.Single(services.GetServices<IHomeNativeInstalledPeerVerifier>());
    }
    private sealed class HostVerifier : IHomeNativeInstalledPeerVerifier
    {
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observed, CancellationToken ct) =>
            ValueTask.FromResult<HomeNativeInstalledPeer?>(null);
    }
}
