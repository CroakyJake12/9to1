using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Launcher;
using Xunit;

namespace Haven.Android;

public sealed class AndroidLauncherWidgetTests
{
    [Fact]
    public async Task StalePlatformPhaseCannotCompleteCurrentSelectionOrReleaseCanonicalConfiguration()
    {
        using var f = new Fixture(); var original = await f.Seed();
        var phase = new AndroidLauncherWidgetSelectionPhase(); phase.Begin(17);
        Assert.False(phase.IsExpected(17, true)); // Stale configure result during a new picker.
        Assert.False(phase.IsExpected(16, false)); // Foreign old allocation.
        phase.Complete(16); Assert.True(phase.IsExpected(17, false));
        phase.BeginConfiguration(17); Assert.False(phase.IsExpected(17, false));
        Assert.True(phase.IsExpected(17, true));
        Assert.Throws<InvalidOperationException>(() => phase.BeginConfiguration(17));
        phase.Complete(17); Assert.False(phase.IsExpected(17, true));
        phase.Begin(18); Assert.False(phase.IsExpected(17, false));
        Assert.True(await f.Session.IsCurrentAsync(original));
        Assert.Single(f.Bindings.Read()); Assert.Equal(0, f.Platform.Released);
        Assert.Equal(Assert.Single(original.Layout.Current.Widgets), Assert.Single((await f.Session.ReadAsync())!.Layout.Current.Widgets));
    }

    [Fact]
    public void DeviceCacheRejectsCrossProfileReassignmentAndPreservesCorruption()
    {
        var storage = new Storage(); var bindings = new AndroidLauncherWidgetBindings(storage);
        var value = new AndroidLauncherWidgetBinding(7, "home-one", Guid.NewGuid(), new("owner/Widget", "personal"));
        bindings.Associate(value); var original = storage.Value;
        Assert.Throws<UnauthorizedAccessException>(() => bindings.Associate(value with { HomeAuthorityId = "home-two" }));
        Assert.Equal(original, storage.Value); Assert.Null(bindings.Find("home-two", value.PlacementId!.Value));
        storage.Value = "broken";
        Assert.Throws<InvalidDataException>(() => bindings.Associate(value)); Assert.Equal("broken", storage.Value);
        storage.Value = original; storage.AllowWrites = false;
        Assert.Throws<IOException>(() => bindings.Forget(value)); Assert.Equal(value, Assert.Single(bindings.Read()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualHomeSessionPreventsOrReleasesViewWhenActorChanges(bool duringConstruction)
    {
        using var f = new Fixture(); var snapshot = await f.Seed();
        var id = Assert.Single(snapshot.Layout.Current.Widgets).Id;
        if (duringConstruction) f.Platform.OnCreate = () => f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "revoked" };
        else f.Actors.Current = f.Actors.Current with { ProfileId = "other-profile" };
        Assert.Null(await f.Views.CreateAsync(snapshot, id));
        Assert.Equal(duringConstruction ? 1 : 0, f.Platform.Created);
        Assert.Equal(f.Platform.Created, f.Platform.Released);
        Assert.Single(f.Bindings.Read());
    }

    [Fact]
    public async Task ViewNeedsStoredPlacementMatchingActualOsOwnershipAndCurrentLayout()
    {
        using var f = new Fixture(); var snapshot = await f.Seed();
        var widget = Assert.Single(snapshot.Layout.Current.Widgets);
        f.Platform.Provider = new("other/Widget", "personal");
        Assert.Null(await f.Views.CreateAsync(snapshot, widget.Id)); Assert.Equal(0, f.Platform.Created);
        f.Platform.Provider = widget.Android;
        Assert.NotNull(await f.Views.CreateAsync(snapshot, widget.Id)); Assert.Equal(1, f.Platform.Created);
        await f.Store.EditAsync(snapshot.Layout, layout => LauncherLayoutEdits.RemoveWidget(layout, widget.Id));
        Assert.Null(await f.Views.CreateAsync(snapshot, widget.Id)); Assert.Equal(1, f.Platform.Created);
        Assert.Single(f.Bindings.Read()); // Stale local data does not resurrect canonical placement.
    }

    [Fact]
    public async Task ChangedOsOwnerAfterConstructionDisposesUnexposedView()
    {
        using var f = new Fixture(); var snapshot = await f.Seed();
        f.Platform.OnCreate = () => f.Platform.Provider = null;
        Assert.Null(await f.Views.CreateAsync(snapshot, Assert.Single(snapshot.Layout.Current.Widgets).Id));
        Assert.Equal(1, f.Platform.Created); Assert.Equal(1, f.Platform.Released);
    }

    [Fact]
    public async Task CacheReassignmentDuringConstructionCannotExposeOldPlacementView()
    {
        using var f = new Fixture(); var snapshot = await f.Seed();
        var binding = Assert.Single(f.Bindings.Read());
        f.Platform.OnCreate = () => f.Bindings.Associate(binding with { PlacementId = Guid.NewGuid() });
        Assert.Null(await f.Views.CreateAsync(snapshot, binding.PlacementId!.Value));
        Assert.Equal(1, f.Platform.Created); Assert.Equal(1, f.Platform.Released);
    }

    private sealed class Storage : IAndroidLauncherWidgetBindingStorage
    {
        public string? Value; public bool AllowWrites = true;
        public string? Read() => Value;
        public bool Write(string value) { if (!AllowWrites) return false; Value = value; return true; }
    }
    private sealed class Platform : IAndroidLauncherWidgetPlatform<object>
    {
        public LauncherAndroidWidgetReference? Provider = new("owner/Widget", "personal");
        public Action? OnCreate; public int Created; public int Released;
        public LauncherAndroidWidgetReference? ReadOwnedProvider(int id) => id == 7 ? Provider : null;
        public object CreateView(int id) { Created++; OnCreate?.Invoke(); return new(); }
        public void ReleaseView(object view) => Released++;
    }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class UnavailablePeer : IHomeNativeInstalledPeerVerifier
    {
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer peer, CancellationToken ct) => ValueTask.FromResult<HomeNativeInstalledPeer?>(null);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-android-widget-" + Guid.NewGuid().ToString("N"));
        public Actors Actors { get; } = new(); public Platform Platform { get; } = new();
        public AndroidLauncherWidgetBindings Bindings { get; } = new(new Storage());
        public HomeLauncherLayoutStore Store { get; } public HomeLauncherSession Session { get; }
        public AndroidLauncherWidgetViews<object> Views { get; }
        public Fixture()
        {
            var home = new FileHomeCoreStateStore(Path.Combine(_root, "home.json"));
            var resources = new ResourceAuthorizationService(Actors, [new LauncherLayoutResourceResolver(home)]);
            Store = new(home, Actors, resources, new HomeInstalledApplicationRegistry(home, Actors, []));
            Session = new(Store, Actors, new HomeNativeWidgetRegistry(new UnavailablePeer(), Actors, resources));
            Views = new(Session, Bindings, Platform);
        }
        public async Task<LauncherSessionSnapshot> Seed()
        {
            var initial = await Store.GetAsync();
            var saved = await Store.EditAsync(initial, layout => LauncherLayoutEdits.AddWidget(layout, layout.ActivePageId, "Owner widget", 2, 2, android: Platform.Provider));
            Bindings.Associate(new(7, saved.AuthorityId, Assert.Single(saved.Current.Widgets).Id, Platform.Provider!));
            return (await Session.ReadAsync())!;
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
