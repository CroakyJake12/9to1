using Haven.Application;
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
            new(before.Revision + 1, ShellEdits.AddLayer(before.Current, "Must not persist"), before.Current, before.AuthorityId), default);
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
        var preview = await service.PreviewAsync(before.Stored.Revision, candidate, TimeSpan.FromSeconds(30));
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
        var preview = await service.PreviewAsync(original.Stored.Revision, candidate, TimeSpan.FromSeconds(30));
        var saved = await service.KeepAsync(preview.Preview!.Id);
        Assert.Equal(original.Stored.Revision + 1, saved.Stored.Revision);
        var reopened = await new ShellConfigurationService(fixture.Store).GetAsync();
        Assert.Equal(candidate.ActiveSpace.Taskbar.ActiveLayerId, reopened.Effective.ActiveSpace.Taskbar.ActiveLayerId);
        Assert.Equal(original.Effective.ActiveSpace.Taskbar.ActiveLayerId, reopened.Stored.Previous!.ActiveSpace.Taskbar.ActiveLayerId);
        var recovery = await service.PreviewAsync(saved.Stored.Revision, saved.Stored.Previous!, TimeSpan.FromSeconds(30));
        await service.RevertAsync(recovery.Preview!.Id);
        Assert.Equal(candidate.ActiveSpace.Taskbar.ActiveLayerId, (await service.GetAsync()).Effective.ActiveSpace.Taskbar.ActiveLayerId);
        recovery = await service.PreviewAsync(saved.Stored.Revision, saved.Stored.Previous!, TimeSpan.FromSeconds(30));
        var restored = await service.KeepAsync(recovery.Preview!.Id);
        Assert.Single(restored.Effective.ActiveSpace.Taskbar.Layers);
    }
    [Fact]
    public async Task ConcurrentKeepPreservesWinningRevisionAndRejectsStalePreview()
    {
        using var fixture = new Fixture(); var first = new ShellConfigurationService(fixture.Store); var second = new ShellConfigurationService(fixture.Store);
        var baseline = await first.GetAsync();
        await second.GetAsync();
        var a = await first.PreviewAsync(baseline.Stored.Revision, ShellEdits.RenameSpace(baseline.Effective, "First"), TimeSpan.FromSeconds(30));
        var b = await second.PreviewAsync(baseline.Stored.Revision, ShellEdits.RenameSpace(baseline.Effective, "Second"), TimeSpan.FromSeconds(30));
        await first.KeepAsync(a.Preview!.Id);
        await Assert.ThrowsAnyAsync<IOException>(() => second.KeepAsync(b.Preview!.Id));
        Assert.Equal("First", (await fixture.Store.ReadAsync(default)).Current.ActiveSpace.Name);
    }
    [Fact]
    public async Task SwitchedProfileCannotKeepPreviousProfilesPreview()
    {
        using var fixture = new Fixture(); var service = new ShellConfigurationService(fixture.Store);
        var before = await service.GetAsync();
        var preview = await service.PreviewAsync(before.Stored.Revision, ShellEdits.RenameSpace(before.Effective, "Private"), TimeSpan.FromSeconds(30));
        fixture.Actors.Current = fixture.Actors.Current with { ProfileId = "other-profile" };
        // A different profile has a different canonical record, not a claim to the old one.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.KeepAsync(preview.Preview!.Id));
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
        var preview = await service.PreviewAsync(before.Stored.Revision, ShellEdits.AddLayer(before.Effective, "Kept"), TimeSpan.FromSeconds(30));
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
