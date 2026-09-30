using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeNativeServiceHostTests
{
    [Fact]
    public async Task Process_host_reuses_actual_composition_and_never_grants_missing_required_service()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-bootstrap-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileHomeCoreStateStore(Path.Combine(directory, "home.json"));
            var actors = new Actors();
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(store)]);
            var services = new Services(); var starts = 0;
            Task<HomeNativeServiceSession> Compose(CancellationToken ct)
            {
                Interlocked.Increment(ref starts);
                return Task.FromResult(new HomeNativeServiceSession(services, runtime, actors));
            }
            var host = HomeNativeServiceHost.Process;
            var ready = await host.EnsureAsync("fixture-native-host", Compose, ["home.core", "home.state"]);
            Assert.Equal(HomeNativeHostState.Ready, ready.State);
            Assert.Same(services, ready.Services);
            var missing = await host.EnsureAsync("fixture-native-host", Compose, ["home.core", "apps.installed"]);
            Assert.Equal(HomeNativeHostState.RequiresHomeRepair, missing.State);
            Assert.Null(missing.Services);
            Assert.Contains("apps.installed", missing.Message);
            Assert.Equal(1, starts);
            var incompatible = await host.EnsureAsync("fixture-native-host", Compose,
                [new HomeServiceRequirement("home.core", 2)]);
            Assert.Equal(HomeNativeHostState.RequiresHomeRepair, incompatible.State);
            Assert.Null(incompatible.Services);
            var conflict = await host.EnsureAsync("different-host", Compose, ["home.core"]);
            Assert.Equal("HomeCompositionConflict", conflict.Code);
            actors.Current = null;
            Assert.Equal("HomeIdentityUnavailable", (await host.EnsureAsync("fixture-native-host", Compose, ["home.core"])).Code);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult(Current);
    }
    private sealed class Services : IServiceProvider { public object? GetService(Type type) => null; }
}
