using Haven.Application;
using Haven.Application.Go;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

public sealed class ShellConfigurationTests
{
    [Fact]
    public async Task ActorWithoutCommitGuardCannotInitializeShellState()
    {
        using var f = new Fixture(); var actor = new UnguardedActors(f.Actors);
        var owner = new HomeShellConfigurationStore(f.Home, actor, new ResourceAuthorizationService(actor, [new ShellConfigurationResourceResolver(f.Home)]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ReadAsync(default));
        Assert.DoesNotContain((await f.Home.ReadAsync()).State!.Records, r => r.RecordType == HomeShellConfigurationStore.RecordType);
    }
    private sealed class UnguardedActors(IAuthenticatedResourceActorSource source) : IAuthenticatedResourceActorSource
    { public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => source.GetCurrentAsync(ct); }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ActorChangeWhileActualHomeWriteLeaseIsHeldCannotPersistShellState(bool initialize, bool changeProfile)
    {
        using var f = new Fixture();
        var before = initialize ? null : await f.Store.ReadAsync(default);
        var recordId = HomeShellConfigurationStore.RecordId(f.Actors.Current.ProfileId);
        var beforeRecord = (await f.Home.ReadAsync()).State!.Records.SingleOrDefault(r => r.RecordId == recordId);
        var blocked = new LeaseBlockingHomeStore(f.Home, f.StatePath);
        var owner = new HomeShellConfigurationStore(blocked, f.Actors,
            new ResourceAuthorizationService(f.Actors, [new ShellConfigurationResourceResolver(f.Home)]));
        Task pending = initialize ? owner.ReadAsync(default) : owner.TryWriteAsync(before!.Revision,
            before with { Revision = before.Revision + 1, Current = ShellEdits.AddLayer(before.Current, "Must not persist"), Previous = before.Current }, default);
        await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Actors.Current = changeProfile ? f.Actors.Current with { ProfileId = "other-profile" }
            : f.Actors.Current with { AuthenticationRevision = "revoked-session" };
        blocked.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        var afterRecord = (await f.Home.ReadAsync()).State!.Records.SingleOrDefault(r => r.RecordId == recordId);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(beforeRecord), System.Text.Json.JsonSerializer.Serialize(afterRecord));
    }

