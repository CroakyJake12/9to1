using Haven.Application;
using Haven.Application.Go;
using HavenOS.Home.Core;
using NineToOne.Launcher;
namespace Haven.Android;

public sealed class AndroidOriginalAppSelectionTests
{
    [Fact]
    public async Task ActualIssuedOriginalTileDelegatesCanonicalReferenceToOriginalProductionGoOwner()
    {
        using var f = new Fixture(); var original = await f.Seed(); var view = new object();
        var result = Assert.Single(await f.Results()); var reference = result.Reference;
        var selection = f.Actions.Issue(original, view, reference, () => true);
        var bytes = await File.ReadAllBytesAsync(f.StatePath);
        await f.Actions.InvokeAsync(selection, view, default);
        Assert.Equal(("personal", "app/main"), Assert.Single(f.Platform.Calls));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task WrongViewForeignIssuerAndUnissuedCopyCannotDispatchOriginalAction()
    {
        using var f = new Fixture(); var original = await f.Seed(); var view = new object(); var result = Assert.Single(await f.Results());
        var selection = f.Actions.Issue(original, view, result.Reference, () => true);
        var foreign = new AndroidLauncherOriginalAppActions<object>(f.Session, f.Go);
        var copied = new AndroidLauncherOriginalAppSelection<object>(f.Actions, original, view, result.Reference, () => true);
        var bytes = await File.ReadAllBytesAsync(f.StatePath); var reads = f.Observations.Calls;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Actions.InvokeAsync(selection, new object(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.InvokeAsync(selection, view, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Actions.InvokeAsync(copied, view, default));
        Assert.Empty(f.Platform.Calls); Assert.Equal(reads, f.Observations.Calls); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task ValidReplacementSessionCannotReassociateOriginalTileToNewActor()
    {
        using var f = new Fixture(); var original = await f.Seed(); var view = new object(); var result = Assert.Single(await f.Results());
        var selection = f.Actions.Issue(original, view, result.Reference, () => true);
        f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "replacement" };
        var replacement = (await f.Session.ReadForActorAsync(f.Actors.Current))!;
        Assert.True(await f.Session.IsCurrentAsync(replacement));
        var bytes = await File.ReadAllBytesAsync(f.StatePath); var reads = f.Observations.Calls;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Actions.InvokeAsync(selection, view, default));
        Assert.Empty(f.Platform.Calls); Assert.Equal(reads, f.Observations.Calls); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalHomeReadPendingCannotAdoptDetachedTileOrReplacementActor(bool replaceActor)
    {
        using var f = new Fixture(); var original = await f.Seed(); var view = new object(); var live = true; var result = Assert.Single(await f.Results());
        var selection = f.Actions.Issue(original, view, result.Reference, () => live);
        var bytes = await File.ReadAllBytesAsync(f.StatePath); var reads = f.Observations.Calls;
        f.Actors.Suspend = true; var pending = f.Actions.InvokeAsync(selection, view, default);
        await f.Actors.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (replaceActor) f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "replacement" }; else live = false;
        f.Actors.Release.TrySetResult(); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Empty(f.Platform.Calls); Assert.Equal(reads, f.Observations.Calls); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task NativeOriginalViewRetirementInsideActualOwnerProviderReadCannotReachPlatformLaunch()
    {
        using var f = new Fixture(); var original = await f.Seed(); var view = new object(); var live = true; var result = Assert.Single(await f.Results());
        var selection = f.Actions.Issue(original, view, result.Reference, () => live);
        var bytes = await File.ReadAllBytesAsync(f.StatePath);
        f.Observations.DuringObservation = () => live = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Actions.InvokeAsync(selection, view, default));
        Assert.False(live); Assert.Empty(f.Platform.Calls); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "home-profile", null, null, "session"); public bool Suspend;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        { var actor = Current; if (Suspend) { Suspend = false; Entered.TrySetResult(); await Release.Task.WaitAsync(ct); } return actor; }
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class Observations : IInstalledApplicationObservationProvider
    {
        public string ProviderId => AndroidLauncherPlatformCatalog.ProviderId; public int Calls; public Action? DuringObservation;
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Calls++; DuringObservation?.Invoke(); return ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>([new("personal", "Personal", false, true, [new("app", "app/main", "App", "1", true)])]); }
    }
    private sealed class UnavailablePeer : IHomeNativeInstalledPeerVerifier
    { public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer peer, CancellationToken ct) => ValueTask.FromResult<HomeNativeInstalledPeer?>(null); }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "astra-original-app-selection-" + Guid.NewGuid());
        public string StatePath => Path.Combine(root, "home.json"); public Actors Actors { get; } = new(); public Observations Observations { get; } = new();
        public AndroidLauncherPlatformCatalog Platform { get; } = new(); public HomeInstalledApplicationRegistry Registry { get; }
        public AndroidInstalledApplicationsGoProvider Go { get; } public HomeLauncherLayoutStore Store { get; } public HomeLauncherSession Session { get; }
        public AndroidLauncherOriginalAppActions<object> Actions { get; }
        public Fixture()
        {
            var home = new FileHomeCoreStateStore(StatePath); Registry = new(home, Actors, [Observations]);
            var resources = new ResourceAuthorizationService(Actors, [new AndroidInstalledApplicationResourceResolver(Registry), new LauncherLayoutResourceResolver(home)]);
            Store = new(home, Actors, resources, Registry); Session = new(Store, Actors, new HomeNativeWidgetRegistry(new UnavailablePeer(), Actors, resources));
            Go = new(Registry, Actors, resources, Platform); Actions = new(Session, Go);
        }
        public async Task<LauncherSessionSnapshot> Seed()
        { await Store.GetForActorAsync(Actors.Current); return (await Session.ReadForActorAsync(Actors.Current))!; }
        public async Task<List<GoResult>> Results()
        { var list = new List<GoResult>(); await foreach (var result in Go.QueryForActorAsync(new("", "Apps"), Actors.Current, default)) list.Add(result); return list; }
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
