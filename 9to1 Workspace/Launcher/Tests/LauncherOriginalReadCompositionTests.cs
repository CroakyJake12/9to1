using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Launcher;
namespace NineToOne.Launcher.Tests;

public sealed class LauncherOriginalReadCompositionTests
{
    [Fact]
    public async Task RealFirstInitializedLayoutIssuesOriginalSnapshotAndReturnsOnlyItsCapturedIdentity()
    {
        using var f = new Fixture(); var original = f.Actors.Current;
        var layout = await f.Store.GetForActorAsync(original);
        var snapshot = Assert.IsType<LauncherSessionSnapshot>(await f.Session.ReadForActorAsync(original));
        Assert.Equal(layout.AuthorityId, snapshot.Layout.AuthorityId); Assert.Equal(layout.Revision, snapshot.Layout.Revision);
        Assert.Same(original, await f.Session.RequireOriginalActorAsync(snapshot));
        Assert.True(await f.Session.IsCurrentAsync(snapshot)); Assert.Single(snapshot.Layout.Current.ActivePage.Items);
    }
    [Fact]
    public async Task ReplacedActorCannotReadOrIssueAnotherSnapshotBeforeHomeObservation()
    {
        using var f = new Fixture(); var original = f.Actors.Current;
        f.Actors.Current = original with { AuthenticationRevision = "replacement" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Session.ReadForActorAsync(original));
        Assert.False(File.Exists(f.StatePath)); Assert.Equal(0, f.Provider.Calls);
    }
    [Fact]
    public async Task RevokedDisplayedSnapshotCannotSupplyDrawerIdentityOrReadReplacementLayout()
    {
        using var f = new Fixture(); var original = f.Actors.Current; await f.Store.GetForActorAsync(original);
        var snapshot = (await f.Session.ReadForActorAsync(original))!; var bytes = await File.ReadAllBytesAsync(f.StatePath);
        f.Actors.Current = original with { AuthenticationRevision = "replacement" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Session.RequireOriginalActorAsync(snapshot));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Session.ReadForActorAsync(original));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task SameHomeAndActorForeignIssuerCannotSupplyDisplayedIdentity()
    {
        using var f = new Fixture(); var original = f.Actors.Current; await f.Store.GetForActorAsync(original);
        var foreign = f.NewSession(); var snapshot = (await foreign.ReadForActorAsync(original))!;
        Assert.True(await foreign.IsCurrentAsync(snapshot));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Session.RequireOriginalActorAsync(snapshot));
        Assert.Same(original, await foreign.RequireOriginalActorAsync(snapshot));
    }
    private sealed class UnavailablePeer : IHomeNativeInstalledPeerVerifier
    { public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer peer, CancellationToken ct) => ValueTask.FromResult<HomeNativeInstalledPeer?>(null); }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class Provider : IInstalledApplicationObservationProvider
    {
        public string ProviderId => "android-test"; public int Calls; public Action? DuringObservation;
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
        public HomeInstalledApplicationRegistry Registry { get; } public HomeLauncherLayoutStore Store { get; } public HomeLauncherSession Session { get; }
        public Fixture() { Home = new(StatePath); Registry = new(Home, Actors, [Provider]); Store = Owner(Registry); Session = NewSession(); }
        public HomeLauncherSession NewSession() => new(Store, Actors, new HomeNativeWidgetRegistry(new UnavailablePeer(), Actors, new ResourceAuthorizationService(Actors, [new LauncherLayoutResourceResolver(Home)])));
        public HomeLauncherLayoutStore Owner(IInstalledApplicationRegistry registry) => new(Home, Actors, new ResourceAuthorizationService(Actors, [new LauncherLayoutResourceResolver(Home)]), registry);
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