    [Fact]
    public async Task PreviewDoesNotPersistAndExpiredPreviewCannotBeKept()
    {
        using var fixture = new Fixture(); var clock = new Clock(); var service = new ShellConfigurationService(fixture.Store, clock);
        var before = await service.GetAsync(); var candidate = ShellEdits.AddLayer(before.Effective, "Development");
        var preview = await service.PreviewAsync(before.Stored, candidate, TimeSpan.FromSeconds(30));
        Assert.Equal(2, preview.Effective.ActiveSpace.Taskbar.Layers.Count);
        Assert.Single((await new ShellConfigurationService(fixture.Store).GetAsync()).Effective.ActiveSpace.Taskbar.Layers);
        clock.Utc += TimeSpan.FromSeconds(31);
        Assert.Null((await service.GetAsync()).Preview);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.KeepAsync(preview.Preview!.Id));
        Assert.Single((await fixture.Store.ReadAsync(default)).Current.ActiveSpace.Taskbar.Layers);
    }
    [Fact]
    public async Task KeepReopenRevertAndPreviousRecoveryRetainStableIdentity()
    {
        using var fixture = new Fixture(); var service = new ShellConfigurationService(fixture.Store);
        var original = await service.GetAsync(); var candidate = ShellEdits.AddLayer(original.Effective, "Work");
        var preview = await service.PreviewAsync(original.Stored, candidate, TimeSpan.FromSeconds(30));
        var saved = await service.KeepAsync(preview.Preview!.Id);
        Assert.Equal(original.Stored.Revision + 1, saved.Stored.Revision);
        var reopened = await new ShellConfigurationService(fixture.Store).GetAsync();
        Assert.Equal(candidate.ActiveSpace.Taskbar.ActiveLayerId, reopened.Effective.ActiveSpace.Taskbar.ActiveLayerId);
        Assert.Equal(original.Effective.ActiveSpace.Taskbar.ActiveLayerId, reopened.Stored.Previous!.ActiveSpace.Taskbar.ActiveLayerId);
        var recovery = await service.PreviewAsync(saved.Stored, saved.Stored.Previous!, TimeSpan.FromSeconds(30));
        await service.RevertAsync(recovery.Preview!.Id);
        Assert.Equal(candidate.ActiveSpace.Taskbar.ActiveLayerId, (await service.GetAsync()).Effective.ActiveSpace.Taskbar.ActiveLayerId);
        recovery = await service.PreviewAsync(saved.Stored, saved.Stored.Previous!, TimeSpan.FromSeconds(30));
        var restored = await service.KeepAsync(recovery.Preview!.Id);
        Assert.Single(restored.Effective.ActiveSpace.Taskbar.Layers);
    }
    [Fact]
    public async Task ConcurrentKeepPreservesWinningRevisionAndRejectsStalePreview()
    {
        using var fixture = new Fixture(); var first = new ShellConfigurationService(fixture.Store); var second = new ShellConfigurationService(fixture.Store);
        var baseline = await first.GetAsync();
        await second.GetAsync();
        var a = await first.PreviewAsync(baseline.Stored, ShellEdits.RenameSpace(baseline.Effective, "First"), TimeSpan.FromSeconds(30));
        var b = await second.PreviewAsync(baseline.Stored, ShellEdits.RenameSpace(baseline.Effective, "Second"), TimeSpan.FromSeconds(30));
        await first.KeepAsync(a.Preview!.Id);
        await Assert.ThrowsAnyAsync<IOException>(() => second.KeepAsync(b.Preview!.Id));
        Assert.Equal("First", (await fixture.Store.ReadAsync(default)).Current.ActiveSpace.Name);
    }
    [Fact]
    public async Task SwitchedProfileCannotKeepPreviousProfilesPreview()
    {
        using var fixture = new Fixture(); var service = new ShellConfigurationService(fixture.Store);
        var before = await service.GetAsync();
        var preview = await service.PreviewAsync(before.Stored, ShellEdits.RenameSpace(before.Effective, "Private"), TimeSpan.FromSeconds(30));
        var bytes = await File.ReadAllBytesAsync(fixture.StatePath);
        fixture.Actors.Current = fixture.Actors.Current with { ProfileId = "other-profile" };
        // An old action must not initialize another profile while checking its stale preview.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.KeepAsync(preview.Preview!.Id));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.StatePath));
    }
    [Fact]
    public async Task SameProfileSessionChangeInvalidatesPreviewAndCannotRebindAnOldRead()
    {
        using var fixture = new Fixture(); var service = new ShellConfigurationService(fixture.Store);
        var original = await service.GetAsync();
        var pending = await service.PreviewAsync(original.Stored, ShellEdits.AddLayer(original.Effective, "Old session"), TimeSpan.FromSeconds(30));
        var bytes = await File.ReadAllBytesAsync(fixture.StatePath);
        fixture.Actors.Current = fixture.Actors.Current with { AuthenticationRevision = "new-session" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.KeepAsync(pending.Preview!.Id));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.StatePath));
        var fresh = await service.GetAsync(); Assert.Null(fresh.Preview);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PreviewAsync(original.Stored,
            ShellEdits.RenameSpace(original.Effective, "Old read after refresh"), TimeSpan.FromSeconds(30)));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.StatePath));
        var next = await service.PreviewAsync(fresh.Stored, ShellEdits.AddLayer(fresh.Effective, "Current session"), TimeSpan.FromSeconds(30));
        Assert.Equal(fresh.Stored.Revision + 1, (await service.KeepAsync(next.Preview!.Id)).Stored.Revision);
    }
    [Fact]
    public async Task ReconstructedShellRecordCannotClaimReadSessionAndStaleRecordCannotWrite()
    {
        using var fixture = new Fixture(); var before = await fixture.Store.ReadAsync(default);
        var bytes = await File.ReadAllBytesAsync(fixture.StatePath);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Store.TryWriteAsync(before.Revision,
            new(before.Revision + 1, ShellEdits.AddLayer(before.Current, "Copied data"), before.Current, before.AuthorityId), default));
        fixture.Actors.Current = fixture.Actors.Current with { AuthenticationRevision = "new-session" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Store.TryWriteAsync(before.Revision,
            before with { Revision = before.Revision + 1, Current = ShellEdits.AddLayer(before.Current, "Stale binding"), Previous = before.Current }, default));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.StatePath));
    }

    [Fact]
    public void LayersAreDiscreteBoundedAndDuplicationRetainsOwnerReferences()
    {
        var config = ShellConfiguration.Default(); var original = config.ActiveSpace.Taskbar.ActiveLayerId;
        for (var i = 0; i < 4; i++) config = ShellEdits.AddLayer(config, "Layer " + i);
        Assert.Throws<InvalidOperationException>(() => ShellEdits.AddLayer(config, "Sixth"));
        var atLast = config.ActiveSpace.Taskbar.ActiveLayerId;
        Assert.Equal(atLast, ShellEdits.StepLayer(config, 100).ActiveSpace.Taskbar.ActiveLayerId);
        config = ShellEdits.StepLayer(config, -100); // One gesture moves exactly one adjacent layer.
        Assert.Equal(3, config.ActiveSpace.Taskbar.Layers.ToList().FindIndex(l => l.Id == config.ActiveSpace.Taskbar.ActiveLayerId));
        var duplicate = ShellEdits.DuplicateSpace(config, "Touch"); duplicate.Validate();
        Assert.NotEqual(original, duplicate.ActiveSpace.Taskbar.Layers[0].Id);
        Assert.Equal(2, duplicate.Spaces.Count);
    }
    [Fact]
    public async Task UnsupportedSchemaIsPreservedWithoutAutomaticDefaults()
    {
        using var fixture = new Fixture(); await fixture.Store.ReadAsync(default);
        var before = await fixture.Home.ReadAsync(); var record = before.State!.Records.Single();
        var modified = record with { SchemaVersion = 99, Revision = record.Revision + 1 };
        Assert.True((await fixture.Home.WriteAsync(modified, record.Revision)).IsSuccess);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.ReadAsync(default));
        Assert.Equal(99, (await fixture.Home.ReadAsync()).State!.Records.Single().SchemaVersion);
    }
    [Fact]
    public async Task ReturnedSnapshotsCannotMutatePendingTransaction()
    {
        using var fixture = new Fixture(); var service = new ShellConfigurationService(fixture.Store);
        var before = await service.GetAsync();
        var preview = await service.PreviewAsync(before.Stored, ShellEdits.AddLayer(before.Effective, "Kept"), TimeSpan.FromSeconds(30));
        var exposed = (DesktopSpace[])preview.Preview!.Candidate.Spaces;
        exposed[0] = exposed[0] with { Name = "Tampered" };
        Assert.Equal("Standard", (await service.KeepAsync(preview.Preview.Id)).Effective.ActiveSpace.Name);
    }
    [Fact]
    public void PortableSpaceImportRemapsInternalIdsButPreservesCanonicalReferences()
    {
        var id = Guid.NewGuid(); var original = ShellEdits.PinApplication(ShellConfiguration.Default(), id, "Editor");
        var imported = DesktopSpaceExchange.Import(original, DesktopSpaceExchange.Export(original.ActiveSpace));
        Assert.NotEqual(original.ActiveSpaceId, imported.ActiveSpaceId);
        var item = imported.ActiveSpace.Taskbar.Layers.Single().Items.Single(i => i.Kind == TaskbarItemKind.Application);
        Assert.Equal(id.ToString("D"), item.Target!.Id);
        Assert.NotEqual(original.ActiveSpace.Taskbar.Layers.Single().Items.Single(i => i.Kind == TaskbarItemKind.Application).Id, item.Id);
        Assert.Throws<InvalidDataException>(() => DesktopSpaceExchange.Import(original, DesktopSpaceExchange.Export(original.ActiveSpace).Replace("\"SchemaVersion\": 2", "\"SchemaVersion\": 99")));
        imported.Validate();
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Utc = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Utc; }
    [Fact]
    public async Task DisplayedGoQueryRetainsOriginalActualHomeSessionAndRejectsCopiedOwner()
    {
        using var f = new Fixture(); var service = new ShellConfigurationService(f.Store);
        var snapshot = await service.GetAsync(); var owner = new ShellGoSearchOwner(service);
        var displayed = owner.Begin(snapshot.Stored, new("original", "Apps"));
        Assert.True(await owner.IsCurrentAsync(displayed, default));
        Assert.False(await new ShellGoSearchOwner(service).IsCurrentAsync(displayed, default));
        f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "changed" };
        Assert.False(await owner.IsCurrentAsync(displayed, default));
        Assert.Equal("original", displayed.Query.Text);
    }

    [Fact]
    public async Task SerializedShellFieldsCannotRecreateDisplayedGoActorAdmission()
    {
        using var f = new Fixture(); var service = new ShellConfigurationService(f.Store);
        var stored = (await service.GetAsync()).Stored;
        var unbound = new ShellStoredConfiguration(stored.Revision, stored.Current, stored.Previous, stored.AuthorityId);
        var owner = new ShellGoSearchOwner(service);
        Assert.False(await owner.IsCurrentAsync(owner.Begin(unbound, new("copied fields")), default));
    }

    [Fact]
    public async Task NewGoQueryCannotBeClearedOrUpdatedByPreviousDisplayedQuery()
    {
        using var f = new Fixture(); var service = new ShellConfigurationService(f.Store);
        var snapshot = await service.GetAsync(); var owner = new ShellGoSearchOwner(service);
        var ids = new HashSet<string>(StringComparer.Ordinal) { "original-provider" };
        var older = owner.Begin(snapshot.Stored, new("older", Scope: new(ids)));
        ids.Clear(); ids.Add("substituted-provider");
        Assert.Contains("original-provider", older.Query.Scope!.ProviderIds!);
        Assert.DoesNotContain("substituted-provider", older.Query.Scope.ProviderIds!);
        var newer = owner.Begin(snapshot.Stored, new("newer"));
        Assert.False(await owner.IsCurrentAsync(older, default));
        Assert.True(await owner.IsCurrentAsync(newer, default));
        owner.Invalidate(); Assert.False(await owner.IsCurrentAsync(newer, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuspendedActualHomeActorReadCannotAdmitOldQueryAfterReplacement(bool replaceActor)
    {
        using var f = new Fixture(); var original = await f.Store.ReadAsync(default);
        var paused = new PausedGoActors(f.Actors);
        var store = new HomeShellConfigurationStore(f.Home, paused,
            new ResourceAuthorizationService(paused, [new ShellConfigurationResourceResolver(f.Home)]));
        var owner = new ShellGoSearchOwner(new ShellConfigurationService(store));
        var older = owner.Begin(original, new("original displayed query"));
        var pending = owner.IsCurrentAsync(older, default);
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (replaceActor) f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "other-session" };
        else owner.Begin(original, new("replacement query"));
        paused.Release.TrySetResult();
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }
    private sealed class PausedGoActors(Actors actual) : IAuthenticatedResourceActorSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); return await actual.GetCurrentAsync(ct); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalActorGoNavigationUsesActualOwnerPreviewOrDeniesChangedSession(bool changeSession)
    {
        using var f = new Fixture(); var service = new ShellConfigurationService(f.Store);
        var provider = new ShellNavigationGoProvider(service); var actor = f.Actors.Current;
        var before = await service.GetAsync(); GoResult? selected = null;
        await foreach (var result in provider.QueryAsync(new("", "Desktop Spaces"), default)) { selected = result; break; }
        Assert.NotNull(selected);
        if (changeSession) f.Actors.Current = actor with { AuthenticationRevision = "replacement" };
        if (changeSession)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.InvokeForActorAsync(selected!.Reference, "Navigate", actor, default));
            Assert.Null((await service.GetAsync()).Preview);
        }
        else
        {
            await provider.InvokeForActorAsync(selected!.Reference, "Navigate", actor, default);
            Assert.NotNull((await service.GetAsync()).Preview);
        }
        Assert.Equal(before.Stored.Revision, (await service.GetAsync()).Stored.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalGoLaunchDeniesUnavailableOrChangedActorBeforeRegistryRead(bool unavailableSource)
    {
        using var f = new Fixture(); var original = f.Actors.Current;
        var registry = new CountingGoRegistry();
        var resources = new ResourceAuthorizationService(f.Actors, []);
        var launcher = new LinuxApplicationLauncher(registry, resources, unavailableSource ? null : f.Actors);
        f.Actors.Current = original with { AuthenticationRevision = "replacement" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => launcher.LaunchForActorAsync(Guid.NewGuid(), 1, original, default));
        Assert.Equal(0, registry.Reads);
    }
    private sealed class CountingGoRegistry : IInstalledApplicationRegistry
    {
        public int Reads;
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct)
        { Reads++; return ValueTask.FromResult<IReadOnlyList<InstalledApplicationReference>>([]); }
        public ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct)
        { Reads++; return ValueTask.FromResult<InstalledApplicationReference?>(null); }
    }

    [Fact]
    public async Task OriginalLaunchRejectsActorSwitchAfterActualInstalledRegistryReadBeforeInventory()
    {
        using var f = new Fixture(); var actor = f.Actors.Current;
        var actual = new HomeInstalledApplicationRegistry(f.Home, f.Actors, [new FixtureInstalledObservation()]);
        var app = Assert.Single(await actual.RefreshAsync(default));
        var paused = new PausedInstalledRegistry(actual);
        var resources = new ResourceAuthorizationService(f.Actors, [new InstalledApplicationResourceResolver(paused)]);
        var launcher = new LinuxApplicationLauncher(paused, resources, f.Actors);
        var pending = launcher.LaunchForActorAsync(app.ApplicationId, app.Revision, actor, default);
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Actors.Current = actor with { AuthenticationRevision = "changed-after-owner-read" };
        paused.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Equal(2, paused.Reads);
    }
    private sealed class FixtureInstalledObservation : IInstalledApplicationObservationProvider
    {
        public string ProviderId => "linux.xdg-desktop";
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>([new("fixture-platform", "Fixture platform", false, true,
                [new("desktop:fixture.desktop", "desktop:fixture.desktop", "Fixture app", "fixture-digest", true)])]);
    }
    private sealed class PausedInstalledRegistry(IInstalledApplicationRegistry actual) : IInstalledApplicationRegistry
    {
        public int Reads;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct) => actual.RefreshAsync(ct);
        public async ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct)
        {
            var observed = await actual.ResolveLaunchAsync(id, revision, ct);
            if (Interlocked.Increment(ref Reads) == 2) { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return observed;
        }
    }

    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expectedActor);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "astra-shell-" + Guid.NewGuid());
        public string StatePath => Path.Combine(_directory, "home.json");
        public FileHomeCoreStateStore Home { get; }
        public Actors Actors { get; } = new();
        public HomeShellConfigurationStore Store { get; }
        public Fixture()
        {
            Home = new(Path.Combine(_directory, "home.json"));
            Store = new(Home, Actors, new ResourceAuthorizationService(Actors, [new ShellConfigurationResourceResolver(Home)]));
        }
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
