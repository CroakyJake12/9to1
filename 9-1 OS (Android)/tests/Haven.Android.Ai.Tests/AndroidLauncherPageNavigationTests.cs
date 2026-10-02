using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Launcher;
using Xunit;

namespace Haven.Android;

public sealed class AndroidLauncherPageNavigationTests
{
    [Fact]
    public async Task ExactOriginalPageCommitsAndRenewsSameSessionWithoutChangingOtherPages()
    {
        using var f = new Fixture(); var original = await f.Seed();
        var page = original.Layout.Current.Pages.Last().Id;
        var renewed = await f.Navigation.SelectAsync(original, page, () => true);
        Assert.Equal(page, renewed.Layout.Current.ActivePageId);
        Assert.Equal(original.Layout.AuthorityId, renewed.Layout.AuthorityId);
        Assert.Equal(original.Layout.Revision + 1, renewed.Layout.Revision);
        Assert.Equal(original.Layout.Current.Pages.Select(p => p.Id), renewed.Layout.Current.Pages.Select(p => p.Id));
        Assert.True(await f.Session.IsCurrentAsync(renewed));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Navigation.SelectAsync(original, page, () => true));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeldOriginalReadCannotNavigateAfterActivityCloseOrActorReplacement(bool replaceActor)
    {
        using var f = new Fixture(); var original = await f.Seed(); var active = true;
        var before = File.ReadAllBytes(f.HomePath); f.Actors.Suspend = true;
        var pending = f.Navigation.SelectAsync(original, original.Layout.Current.Pages.Last().Id, () => active);
        try
        {
            await f.Actors.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (replaceActor) f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "replacement-session" };
            else active = false;
            f.Actors.Release.TrySetResult(true);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(before, File.ReadAllBytes(f.HomePath));
        }
        finally
        {
            f.Actors.Release.TrySetResult(true);
            try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); } catch (UnauthorizedAccessException) { }
        }
    }
    [Fact]
    public async Task UnknownPageIsRejectedBeforeAnyActorOrHomeRead()
    {
        using var f = new Fixture(); var original = await f.Seed(); var calls = f.Actors.Calls;
        var before = File.ReadAllBytes(f.HomePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Navigation.SelectAsync(original, Guid.NewGuid(), () => true));
        Assert.Equal(calls, f.Actors.Calls); Assert.Equal(before, File.ReadAllBytes(f.HomePath));
    }
    [Fact]
    public async Task BackupImportReadsCanonicalExchangeWithoutChangingOriginalHome()
    {
        using var f = new Fixture(); var original = await f.Seed(); var before = File.ReadAllBytes(f.HomePath);
        using var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(LauncherLayoutExchange.Export(original.Layout)));
        var imported = await AndroidLauncherLayoutDocumentReader.ReadAsync(f.Session, original, bytes, () => true);
        Assert.Equal(original.Layout.Current.Pages.Select(page => page.Id), imported.Pages.Select(page => page.Id));
        Assert.Equal(before, File.ReadAllBytes(f.HomePath));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuspendedBackupReadRejectsChangedOriginalActorOrSelectionWithoutHomeMutation(bool actorChanged)
    {
        using var f = new Fixture(); var original = await f.Seed(); var current = true;
        var before = File.ReadAllBytes(f.HomePath);
        using var bytes = new HeldDocument(System.Text.Encoding.UTF8.GetBytes(LauncherLayoutExchange.Export(original.Layout)));
        var pending = AndroidLauncherLayoutDocumentReader.ReadAsync(f.Session, original, bytes, () => current);
        try
        {
            await bytes.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (actorChanged) f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "foreign-backup-session" };
            else current = false;
            bytes.Release.TrySetResult(true);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(before, File.ReadAllBytes(f.HomePath)); Assert.Equal(1, bytes.Reads);
        }
        finally
        {
            bytes.Release.TrySetResult(true);
            try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); } catch (UnauthorizedAccessException) { }
        }
    }
    private sealed class HeldDocument(byte[] bytes) : MemoryStream(bytes)
    {
        public int Reads;
        public TaskCompletionSource<bool> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { Reads++; Entered.TrySetResult(true); await Release.Task.WaitAsync(ct); return await base.ReadAsync(buffer, ct); }
    }
    [Fact]
    public async Task FolderReadRetainsActualDisplayedFolderAndNeverWritesLayout()
    {
        using var f = new Fixture(); var initial = await f.Seed();
        var saved = await f.Session.EditAsync(initial, layout => LauncherLayoutEdits.CreateFolder(layout, layout.ActivePageId, "Study"));
        var original = (await f.Session.ReadAfterEditAsync(initial, saved))!;
        var displayed = Assert.Single(original.Layout.Current.Folders); var before = File.ReadAllBytes(f.HomePath);
        var folder = await new AndroidLauncherFolderNavigation(f.Session).ReadAsync(original, displayed.Id, () => true);
        Assert.Same(displayed, folder); Assert.Equal("Study", folder.Name); Assert.Empty(folder.Items);
        Assert.Equal(before, File.ReadAllBytes(f.HomePath));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeldFolderReadCannotRetargetAfterActorOrHostReplacement(bool actorChanged)
    {
        using var f = new Fixture(); var initial = await f.Seed();
        var saved = await f.Session.EditAsync(initial, layout => LauncherLayoutEdits.CreateFolder(layout, layout.ActivePageId, "Study"));
        var original = (await f.Session.ReadAfterEditAsync(initial, saved))!;
        var folder = Assert.Single(original.Layout.Current.Folders); var current = true;
        var before = File.ReadAllBytes(f.HomePath); f.Actors.Suspend = true;
        var pending = new AndroidLauncherFolderNavigation(f.Session).ReadAsync(original, folder.Id, () => current);
        try
        {
            await f.Actors.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (actorChanged) f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "foreign-folder-session" };
            else current = false;
            f.Actors.Release.TrySetResult(true);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(before, File.ReadAllBytes(f.HomePath));
        }
        finally
        {
            f.Actors.Release.TrySetResult(true);
            try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); } catch (UnauthorizedAccessException) { }
        }
    }
    [Fact]
    public async Task OldSamePhasePickerResultCannotConsumeNewPrivateDocumentSelection()
    {
        using var f = new Fixture(); var original = await f.Seed(); var before = File.ReadAllBytes(f.HomePath);
        var documents = new AndroidLauncherLayoutDocumentSelections();
        var old = documents.Issue(original, false); var current = documents.Issue(original, false);
        Assert.NotEqual(old.RequestCode, current.RequestCode);
        Assert.Null(documents.Take(old.RequestCode)); Assert.False(documents.IsCurrent(old));
        Assert.Same(current, documents.Take(current.RequestCode)); Assert.Same(original, current.Original);
        Assert.False(current.Export); Assert.True(documents.IsCurrent(current));
        Assert.Null(documents.Take(current.RequestCode));
        documents.Cancel(); Assert.False(documents.IsCurrent(current));
        Assert.Equal(before, File.ReadAllBytes(f.HomePath));
    }
    [Fact]
    public async Task HomeCancelAndOtherActivityCannotReuseOriginalDocumentRequestIdentity()
    {
        using var f = new Fixture(); var original = await f.Seed(); var before = File.ReadAllBytes(f.HomePath);
        var first = new AndroidLauncherLayoutDocumentSelections(); var selected = first.Issue(original, true);
        first.Cancel(); Assert.Null(first.Take(selected.RequestCode));
        var replacement = new AndroidLauncherLayoutDocumentSelections(); var fresh = replacement.Issue(original, true);
        Assert.NotEqual(selected.RequestCode, fresh.RequestCode);
        Assert.Null(replacement.Take(selected.RequestCode)); Assert.Same(fresh, replacement.Take(fresh.RequestCode));
        Assert.True(fresh.Export); Assert.Same(original, fresh.Original); Assert.Equal(before, File.ReadAllBytes(f.HomePath));
    }
    [Fact]
    public async Task ActualRecognizerSwipeUsesOriginalCanonicalPageOperation()
    {
        using var f = new Fixture(); var original = await f.Seed(); var active = true;
        var input = new AndroidLauncherGestureInput(80, 12, () => () => active);
        input.Down(200, 100, 0, 1); input.Move(100, 100, 1);
        var gesture = input.Up(90, 100, 100, 1); Assert.Equal(LauncherGesture.SwipeLeft, gesture);
        Assert.Equal(LauncherCommand.NextPage, (original.Layout.Current.Gestures ?? new()).Resolve(gesture!.Value));
        Assert.NotEqual(original.Layout.Current.ActivePageId, original.Layout.Current.Pages.Last().Id);
        var renewed = await f.Navigation.SelectAsync(original, original.Layout.Current.Pages.Last().Id, () => active);
        Assert.Equal(original.Layout.Current.Pages.Last().Id, renewed.Layout.Current.ActivePageId);
        Assert.Equal(original.Layout.Revision + 1, renewed.Layout.Revision);
    }
    [Fact]
    public async Task ChangedActualHomeGestureLayoutCancelsInProgressSwipeInsteadOfRetargeting()
    {
        using var f = new Fixture(); var original = await f.Seed(); var displayed = original;
        var input = new AndroidLauncherGestureInput(80, 12, () =>
        { var captured = displayed; return () => ReferenceEquals(captured, displayed); });
        input.Down(200, 100, 0, 1);
        var changed = await f.Session.EditAsync(original, layout => LauncherLayoutEdits.SetGestures(layout,
            new LauncherGestures(SwipeLeft: LauncherCommand.OpenSettings)));
        displayed = (await f.Session.ReadAfterEditAsync(original, changed))!;
        var before = File.ReadAllBytes(f.HomePath);
        Assert.Null(input.Up(90, 100, 100, 1)); Assert.False(input.WasTap);
        Assert.Equal(before, File.ReadAllBytes(f.HomePath));
        Assert.Equal(changed.Current.ActivePageId, displayed.Layout.Current.ActivePageId);
    }
    [Fact]
    public async Task ClosedHostOrReplacementViewCannotJoinOldTapIntoNewDoubleTap()
    {
        using var f = new Fixture(); var original = await f.Seed(); var active = true; object view = new();
        var input = new AndroidLauncherGestureInput(80, 12, () =>
        { var captured = view; return () => active && ReferenceEquals(captured, view); });
        input.Down(10, 10, 0, 1); Assert.Null(input.Up(10, 10, 50, 1)); Assert.True(input.WasTap);
        view = new object(); input.Down(10, 10, 100, 1);
        Assert.Null(input.Up(10, 10, 150, 1)); // First tap belongs to the replacement view, not the prior view.
        input.Down(200, 100, 200, 1); active = false;
        var before = File.ReadAllBytes(f.HomePath);
        Assert.Null(input.Up(90, 100, 250, 1)); Assert.False(input.WasTap);
        Assert.Equal(before, File.ReadAllBytes(f.HomePath)); Assert.True(await f.Session.IsCurrentAsync(original));
    }
    [Fact]
    public async Task ActualHomeLeaseRefusesPageWriteAfterOriginalHostRetiresWithoutActorChange()
    {
        using var f = new Fixture(); var original = await f.Seed(); var current = true;
        var before = File.ReadAllBytes(f.HomePath); var actor = f.Actors.Current; f.Home.Hold = true;
        var pending = f.Navigation.SelectAsync(original, original.Layout.Current.Pages.Last().Id, () => current);
        try
        {
            await f.Home.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            current = false; Assert.Equal(actor, f.Actors.Current); f.Home.Release.TrySetResult();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(before, File.ReadAllBytes(f.HomePath)); Assert.Equal(actor, f.Actors.Current);
        }
        finally { f.Home.Release.TrySetResult(); try { await pending; } catch (UnauthorizedAccessException) { } }
    }
    [Fact]
    public async Task ActualReturnedPageCommitIsRetainedBeforeLateHostRetirementWithoutAdoptingView()
    {
        using var f = new Fixture(); var original = await f.Seed(); var current = true;
        LauncherSessionSnapshot? acknowledgedOriginal = null; LauncherStoredLayout? acknowledged = null;
        var before = File.ReadAllBytes(f.HomePath); var target = original.Layout.Current.Pages.Last().Id;
        // Observe AFTER real FileHome returned success, not inside persistence or a fabricated success path.
        f.Home.AfterSuccess = () => current = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Navigation.SelectAsync(original, target, () => current,
            knownCommitted: (issued, saved) => { acknowledgedOriginal = issued; acknowledged = saved; }));
        Assert.Same(original, acknowledgedOriginal); Assert.NotNull(acknowledged);
        Assert.Equal(original.Layout.Revision + 1, acknowledged.Revision); Assert.Equal(target, acknowledged.Current.ActivePageId);
        Assert.False(before.SequenceEqual(File.ReadAllBytes(f.HomePath))); Assert.False(await f.Session.IsCurrentAsync(original));
        var renewed = await f.Session.ReadAfterEditAsync(original, acknowledged);
        Assert.NotNull(renewed); Assert.Equal(target, renewed.Layout.Current.ActivePageId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Session.ReadAfterEditAsync(original, acknowledged with { }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Navigation.SelectAsync(original, target, () => true));
    }
    private sealed class ControlledHomeStore(IHomeCoreStateStore actual, string path) : IHomeCoreStateStore
    {
        public bool Hold; public Action? AfterSuccess;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => actual.ReadAsync(ct);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long revision, CancellationToken ct = default)
            => Observe(() => actual.WriteAsync(record, revision, ct), ct);
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long revision, AuthenticatedResourceActor actor,
            IHomeStateCommitActorGuard guard, CancellationToken ct = default)
            => Observe(() => actual.WriteGuardedAsync(record, revision, actor, guard, ct), ct);
        private async Task<HomeStateWriteResult> Observe(Func<Task<HomeStateWriteResult>> write, CancellationToken ct)
        {
            Task<HomeStateWriteResult> pending;
            if (Hold)
            {
                using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                pending = write(); Assert.False(pending.IsCompleted); Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct); lease.Dispose();
            }
            else pending = write();
            var result = await pending; if (result.IsSuccess) AfterSuccess?.Invoke(); return result;
        }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public bool Suspend; public int Calls;
        public TaskCompletionSource<bool> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        {
            Calls++; var observed = Current;
            if (Suspend) { Suspend = false; Entered.TrySetResult(true); await Release.Task.WaitAsync(ct); }
            return observed;
        }
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor, HomeStateCommitPhase phase, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(Current == actor); }
    }
    private sealed class NoPeer : IHomeNativeInstalledPeerVerifier
    { public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer peer, CancellationToken ct) => ValueTask.FromResult<HomeNativeInstalledPeer?>(null); }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "launcher-page-" + Guid.NewGuid().ToString("N"));
        public string HomePath => Path.Combine(root, "home.json");
        public Actors Actors = new(); private HomeLauncherLayoutStore layouts;
        public ControlledHomeStore Home { get; }
        public HomeLauncherSession Session { get; } public AndroidLauncherPageNavigation Navigation { get; }
        public Fixture()
        {
            Directory.CreateDirectory(root); var home = new ControlledHomeStore(new FileHomeCoreStateStore(HomePath), HomePath); Home = home;
            var resources = new ResourceAuthorizationService(Actors, [new LauncherLayoutResourceResolver(home)]);
            layouts = new(home, Actors, resources, new HomeInstalledApplicationRegistry(home, Actors, []));
            Session = new(layouts, Actors, new HomeNativeWidgetRegistry(new NoPeer(), Actors, resources)); Navigation = new(Session);
        }
        public async Task<LauncherSessionSnapshot> Seed()
        {
            var initial = await layouts.GetAsync();
            var added = await layouts.EditAsync(initial, layout => LauncherLayoutEdits.AddPage(layout, "Work"));
            await layouts.EditAsync(added, layout => LauncherLayoutEdits.SelectPage(layout, layout.Pages.First().Id));
            return (await Session.ReadAsync())!;
        }
        public void Dispose() => Directory.Delete(root, true);
    }
}
