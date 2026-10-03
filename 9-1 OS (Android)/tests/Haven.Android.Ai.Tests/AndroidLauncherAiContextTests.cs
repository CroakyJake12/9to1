using Haven.Application;
using Haven.Application.Go;
using HavenOS.Home.Core;
using NineToOne.Launcher;
using Xunit;

namespace Haven.Android;

public sealed class AndroidLauncherAiContextTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AiContextUsesActualPermissionFilteredGoReferencesAndCurrentRegistry(bool allowApplicationRead)
    {
        using var f = new Fixture(allowApplicationRead); await f.Store.GetAsync();
        var snapshot = await f.Context.CaptureAsync(default);
        var apps = snapshot.SemanticState["applications"].EnumerateArray().ToArray();
        if (allowApplicationRead)
        {
            var app = Assert.Single(apps);
            Assert.Equal("Sensitive application label", app.GetProperty("Label").GetString());
            Assert.Equal("device-user-17", app.GetProperty("PlatformProfileId").GetString());
            Assert.True(app.GetProperty("IsManaged").GetBoolean());
            Assert.Equal(Assert.Single(await f.Registry.RefreshAsync(default)).ApplicationId, app.GetProperty("ApplicationId").GetGuid());
        }
        else Assert.Empty(apps);
        Assert.Empty(f.Platform.Calls);
    }

    [Fact]
    public async Task SessionChangeDuringGoDiscoveryCannotReturnCapturedLayoutOrApplicationLabels()
    {
        using var f = new Fixture(true); await f.Store.GetAsync();
        f.Observations.OnObserve = () => f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "changed" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Context.CaptureAsync(default));
        Assert.Empty(f.Platform.Calls);
    }

    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class Observations : IInstalledApplicationObservationProvider
    {
        public string ProviderId => AndroidLauncherPlatformCatalog.ProviderId;
        public Action? OnObserve;
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct)
        { OnObserve?.Invoke(); return ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>([new("device-user-17", "Actual work profile", true, true, [new("app", "app/main", "Sensitive application label", "1", true)])]); }
    }
    private sealed class UnavailablePeer : IHomeNativeInstalledPeerVerifier
    {
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer peer, CancellationToken ct) => ValueTask.FromResult<HomeNativeInstalledPeer?>(null);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-launcher-ai-context-" + Guid.NewGuid().ToString("N"));
        public Actors Actors { get; } = new(); public Observations Observations { get; } = new();
        public AndroidLauncherPlatformCatalog Platform { get; } = new();
        public HomeInstalledApplicationRegistry Registry { get; } public HomeLauncherLayoutStore Store { get; }
        public LauncherAppAiContext Context { get; }
        public Fixture(bool applicationRead)
        {
            var home = new FileHomeCoreStateStore(Path.Combine(_root, "home.json")); Registry = new(home, Actors, [Observations]);
            var resolvers = new List<ICanonicalResourceAccessResolver> { new LauncherLayoutResourceResolver(home) };
            if (applicationRead) resolvers.Add(new AndroidInstalledApplicationResourceResolver(Registry));
            var resources = new ResourceAuthorizationService(Actors, resolvers);
            Store = new(home, Actors, resources, Registry);
            var sessions = new HomeLauncherSession(Store, Actors, new HomeNativeWidgetRegistry(new UnavailablePeer(), Actors, resources));
            Context = new(sessions, new GoService([new AndroidInstalledApplicationsGoProvider(Registry, Actors, resources, Platform)]), Registry);
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
