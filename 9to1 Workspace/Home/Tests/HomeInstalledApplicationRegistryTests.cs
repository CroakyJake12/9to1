using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeInstalledApplicationRegistryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "astra-app-registry-" + Guid.NewGuid().ToString("N"), "home.json");
    [Fact]
    public async Task Canonical_ids_survive_reopen_profile_duplicate_labels_and_quiet_profiles_cannot_launch()
    {
        var actors = new Actors(); var provider = new Provider();
        var registry = new HomeInstalledApplicationRegistry(new FileHomeCoreStateStore(_path), actors, [provider]);
        var first = await registry.RefreshAsync(default);
        Assert.Equal(2, first.Count);
        Assert.NotEqual(first[0].ApplicationId, first[1].ApplicationId);
        Assert.All(first, a => Assert.Equal(Haven.Core.AppOperabilityClassification.Unknown, a.Operability.Classification));
        var personal = first.Single(a => a.PlatformProfileId == "personal");
        var work = first.Single(a => a.PlatformProfileId == "work");
        var reopened = new HomeInstalledApplicationRegistry(new FileHomeCoreStateStore(_path), actors, [provider]);
        Assert.Equal(personal, await reopened.ResolveLaunchAsync(personal.ApplicationId, personal.Revision, default));
        provider.Quiet = true;
        var quiet = await reopened.RefreshAsync(default);
        var quietWork = quiet.Single(a => a.ApplicationId == work.ApplicationId);
        Assert.False(quietWork.ProfileAccessible);
        Assert.Null(await reopened.ResolveLaunchAsync(work.ApplicationId, quietWork.Revision, default));
        provider.Quiet = false;
        var returned = await reopened.RefreshAsync(default);
        Assert.Equal(work.ApplicationId, returned.Single(a => a.PlatformProfileId == "work").ApplicationId);
        Assert.Null(await reopened.ResolveLaunchAsync(work.ApplicationId, work.Revision, default));
        provider.Removed = true;
        Assert.Null(await reopened.ResolveLaunchAsync(personal.ApplicationId, personal.Revision, default));
        Assert.False((await reopened.RefreshAsync(default)).Single(a => a.ApplicationId == personal.ApplicationId).Enabled);
    }
    [Fact]
    public async Task Unknown_authority_duplicate_provider_and_actor_switch_fail_closed()
    {
        var actor = new Actors(); var provider = new Provider();
        var store = new FileHomeCoreStateStore(_path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new HomeInstalledApplicationRegistry(store, actor, [provider, provider]).RefreshAsync(default).AsTask());
        provider.Switch = () => actor.Current = actor.Current with { AuthenticationRevision = "new-session" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new HomeInstalledApplicationRegistry(store, actor, [provider]).RefreshAsync(default).AsTask());
        Assert.Empty((await store.ReadAsync()).State!.Records);
    }
    [Fact]
    public async Task Native_invocation_source_omits_unknown_games_and_revoked_platform_entries()
    {
        var provider = new Provider();
        var registry = new HomeInstalledApplicationRegistry(new FileHomeCoreStateStore(_path), new Actors(), [provider]);
        var source = new HomeInstalledAppInvocationSource(registry);
        Assert.Empty(await source.SearchAsync("", default));
        provider.Operability = new(Haven.Core.AppOperabilityClassification.OrdinaryApplication, Haven.Core.AppOperabilityPath.ComputerUseRequired);
        var ordinary = await source.SearchAsync("", default);
        Assert.Equal(2, ordinary.Count);
        Assert.All(ordinary, resource => Assert.Equal(NineToOne.Cui.AI.AppInteractionPath.ComputerUseRequired, resource.InteractionPath));
        provider.Operability = new(Haven.Core.AppOperabilityClassification.Game, Haven.Core.AppOperabilityPath.ComputerUseRequired);
        Assert.Empty(await source.SearchAsync("", default));
        provider.Operability = new(Haven.Core.AppOperabilityClassification.AntiCheatProtected, Haven.Core.AppOperabilityPath.ComputerUseRequired);
        Assert.Empty(await source.SearchAsync("", default));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actor_change_while_actual_store_lease_is_held_does_not_publish_registry(bool existing)
    {
        var actors = new Actors(); var provider = new Provider();
        var store = new PausedStore(_path);
        var registry = new HomeInstalledApplicationRegistry(store, actors, [provider]);
        if (existing) await registry.RefreshAsync(default);
        var before = File.Exists(_path) ? await File.ReadAllBytesAsync(_path) : null;
        store.Pause = true; provider.Removed = true;
        var write = registry.RefreshAsync(default).AsTask();
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        actors.Current = actors.Current with { AuthenticationRevision = "revoked-session" };
        store.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => write);
        if (before is null) Assert.False(File.Exists(_path));
        else Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sole_entry_update_retains_canonical_id_and_invalidates_old_launch_revision(bool legacy)
    {
        var provider = new MutableProvider { Apps = [Launch(".Old", legacy ? null : "package:example")] };
        var store = new FileHomeCoreStateStore(_path);
        var registry = new HomeInstalledApplicationRegistry(store, new Actors(), [provider]);
        var first = Assert.Single(await registry.RefreshAsync(default));
        if (legacy)
        {
            var record = Assert.Single((await store.ReadAsync()).State!.Records);
            Assert.True((await store.WriteAsync(record with { SchemaVersion = 1, Revision = record.Revision + 1 }, record.Revision)).IsSuccess);
        }
        provider.Apps = [Launch(".New", "package:example")];
        var reopened = new HomeInstalledApplicationRegistry(new FileHomeCoreStateStore(_path), new Actors(), [provider]);
        var updated = Assert.Single(await reopened.RefreshAsync(default));
        Assert.Equal(first.ApplicationId, updated.ApplicationId);
        Assert.Equal(".New", updated.Entrypoint);
        Assert.True(updated.Revision > first.Revision);
        Assert.Null(await reopened.ResolveLaunchAsync(first.ApplicationId, first.Revision, default));
        Assert.NotNull(await reopened.ResolveLaunchAsync(updated.ApplicationId, updated.Revision, default));
        Assert.Equal(2, Assert.Single((await store.ReadAsync()).State!.Records).SchemaVersion);
    }

    [Fact]
    public async Task Historical_multiple_entries_are_not_merged_or_selected_by_package()
    {
        var provider = new MutableProvider { Apps = [Launch(".A", null), Launch(".B", null)] };
        var registry = new HomeInstalledApplicationRegistry(new FileHomeCoreStateStore(_path), new Actors(), [provider]);
        var first = await registry.RefreshAsync(default);
        provider.Apps = [];
        await registry.RefreshAsync(default);
        provider.Apps = [Launch(".C", "package:example")];
        var updated = await registry.RefreshAsync(default);
        Assert.Equal(3, updated.Count);
        var active = Assert.Single(updated, a => a.Enabled);
        Assert.DoesNotContain(first, a => a.ApplicationId == active.ApplicationId);
        provider.Apps = [Launch(".A", "component:A"), Launch(".C", "package:example")];
        var restored = await registry.RefreshAsync(default);
        Assert.Equal(first.Single(a => a.Entrypoint == ".A").ApplicationId, restored.Single(a => a.Entrypoint == ".A").ApplicationId);
    }

    [Fact]
    public async Task Duplicate_or_conflicting_stable_keys_preserve_durable_registry()
    {
        var provider = new MutableProvider { Apps = [Launch(".A", "package:example")] };
        var registry = new HomeInstalledApplicationRegistry(new FileHomeCoreStateStore(_path), new Actors(), [provider]);
        await registry.RefreshAsync(default);
        var before = await File.ReadAllBytesAsync(_path);
        provider.Apps = [Launch(".A", "same"), Launch(".B", "same")];
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.RefreshAsync(default).AsTask());
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
        provider.Apps = [Launch(".B", "package:example"), Launch(".A", "component:A")];
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.RefreshAsync(default).AsTask());
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    private static InstalledApplicationObservation Launch(string entrypoint, string? stable) =>
        new("example", entrypoint, "Example", "2", true) { StableLaunchIdentity = stable };
    private sealed class MutableProvider : IInstalledApplicationObservationProvider
    {
        public string ProviderId => "android.launcherapps";
        public IReadOnlyList<InstalledApplicationObservation> Apps = [];
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>([new("personal", "Personal", false, true, Apps)]);
    }

    private sealed class PausedStore(string path) : IHomeCoreStateStore
    {
        private readonly FileHomeCoreStateStore _inner = new(path);
        public bool Pause;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => _inner.ReadAsync(ct);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long revision, CancellationToken ct = default) =>
            _inner.WriteAsync(record, revision, ct);
        public async Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long revision,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken ct = default)
        {
            if (!Pause) return await _inner.WriteGuardedAsync(record, revision, actor, guard, ct);
            using var heldLease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var pending = _inner.WriteGuardedAsync(record, revision, actor, guard, ct);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            heldLease.Dispose();
            return await pending;
        }
    }

    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
            HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class Provider : IInstalledApplicationObservationProvider
    {
        public string ProviderId => "android.launcherapps";
        public bool Quiet; public bool Removed; public Action? Switch; public Haven.Core.AppOperability? Operability;
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct)
        {
            Switch?.Invoke();
            InstalledApplicationObservation app = new("android:example", "example/.Main", "Same label", null, true, Operability);
            return ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>([
                new("personal", "Personal", false, true, Removed ? [] : [app]),
                new("work", "Work", true, !Quiet, Quiet ? [] : [app])]);
        }
    }
    public void Dispose() { var root = Path.GetDirectoryName(_path)!; if (Directory.Exists(root)) Directory.Delete(root, true); }
}
