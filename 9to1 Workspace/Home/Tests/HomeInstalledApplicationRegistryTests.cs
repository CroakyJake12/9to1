using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeInstalledApplicationRegistryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "astra-app-registry-" + Guid.NewGuid().ToString("N"), "home.json");
    [Fact]
    public async Task Canonical_ids_survive_reopen_profile_duplicate_labels_and_quiet_profiles_cannot_launch()
    {
        var actors = new Actors(); var provider = new Provider();
        var registry = new HomeInstalledApplicationRegistry(new FileHomeCoreStateStore(_path), actors, [provider]);
        var first = await registry.RefreshAsync(default);
        Assert.Equal(2, first.Count);
        Assert.NotEqual(first[0].ApplicationId, first[1].ApplicationId);
        Assert.All(first, a => Assert.Equal(Haven.Core.AppOperabilityClassification.Unknown, a.Operability.Classification));
        var personal = first.Single(a => a.PlatformProfileId == "personal");
        var work = first.Single(a => a.PlatformProfileId == "work");
        var reopened = new HomeInstalledApplicationRegistry(new FileHomeCoreStateStore(_path), actors, [provider]);
        Assert.Equal(personal, await reopened.ResolveLaunchAsync(personal.ApplicationId, personal.Revision, default));
        provider.Quiet = true;
        var quiet = await reopened.RefreshAsync(default);
        var quietWork = quiet.Single(a => a.ApplicationId == work.ApplicationId);
        Assert.False(quietWork.ProfileAccessible);
        Assert.Null(await reopened.ResolveLaunchAsync(work.ApplicationId, quietWork.Revision, default));
        provider.Quiet = false;
        var returned = await reopened.RefreshAsync(default);
        Assert.Equal(work.ApplicationId, returned.Single(a => a.PlatformProfileId == "work").ApplicationId);
        Assert.Null(await reopened.ResolveLaunchAsync(work.ApplicationId, work.Revision, default));
        provider.Removed = true;
        Assert.Null(await reopened.ResolveLaunchAsync(personal.ApplicationId, personal.Revision, default));
        Assert.False((await reopened.RefreshAsync(default)).Single(a => a.ApplicationId == personal.ApplicationId).Enabled);
    }
    [Fact]
    public async Task Unknown_authority_duplicate_provider_and_actor_switch_fail_closed()
    {
        var actor = new Actors(); var provider = new Provider();
        var store = new FileHomeCoreStateStore(_path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new HomeInstalledApplicationRegistry(store, actor, [provider, provider]).RefreshAsync(default).AsTask());
        provider.Switch = () => actor.Current = actor.Current with { AuthenticationRevision = "new-session" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new HomeInstalledApplicationRegistry(store, actor, [provider]).RefreshAsync(default).AsTask());
        Assert.Empty((await store.ReadAsync()).State!.Records);
    }
    [Fact]
    public async Task Native_invocation_source_omits_unknown_games_and_revoked_platform_entries()
    {
        var provider = new Provider();
        var registry = new HomeInstalledApplicationRegistry(new FileHomeCoreStateStore(_path), new Actors(), [provider]);
        var source = new HomeInstalledAppInvocationSource(registry);
        Assert.Empty(await source.SearchAsync("", default));
        provider.Operability = new(Haven.Core.AppOperabilityClassification.OrdinaryApplication, Haven.Core.AppOperabilityPath.ComputerUseRequired);
        var ordinary = await source.SearchAsync("", default);
        Assert.Equal(2, ordinary.Count);
        Assert.All(ordinary, resource => Assert.Equal(NineToOne.Cui.AI.AppInteractionPath.ComputerUseRequired, resource.InteractionPath));
        provider.Operability = new(Haven.Core.AppOperabilityClassification.Game, Haven.Core.AppOperabilityPath.ComputerUseRequired);
        Assert.Empty(await source.SearchAsync("", default));
        provider.Operability = new(Haven.Core.AppOperabilityClassification.AntiCheatProtected, Haven.Core.AppOperabilityPath.ComputerUseRequired);
        Assert.Empty(await source.SearchAsync("", default));
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class Provider : IInstalledApplicationObservationProvider
    {
        public string ProviderId => "android.launcherapps";
        public bool Quiet; public bool Removed; public Action? Switch; public Haven.Core.AppOperability? Operability;
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct)
        {
            Switch?.Invoke();
            InstalledApplicationObservation app = new("android:example", "example/.Main", "Same label", null, true, Operability);
            return ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>([
                new("personal", "Personal", false, true, Removed ? [] : [app]),
                new("work", "Work", true, !Quiet, Quiet ? [] : [app])]);
        }
    }
    public void Dispose() { var root = Path.GetDirectoryName(_path)!; if (Directory.Exists(root)) Directory.Delete(root, true); }
}
