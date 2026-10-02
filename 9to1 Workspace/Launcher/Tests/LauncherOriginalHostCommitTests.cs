using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Launcher;
namespace NineToOne.Launcher.Tests;
public sealed class LauncherOriginalHostCommitTests
{
    [Fact]
    public async Task GenuinePrivateOriginalSessionAndLiveHostPersistActualHomeAndRenewExactReceipt()
    {
        using var f = new Fixture(); await f.Store.GetForActorAsync(f.Actors.Current);
        var original = (await f.Session.ReadForActorAsync(f.Actors.Current))!;
        var saved = await f.Session.EditForOriginalHostAsync(original, layout => LauncherLayoutEdits.AddPage(layout, "Work"), () => true);
        var renewed = await f.Session.ReadAfterEditAsync(original, saved);
        Assert.NotNull(renewed); Assert.Equal(original.Layout.Revision + 1, renewed.Layout.Revision);
        Assert.Contains((await f.Store.ReadExistingForActorAsync(f.Actors.Current))!.Current.Pages, page => page.Name == "Work");
    }
    [Fact]
    public async Task RetiredOriginalHostCannotStartActualOriginalHomeEdit()
    {
        using var f = new Fixture(); await f.Store.GetForActorAsync(f.Actors.Current);
        var original = (await f.Session.ReadForActorAsync(f.Actors.Current))!;
        var bytes = await File.ReadAllBytesAsync(f.StatePath);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Session.EditForOriginalHostAsync(original,
            layout => LauncherLayoutEdits.AddPage(layout, "Denied"), () => false));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath)); Assert.True(await f.Session.IsCurrentAsync(original));
    }
    [Fact]
    public async Task HostRetirementBehindRealHomeWriterLeaseDeniesCommitEvenWithUnchangedActualActor()
    {
        using var f = new Fixture(); await f.Store.GetForActorAsync(f.Actors.Current);
        var blocked = new LeaseBlockingHomeStore(f.Home, f.StatePath); var session = f.NewSession(f.Owner(f.Registry, blocked));
        var original = (await session.ReadForActorAsync(f.Actors.Current))!; var actor = f.Actors.Current;
        var bytes = await File.ReadAllBytesAsync(f.StatePath); var hostLive = true;
        var pending = session.EditForOriginalHostAsync(original, layout => LauncherLayoutEdits.AddPage(layout, "Denied"), () => hostLive);
        await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); hostLive = false; blocked.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Same(actor, f.Actors.Current); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath)); Assert.True(await session.IsCurrentAsync(original));
    }
    [Fact]
    public async Task HostRetirementDuringActualInnerPublicationGuardDeniesFinalHomePublication()
    {
        using var f = new Fixture(); await f.Store.GetForActorAsync(f.Actors.Current);
        var original = (await f.Session.ReadForActorAsync(f.Actors.Current))!; var bytes = await File.ReadAllBytesAsync(f.StatePath);
        var hostLive = true; f.Actors.HoldPublication = true;
        var pending = f.Session.EditForOriginalHostAsync(original, layout => LauncherLayoutEdits.AddPage(layout, "Denied"), () => hostLive);
        await f.Actors.GuardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); hostLive = false; f.Actors.GuardRelease.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath)); Assert.True(await f.Session.IsCurrentAsync(original));
    }
    [Fact]
    public async Task TrueHostPredicateCannotReplaceActualLockedStateActorGuardDenial()
    {
        using var f = new Fixture(); await f.Store.GetForActorAsync(f.Actors.Current);
        var blocked = new LeaseBlockingHomeStore(f.Home, f.StatePath); var session = f.NewSession(f.Owner(f.Registry, blocked));
        var original = (await session.ReadForActorAsync(f.Actors.Current))!; var bytes = await File.ReadAllBytesAsync(f.StatePath);
        var pending = session.EditForOriginalHostAsync(original, layout => LauncherLayoutEdits.AddPage(layout, "Denied"), () => true);
        await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); f.Actors.CommitAllowed = false; blocked.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task HostRetiresAfterActualPersistedSuccessRetainsKnownOriginalRevisionAndPrivateReceipt()
    {
        using var f = new Fixture(); await f.Store.GetForActorAsync(f.Actors.Current); var hostLive = true;
        var observed = new PostPersistObservationHomeStore(f.Home, () => hostLive = false);
        var session = f.NewSession(f.Owner(f.Registry, observed)); var original = (await session.ReadForActorAsync(f.Actors.Current))!;
        var bytes = await File.ReadAllBytesAsync(f.StatePath);
        var saved = await session.EditForOriginalHostAsync(original, layout => LauncherLayoutEdits.AddPage(layout, "Committed"), () => hostLive);
        Assert.False(hostLive); Assert.Equal(1, observed.ActualSuccesses); Assert.Equal(original.Layout.Revision + 1, saved.Revision);
        Assert.NotEqual(bytes, await File.ReadAllBytesAsync(f.StatePath));
        Assert.Equal(saved.Revision, (await f.Home.ReadAsync()).State!.Records.Single(record => record.RecordId == saved.AuthorityId).Revision);
        Assert.False(await session.IsCurrentAsync(original));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.ReadAfterEditAsync(original, saved with { }));
        Assert.NotNull(await session.ReadAfterEditAsync(original, saved)); // Home receipt is genuine; caller's false host still forbids native adoption.
        var committedBytes = await File.ReadAllBytesAsync(f.StatePath);
        await Assert.ThrowsAsync<IOException>(() => session.EditForOriginalHostAsync(original, layout => LauncherLayoutEdits.AddPage(layout, "Replay"), () => true));
        Assert.Equal(committedBytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task ActorRetiresAfterActualPersistedSuccessReturnsAckButCannotRenewOrReplayIntoReplacement()
    {
        using var f = new Fixture(); await f.Store.GetForActorAsync(f.Actors.Current); var actor = f.Actors.Current;
        var observed = new PostPersistObservationHomeStore(f.Home, () => f.Actors.Current = actor with { AuthenticationRevision = "retired after actual commit" });
        var session = f.NewSession(f.Owner(f.Registry, observed)); var original = (await session.ReadForActorAsync(actor))!;
        var saved = await session.EditForOriginalHostAsync(original, layout => LauncherLayoutEdits.AddPage(layout, "Committed"), () => true);
        Assert.Equal(1, observed.ActualSuccesses); Assert.Equal(original.Layout.Revision + 1, saved.Revision);
        Assert.Null(await session.ReadAfterEditAsync(original, saved)); Assert.False(await session.IsCurrentAsync(original));
        var bytes = await File.ReadAllBytesAsync(f.StatePath);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.EditForOriginalHostAsync(original, layout => LauncherLayoutEdits.AddPage(layout, "Replay"), () => true));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    private sealed class PostPersistObservationHomeStore(IHomeCoreStateStore actual, Action afterActualSuccess) : IHomeCoreStateStore
    {
        public int ActualSuccesses;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => actual.ReadAsync(ct);
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long revision, CancellationToken ct = default)
        { var result = await actual.WriteAsync(record, revision, ct); Observe(result); return result; }
        public async Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long revision,
            AuthenticatedResourceActor expected, IHomeStateCommitActorGuard guard, CancellationToken ct = default)
        { var result = await actual.WriteGuardedAsync(record, revision, expected, guard, ct); Observe(result); return result; }
        private void Observe(HomeStateWriteResult result)
        { if (result.IsSuccess) { ActualSuccesses++; afterActualSuccess(); } }
    }
    private sealed class UnavailablePeer : IHomeNativeInstalledPeerVerifier
    { public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer peer, CancellationToken ct) => ValueTask.FromResult<HomeNativeInstalledPeer?>(null); }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public bool CommitAllowed = true; public bool HoldPublication;
        public TaskCompletionSource GuardEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource GuardRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct)
        {
            if (HoldPublication && phase == HomeStateCommitPhase.Publication)
            { GuardEntered.TrySetResult(); await GuardRelease.Task.WaitAsync(TimeSpan.FromSeconds(5), ct); }
            return Current == expected && CommitAllowed;
        }
    }
    private sealed class Provider : IInstalledApplicationObservationProvider
    {
        public string ProviderId => "android-test"; public int Calls;
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Calls++; return ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>([new("personal", "Personal", false, true, [new("first", "first/main", "First app", "1", true)])]); }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "astra-original-launcher-init-" + Guid.NewGuid());
        public string StatePath => Path.Combine(directory, "home.json");
        public FileHomeCoreStateStore Home { get; } public Actors Actors { get; } = new(); public Provider Provider { get; } = new();
        public HomeInstalledApplicationRegistry Registry { get; } public HomeLauncherLayoutStore Store { get; } public HomeLauncherSession Session { get; }
        public Fixture() { Home = new(StatePath); Registry = new(Home, Actors, [Provider]); Store = Owner(Registry); Session = NewSession(); }
        public HomeLauncherSession NewSession(HomeLauncherLayoutStore? owner = null) => new(owner ?? Store, Actors, new HomeNativeWidgetRegistry(new UnavailablePeer(), Actors, new ResourceAuthorizationService(Actors, [new LauncherLayoutResourceResolver(Home)])));
        public HomeLauncherLayoutStore Owner(IInstalledApplicationRegistry registry, IHomeCoreStateStore? home = null) => new(home ?? Home, Actors, new ResourceAuthorizationService(Actors, [new LauncherLayoutResourceResolver(Home)]), registry);
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
