using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Tests;

public sealed class SpaceRegistryCommitTests
{
    [Fact]
    public async Task Existing_snapshot_does_not_seed_or_reconcile_the_canonical_store()
    {
        var token = TestContext.Current.CancellationToken;
        var root = NewRoot();
        try
        {
            var settings = new VersionedAtomicSettingsStore(new Paths(root));
            await settings.GetStoreIdentityAsync(token);
            var before = await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"), token);
            Assert.Null(await new SpaceRegistry(settings).ReadExistingAsync(SpaceRegistry.ChatSpaceId, token));
            Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"), token));
            Assert.Empty((await settings.ExportAsync(token)).Settings);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Two_actual_store_instances_rebase_distinct_creates_from_the_same_captured_registry()
    {
        var token = TestContext.Current.CancellationToken;
        var root = NewRoot();
        try
        {
            var first = new ObservedStore(new VersionedAtomicSettingsStore(new Paths(root)));
            var second = new ObservedStore(new VersionedAtomicSettingsStore(new Paths(root)));
            var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            async Task Barrier(CancellationToken ct)
            {
                if (Interlocked.Increment(ref count) == 2) both.TrySetResult();
                await both.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            }
            first.AfterFirstSnapshot = second.AfterFirstSnapshot = Barrier;
            var created = await Task.WhenAll(new SpaceRegistry(first).CreateAsync("First writer", cancellationToken: token),
                new SpaceRegistry(second).CreateAsync("Second writer", cancellationToken: token));
            var actual = await new SpaceRegistry(new VersionedAtomicSettingsStore(new Paths(root))).GetAllAsync(cancellationToken: token);
            Assert.Contains(actual, space => space.Id == created[0].Id && space.Name == "First writer");
            Assert.Contains(actual, space => space.Id == created[1].Id && space.Name == "Second writer");
            Assert.Equal(2, actual.Count(space => !space.IsBuiltIn));
            Assert.Equal(3, first.CommitAttempts + second.CommitAttempts); // One exact-key CAS conflict is retried.
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Selection_cannot_commit_after_its_Space_is_archived_between_validation_and_publication()
    {
        var token = TestContext.Current.CancellationToken;
        var root = NewRoot();
        try
        {
            var settings = new VersionedAtomicSettingsStore(new Paths(root));
            var original = new SpaceRegistry(settings);
            var target = await original.CreateAsync("Pending selection", cancellationToken: token);
            await original.SetCurrentSpaceIdAsync(SpaceRegistry.StudySpaceId, token);
            var observed = new ObservedStore(settings)
            {
                BeforeCommit = async ct =>
                {
                    await new SpaceRegistry(new VersionedAtomicSettingsStore(new Paths(root))).SetArchivedAsync(target.Id, true, ct);
                }
            };
            var selecting = new SpaceRegistry(observed);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => selecting.SetCurrentSpaceIdAsync(target.Id, token));
            Assert.Equal(SpaceRegistry.StudySpaceId, await original.GetCurrentSpaceIdAsync(token));
            Assert.True((await original.ReadExistingAsync(target.Id, token))!.IsArchived);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Revoked_Home_actor_while_waiting_for_actual_settings_lease_cannot_publish_Space_change()
    {
        var token = TestContext.Current.CancellationToken;
        var root = NewRoot();
        var blocker = new HeldLeaseAdmission();
        Task<SettingsGuardedCompareExchangeResult>? held = null;
        Task<SpaceDefinition>? edit = null;
        try
        {
            var paths = new Paths(Path.Combine(root, "settings"));
            var settings = new VersionedAtomicSettingsStore(paths);
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var principal = new RevocablePrincipal();
            var actors = new HomeLocalProfileIdentity(store, principal);
            var ownership = new HomeLocalStoreOwnership(store, actors,
                new HomeLocalStoreEvidenceRegistry([new SpacesLocalStoreEvidenceProvider(settings, settings)]),
                new HomePermissionTrustService(store, (_, _) => null));
            var identity = await settings.GetStoreIdentityAsync(token);
            Assert.NotNull(await ownership.BindNewEmptyAsync("spaces", identity.StoreId.ToString("D"), token));
            var authority = new SpaceLocalStoreAuthority(settings, actors, new HomeResourceStoreOwnershipAuthority(ownership, actors), () => true);
            var observed = new ObservedStore(settings);
            var registry = new SpaceRegistry(observed, authority.CaptureWriteAdmissionAsync);
            var created = await registry.CreateAsync("Original", cancellationToken: token);
            var before = await File.ReadAllBytesAsync(Path.Combine(paths.DataDirectory, "settings.json"), token);
            var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            observed.BeforeCommit = async ct =>
            {
                var other = new VersionedAtomicSettingsStore(paths);
                held = Task.Run(() => other.CompareExchangeGuardedAsync("fixture.hold", null, "true",
                    new Dictionary<string, string?>(), blocker, ct), ct);
                await blocker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
                attempted.TrySetResult();
            };
            edit = registry.RenameAsync(created.Id, "Unapproved replacement", token);
            await attempted.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            Assert.False(edit.IsCompleted);
            principal.Revoked = true;
            blocker.Release.TrySetResult();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => edit);
            Assert.True((await held!).AdmissionRejected);
            Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(paths.DataDirectory, "settings.json"), token));
            Assert.Equal("Original", (await new SpaceRegistry(settings).ReadExistingAsync(created.Id, token))!.Name);
        }
        finally
        {
            blocker.Release.TrySetResult();
            if (held is not null) { try { await held; } catch { } }
            if (edit is not null) { try { await edit; } catch { } }
            Directory.Delete(root, true);
        }
    }

    private sealed class RevocablePrincipal : ITrustedHostPrincipalSource
    {
        public bool Revoked;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => Revoked
            ? ValueTask.FromResult<string?>(null) : new OperatingSystemPrincipalSource().GetPrincipalAsync(token);
    }
    private sealed class HeldLeaseAdmission : ISettingsCommitAdmission
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), token); return false; }
    }
    private sealed class ObservedStore(VersionedAtomicSettingsStore inner) : IVersionedSettingsStore, IVersionedSettingsGuardedCompareExchange
    {
        private int _snapshots;
        public int CommitAttempts;
        public Func<CancellationToken, Task>? AfterFirstSnapshot { get; set; }
        public Func<CancellationToken, Task>? BeforeCommit { get; set; }
        public Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class => inner.GetAsync<T>(key, ct);
        public Task SetAsync<T>(string key, T value, CancellationToken ct) where T : class => inner.SetAsync(key, value, ct);
        public Task RemoveAsync(string key, CancellationToken ct) => inner.RemoveAsync(key, ct);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken ct) => inner.ImportAsync(manifest, ct);
        public async Task<SettingsExportManifest> ExportAsync(CancellationToken ct)
        {
            var snapshot = await inner.ExportAsync(ct);
            if (Interlocked.Increment(ref _snapshots) == 1 && AfterFirstSnapshot is { } callback) await callback(ct);
            return snapshot;
        }
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken ct) =>
            inner.CompareExchangeAsync(key, expected, replacement, ct);
        public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, CancellationToken ct)
        {
            Interlocked.Increment(ref CommitAttempts);
            if (BeforeCommit is { } callback) await callback(ct);
            return await inner.CompareExchangeGuardedAsync(key, expected, replacement, guards, ct);
        }
        public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken ct)
        {
            Interlocked.Increment(ref CommitAttempts);
            if (BeforeCommit is { } callback) await callback(ct);
            return await inner.CompareExchangeGuardedAsync(key, expected, replacement, guards, admission, ct);
        }
    }
    private static string NewRoot()
    { var path = Path.Combine(Path.GetTempPath(), "astra-spaces-cas-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "store.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
