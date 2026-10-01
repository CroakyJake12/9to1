using Haven.Application;
using HavenOS.Home.Core;
using Xunit;
namespace HavenOS.Home.Tests;

// Actual FileHome/profile identity and guarded registry; platform observation is controlled, never a native launch claim.
public sealed class HomeInstalledApplicationOriginalActorTests
{
    [Fact]
    public async Task Replaced_original_host_principal_denies_before_platform_observation()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-installed-original-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var principal = new Principal(); var actors = new HomeLocalProfileIdentity(store, principal);
            var original = (await actors.GetCurrentAsync(default))!; var provider = new Observations();
            var registry = new HomeInstalledApplicationRegistry(store, actors, [provider]);
            principal.Value = "replacement-host-principal";
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await registry.RefreshForActorAsync(original, default));
            Assert.Equal(0, provider.Calls);
            Assert.DoesNotContain((await store.ReadAsync()).State!.Records, r => r.RecordType == "home.installed-apps");
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Suspended_platform_observation_cannot_publish_under_replacement_principal()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-installed-original-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var principal = new Principal(); var actors = new HomeLocalProfileIdentity(store, principal);
            var original = (await actors.GetCurrentAsync(default))!; var provider = new Observations { Suspend = true };
            var registry = new HomeInstalledApplicationRegistry(store, actors, [provider]);
            var refresh = registry.RefreshForActorAsync(original, default).AsTask(); await provider.Entered.Task;
            principal.Value = "replacement-host-principal"; provider.Release.TrySetResult();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await refresh);
            Assert.Equal(1, provider.Calls);
            Assert.DoesNotContain((await store.ReadAsync()).State!.Records, r => r.RecordType == "home.installed-apps");
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public string Value = "original-controlled-host-principal";
        public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) => ValueTask.FromResult<string?>(Value);
    }
    private sealed class Observations : IInstalledApplicationObservationProvider
    {
        public string ProviderId => "controlled-platform"; public bool Suspend; public int Calls;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct)
        {
            Calls++; Entered.TrySetResult(); if (Suspend) await Release.Task.WaitAsync(ct);
            return [new("personal", "Personal", false, true, [new("controlled-app", "controlled-entry", "Controlled", null, true)])];
        }
    }
}
