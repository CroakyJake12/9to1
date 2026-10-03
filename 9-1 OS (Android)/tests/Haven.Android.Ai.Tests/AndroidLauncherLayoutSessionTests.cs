using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Launcher;
using Xunit;

namespace Haven.Android;

public sealed class AndroidLauncherLayoutSessionTests
{
    [Fact]
    public async Task QueuedOldDialogCannotAdoptRefreshedAuthenticationSessionAtSameRevision()
    {
        using var f = new Fixture(); await f.Store.GetAsync();
        var original = f.Displayed.Bind((await f.Session.ReadAsync())!);
        var before = await File.ReadAllBytesAsync(f.Path);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task QueuedEdit() { await release.Task; await f.Displayed.EditAsync(original, layout => LauncherLayoutEdits.AddPage(layout, "Stale dialog")); }
        var queued = QueuedEdit();
        f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "new-session" };
        var fresh = f.Displayed.Bind((await f.Session.ReadAsync())!);
        Assert.Equal(original.Revision, fresh.Revision);
        release.SetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => queued);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.Path));
        var saved = await f.Displayed.EditAsync(fresh, layout => LauncherLayoutEdits.AddPage(layout, "Current dialog"));
        Assert.Equal(fresh.Revision + 1, saved.Revision);
        Assert.Equal("Current dialog", saved.Current.ActivePage.Name);
    }
    [Fact]
    public async Task CopiedLayoutDataIsNotAHostSessionAndProfileChangeCannotInitializeAnotherOwner()
    {
        using var f = new Fixture(); await f.Store.GetAsync();
        var original = f.Displayed.Bind((await f.Session.ReadAsync())!);
        Assert.Throws<UnauthorizedAccessException>(() => f.Displayed.Require(original with { }));
        var before = await File.ReadAllBytesAsync(f.Path);
        f.Actors.Current = f.Actors.Current with { ProfileId = "another-profile" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Displayed.EditAsync(original, layout => LauncherLayoutEdits.AddPage(layout, "Wrong profile")));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.Path));
    }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class Unavailable : IHomeNativeInstalledPeerVerifier
    { public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer peer, CancellationToken ct) => ValueTask.FromResult<HomeNativeInstalledPeer?>(null); }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "astra-android-layout-session-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_root, "home.json");
        public Actors Actors { get; } = new();
        public HomeLauncherLayoutStore Store { get; }
        public HomeLauncherSession Session { get; }
        public AndroidLauncherLayoutSessions Displayed { get; }
        public Fixture()
        {
            var home = new FileHomeCoreStateStore(Path);
            var resources = new ResourceAuthorizationService(Actors, [new LauncherLayoutResourceResolver(home)]);
            Store = new(home, Actors, resources, new HomeInstalledApplicationRegistry(home, Actors, []));
            Session = new(Store, Actors, new HomeNativeWidgetRegistry(new Unavailable(), Actors, resources));
            Displayed = new(Session);
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
