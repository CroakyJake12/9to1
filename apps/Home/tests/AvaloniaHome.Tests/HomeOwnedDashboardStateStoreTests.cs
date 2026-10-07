using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Haven.Application;
using HavenOS.Home;
using HavenOS.Home.Core;
using Xunit;

namespace AvaloniaHome.Tests;

public sealed class HomeOwnedDashboardStateStoreTests
{
    [Fact]
    public async Task Saved_dashboard_preferences_reopen_in_same_original_state_and_preserve_revision_history()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "home-core-state.json");
            var firstStore = new FileHomeCoreStateStore(path);
            var firstProfiles = new HomeLocalProfileIdentity(firstStore, new OperatingSystemPrincipalSource());
            var firstActor = await firstProfiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(firstActor);
            var first = new HomeCoreDashboardLayoutStore(new HomeOwnedDashboardStateStore(firstStore, firstProfiles, firstActor));
            var original = new HomeDashboardLayout(1, 1, [], false, false);
            Assert.True(await first.TrySaveAsync(0, original, CancellationToken.None));
            var saved = original with { Revision = 2, AllowAiGeneratedTiles = true, AllowAiReorder = true };
            Assert.True(await first.TrySaveAsync(1, saved, CancellationToken.None));

            // A new process owner obtains a new session revision, while the persisted OS profile stays the same.
            var reopenedStore = new FileHomeCoreStateStore(path);
            var reopenedProfiles = new HomeLocalProfileIdentity(reopenedStore, new OperatingSystemPrincipalSource());
            var reopenedActor = await reopenedProfiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(reopenedActor);
            Assert.Equal(firstActor.ProfileId, reopenedActor.ProfileId);
            Assert.NotEqual(firstActor.AuthenticationRevision, reopenedActor.AuthenticationRevision);
            Assert.Null(reopenedActor.AccountId);
            Assert.Null(reopenedActor.OrganisationId);
            var reopened = new HomeCoreDashboardLayoutStore(new HomeOwnedDashboardStateStore(reopenedStore, reopenedProfiles, reopenedActor));
            var actual = await reopened.LoadAsync(CancellationToken.None);
            Assert.NotNull(actual);
            Assert.Equal(2, actual.Revision);
            Assert.True(actual.AllowAiGeneratedTiles);
            Assert.True(actual.AllowAiReorder);
            var archive = await reopened.GetRevisionAsync(1);
            Assert.NotNull(archive);
            Assert.False(archive.AllowAiGeneratedTiles);
            Assert.False(archive.AllowAiReorder);

            // A stale owner preserves the competing proposal and does not replace the saved winner.
            Assert.False(await first.TrySaveAsync(1, original with { Revision = 3 }, CancellationToken.None));
            Assert.Equal(2, (await reopened.LoadAsync(CancellationToken.None))!.Revision);
            var state = (await reopenedStore.ReadAsync()).State!;
            Assert.Single(state.Records, record => record.RecordId == "home.local-profile");
            Assert.Single(state.Records, record => record.RecordType == "home.dashboard.layout.conflict");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Actor_change_between_host_check_and_actual_guarded_commit_preserves_original_bytes()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "home-core-state.json");
            var actual = new FileHomeCoreStateStore(path);
            var principals = new ChangingPrincipal();
            var profiles = new HomeLocalProfileIdentity(actual, principals);
            var actor = await profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            var bytes = File.ReadAllBytes(path);
            var bridge = new BeforeCommitStore(actual, () => principals.Changed = true);
            var owned = new HomeOwnedDashboardStateStore(bridge, profiles, actor);
            var result = await owned.WriteAsync(LayoutRecord(), 0);
            Assert.False(result.IsSuccess);
            Assert.True(bridge.ActualGuardedWriteEntered);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.DoesNotContain((await actual.ReadAsync()).State!.Records, record => record.RecordId == "home.dashboard.layout");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("home.permission.grant", "home.permission.grant")]
    [InlineData("home.dashboard.layout", "home.dashboard.layout.foreign")]
    public async Task Dashboard_adapter_refuses_foreign_records_before_any_store_write(string type, string id)
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "home-core-state.json");
            var store = new FileHomeCoreStateStore(path);
            var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var actor = await profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            var before = File.ReadAllBytes(path);
            var bridge = new BeforeCommitStore(store, () => throw new InvalidOperationException("A foreign write reached storage."));
            var owned = new HomeOwnedDashboardStateStore(bridge, profiles, actor);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owned.WriteAsync(LayoutRecord() with { RecordType = type, RecordId = id }, 0));
            Assert.False(bridge.ActualGuardedWriteEntered);
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Corrupt_original_state_is_preserved_without_replacing_it_with_default_layout()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "home-core-state.json");
            var store = new FileHomeCoreStateStore(path);
            var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var actor = await profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            var malformed = "{original-corrupt-home-state";
            File.WriteAllText(path, malformed);
            var layouts = new HomeCoreDashboardLayoutStore(new HomeOwnedDashboardStateStore(store, profiles, actor));
            await Assert.ThrowsAsync<InvalidDataException>(() => layouts.LoadAsync(CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(() => layouts.TrySaveAsync(0, new(1, 1, [], false, false), CancellationToken.None));
            Assert.Equal(malformed, File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Original_storage_fault_siblings_survive_the_owning_adapter()
    {
        var directory = NewDirectory();
        try
        {
            var store = new FileHomeCoreStateStore(Path.Combine(directory, "home-core-state.json"));
            var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var actor = await profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            var first = new IOException("original storage fault");
            var second = new InvalidOperationException("independent original fault");
            var failure = new FaultedStore(store, first, second);
            var owned = new HomeOwnedDashboardStateStore(failure, profiles, actor);
            var observed = await Assert.ThrowsAsync<AggregateException>(() => owned.WriteAsync(LayoutRecord(), 0));
            Assert.True(failure.Actual.IsFaulted);
            Assert.Equal(2, observed.InnerExceptions.Count);
            Assert.Same(first, observed.InnerExceptions[0]);
            Assert.Same(second, observed.InnerExceptions[1]);
            Assert.Contains(first, observed.InnerExceptions);
            Assert.Contains(second, observed.InnerExceptions);
            Assert.DoesNotContain((await store.ReadAsync()).State!.Records, record => record.RecordId == "home.dashboard.layout");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "home-owned-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static HomeCoreStateRecord LayoutRecord() => new("home.dashboard.layout", "home.dashboard.layout", 1,
        HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, 0,
        JsonSerializer.SerializeToElement(new HomeDashboardLayout(1, 1, [], false, false)));

    private sealed class ChangingPrincipal : ITrustedHostPrincipalSource
    {
        private readonly OperatingSystemPrincipalSource _actual = new();
        public bool Changed { get; set; }
        public async ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        {
            var principal = await _actual.GetPrincipalAsync(token);
            return Changed ? "retired:" + principal : principal;
        }
    }

    private sealed class BeforeCommitStore(IHomeCoreStateStore actual, Action beforeCommit) : IHomeCoreStateStore
    {
        public bool ActualGuardedWriteEntered { get; private set; }
        public Task<HomeStateReadResult> ReadAsync(CancellationToken token = default) => actual.ReadAsync(token);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long revision, CancellationToken token = default) =>
            throw new InvalidOperationException("The Home adapter used an unguarded write.");
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long revision,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken token = default)
        {
            ActualGuardedWriteEntered = true;
            beforeCommit();
            return actual.WriteGuardedAsync(record, revision, actor, guard, token);
        }
    }

    private sealed class FaultedStore : IHomeCoreStateStore
    {
        private readonly IHomeCoreStateStore _actual;
        public Task<HomeStateWriteResult> Actual { get; }
        public FaultedStore(IHomeCoreStateStore actual, params Exception[] failures)
        {
            _actual = actual;
            var source = new TaskCompletionSource<HomeStateWriteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetException(failures);
            Actual = source.Task;
        }
        public Task<HomeStateReadResult> ReadAsync(CancellationToken token = default) => _actual.ReadAsync(token);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long revision, CancellationToken token = default) =>
            throw new InvalidOperationException("The Home adapter used an unguarded write.");
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long revision,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken token = default) => Actual;
    }
}
