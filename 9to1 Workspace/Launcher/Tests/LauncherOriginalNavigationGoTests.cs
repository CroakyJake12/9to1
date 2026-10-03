using Haven.Application;
using Haven.Application.Go;
using HavenOS.Home.Core;
using NineToOne.Launcher;
namespace NineToOne.Launcher.Tests;

public sealed class LauncherOriginalNavigationGoTests
{
    [Fact]
    public async Task OriginalGoQueryFreshResolveAndInvokePersistActualOriginalPage()
    {
        using var f = new Fixture(); var originalActor = f.Actors.Current;
        var first = await f.Store.GetForActorAsync(originalActor);
        var second = await f.Store.EditAsync(first, layout => LauncherLayoutEdits.AddPage(layout, "Work"));
        var owner = new LauncherNavigationGoProvider(f.Store); var engine = new GoService([owner]);
        var home = Assert.Single(await Results(owner, originalActor, "Home"));
        Assert.Equal(first.Current.ActivePageId.ToString("D"), home.Reference.Id);
        var fresh = await engine.ResolveForActorAsync(owner.ProviderId, new("Launcher", "launcher.page", home.Reference.Id), originalActor);
        Assert.NotNull(fresh); Assert.Equal(home.Reference, fresh.Reference);
        await engine.InvokeForActorAsync(fresh, "OpenPage", originalActor);
        var saved = await f.Store.GetForActorAsync(originalActor);
        Assert.Equal(first.Current.ActivePageId, saved.Current.ActivePageId); Assert.Equal(second.Revision + 1, saved.Revision);
        await Assert.ThrowsAsync<IOException>(() => owner.InvokeForActorAsync(home.Reference, "OpenPage", originalActor, default));
    }
    [Fact]
    public async Task OriginalDiscoveryNeverInitializesMissingLayoutOrInventsPage()
    {
        using var f = new Fixture(); var actor = f.Actors.Current; var owner = new LauncherNavigationGoProvider(f.Store);
        Assert.Empty(await Results(owner, actor));
        Assert.Null(await owner.ResolveForActorAsync(new("Launcher", "launcher.page", Guid.NewGuid().ToString("D")), actor, default));
        Assert.Equal(0, f.Provider.Calls);
        Assert.DoesNotContain((await f.Home.ReadAsync()).State!.Records, r => r.RecordType is "launcher.layout" or "home.installed-apps");
    }
    [Fact]
    public async Task ChangedActualLayoutBetweenOriginalQueryRowsRejectsRemainderWithoutRetargeting()
    {
        using var f = new Fixture(); var actor = f.Actors.Current; var initial = await f.Store.GetForActorAsync(actor);
        var two = await f.Store.EditAsync(initial, layout => LauncherLayoutEdits.AddPage(layout, "Work"));
        var owner = new LauncherNavigationGoProvider(f.Store);
        await using var iterator = owner.QueryForActorAsync(new("", "Launcher Pages"), actor, default).GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync()); var emitted = iterator.Current;
        var changed = await f.Store.EditAsync(two, layout => LauncherLayoutEdits.AddPage(layout, "Third"));
        var bytes = await File.ReadAllBytesAsync(f.StatePath);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => { await iterator.MoveNextAsync(); });
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
        var resolved = await owner.ResolveForActorAsync(new("Launcher", "launcher.page", emitted.Reference.Id), actor, default);
        Assert.NotNull(resolved); Assert.NotEqual(emitted.Reference.Revision, resolved.Reference.Revision);
        Assert.Equal(changed.Current.ActivePageId, (await f.Store.GetForActorAsync(actor)).Current.ActivePageId);
    }
    [Fact]
    public async Task RevocationDuringActualHomeWriteLeaseRejectsOriginalGoNavigationWithoutDurableChange()
    {
        using var f = new Fixture(); var actor = f.Actors.Current; var initial = await f.Store.GetForActorAsync(actor);
        await f.Store.EditAsync(initial, layout => LauncherLayoutEdits.AddPage(layout, "Work"));
        var blocked = new LeaseBlockingHomeStore(f.Home, f.StatePath);
        var store = new HomeLauncherLayoutStore(blocked, f.Actors, new ResourceAuthorizationService(f.Actors, [new LauncherLayoutResourceResolver(f.Home)]), f.Registry);
        var owner = new LauncherNavigationGoProvider(store); var home = Assert.Single(await Results(owner, actor, "Home"));
        var bytes = await File.ReadAllBytesAsync(f.StatePath);
        var pending = owner.InvokeForActorAsync(home.Reference, "OpenPage", actor, default);
        await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Actors.Current = actor with { AuthenticationRevision = "revoked" }; blocked.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Results(owner, actor));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ResolveForActorAsync(new("Launcher", "launcher.page", home.Reference.Id), actor, default));
    }
    private static async Task<List<GoResult>> Results(LauncherNavigationGoProvider owner, AuthenticatedResourceActor actor, string query = "")
    { var list = new List<GoResult>(); await foreach (var result in owner.QueryForActorAsync(new(query, "Launcher Pages"), actor, default)) list.Add(result); return list; }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class Provider : IInstalledApplicationObservationProvider
    {
        public string ProviderId => "android-test"; public int Calls; public Action? DuringObservation = null;
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Calls++; DuringObservation?.Invoke(); return ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>([new("personal", "Personal", false, true, [new("first", "first/main", "First app", "1", true)])]); }
    }
    private sealed class AmbientRegistry : IInstalledApplicationRegistry
    {
        public int Calls;
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct)
        { Calls++; throw new InvalidOperationException("Ambient registry must not be used."); }
        public ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct) => throw new InvalidOperationException("No launch fixture.");
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "astra-original-launcher-init-" + Guid.NewGuid());
        public string StatePath => Path.Combine(directory, "home.json");
        public FileHomeCoreStateStore Home { get; } public Actors Actors { get; } = new(); public Provider Provider { get; } = new();
        public HomeInstalledApplicationRegistry Registry { get; } public HomeLauncherLayoutStore Store { get; }
        public Fixture() { Home = new(StatePath); Registry = new(Home, Actors, [Provider]); Store = Owner(Registry); }
        public HomeLauncherLayoutStore Owner(IInstalledApplicationRegistry registry) => new(Home, Actors, new ResourceAuthorizationService(Actors, [new LauncherLayoutResourceResolver(Home)]), registry);
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
