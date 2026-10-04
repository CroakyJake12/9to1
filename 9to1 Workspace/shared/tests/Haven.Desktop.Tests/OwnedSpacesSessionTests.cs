using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Spaces;

namespace Haven.Desktop.Tests;

public sealed class OwnedSpacesSessionTests
{
    [Fact]
    public Task Original_provider_store_and_authority_pair_without_creating_a_grant() =>
        RevisionBankOriginalFixture.RunAsync(async profile =>
        {
            var original = profile.Owner;
            Assert.Same(profile.Provider, original.Provider);
            Assert.Same(profile.Store, original.Settings);
            Assert.Same(profile.Authority, original.Authority);
            Assert.True(profile.Authority.MatchesOriginalInputs(profile.Store, profile.Actors, profile.Ownership));
            var request = new RevisionBankMutation(profile.Space.Id, profile.Space.Revision, Guid.NewGuid(),
                RevisionBankMutationKind.Add, profile.Reference.ContextId);
            var acknowledged = await original.Registry.MutateRevisionBankAsync(request, TestContext.Current.CancellationToken);
            var fresh = await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, TestContext.Current.CancellationToken);
            Assert.Equal(acknowledged.SpaceRevision, fresh!.SpaceRevision);
            Assert.Equal(profile.Reference.ContextId, Assert.Single(fresh.Data.Members).ResourceId);
        });

    [Fact]
    public Task A_different_store_instance_refuses_before_any_owner_write() =>
        RevisionBankOriginalFixture.RunAsync(async profile =>
        {
            var before = await profile.ReadBytesAsync();
            var foreign = new VersionedAtomicSettingsStore(profile);
            profile.Provider.Services[typeof(IVersionedSettingsStore)] = foreign;
            Assert.Throws<UnauthorizedAccessException>(() =>
            {
                _ = OwnedSpacesSession.AttachOriginal(profile.Provider, profile.Store);
            });
            Assert.Equal(before, await profile.ReadBytesAsync());
            Assert.Throws<UnauthorizedAccessException>(() => profile.Owner.RequireCurrent());
        });

    [Fact]
    public Task A_missing_actor_or_mismatched_authority_refuses_without_a_default_owner() =>
        RevisionBankOriginalFixture.RunAsync(async profile =>
        {
            var before = await profile.ReadBytesAsync();
            profile.Provider.Services.Remove(typeof(IAuthenticatedResourceActorSource));
            Assert.Throws<UnauthorizedAccessException>(() =>
            {
                _ = OwnedSpacesSession.AttachOriginal(profile.Provider, profile.Store);
            });
            profile.Provider.Services[typeof(IAuthenticatedResourceActorSource)] = profile.Actors;
            profile.Provider.Services[typeof(SpaceLocalStoreAuthority)] = new SpaceLocalStoreAuthority(
                profile.Store, new RevisionBankOriginalFixture.ControlledActors(), profile.Ownership, () => true);
            Assert.Throws<UnauthorizedAccessException>(() =>
            {
                _ = OwnedSpacesSession.AttachOriginal(profile.Provider, profile.Store);
            });
            Assert.Equal(before, await profile.ReadBytesAsync());
        });

    [Fact]
    public Task Actual_write_policy_and_current_actor_refuse_without_changing_settings() =>
        RevisionBankOriginalFixture.RunAsync(async profile =>
        {
            var before = await profile.ReadBytesAsync();
            profile.AllowWrites = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => profile.Owner.Registry.MutateRevisionBankAsync(
                new(profile.Space.Id, profile.Space.Revision, Guid.NewGuid(), RevisionBankMutationKind.Add,
                    profile.Reference.ContextId), TestContext.Current.CancellationToken));
            Assert.Equal(before, await profile.ReadBytesAsync());
            profile.AllowWrites = true;
            profile.Actors.Current = profile.Actors.Current! with { ProfileId = "another-fixture-profile" };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => profile.Owner.Registry.MutateRevisionBankAsync(
                new(profile.Space.Id, profile.Space.Revision, Guid.NewGuid(), RevisionBankMutationKind.Add,
                    profile.Reference.ContextId), TestContext.Current.CancellationToken));
            Assert.Equal(before, await profile.ReadBytesAsync());
        });
}

/// <summary>Controlled in-process actors and ownership in an actual temporary settings owner.
/// These fixtures do not represent an installed Home receipt, provider or Windows permission.</summary>
internal sealed class RevisionBankOriginalFixture : IAppPaths
{
    private readonly List<RevisionBankPage> _pages = [];
    private readonly List<NativeSpacesPage> _nativePages = [];
    private readonly List<Window> _windows = [];
    private readonly HashSet<Exception> _observed = new(ReferenceEqualityComparer.Instance);
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "haven-native-bank-fresh-" + Guid.NewGuid().ToString("N"));
    public string DatabasePath => Path.Combine(DataDirectory, "app.db");
    public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
    public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    public string SourceSentinel => Path.Combine(DataDirectory, "source-sentinel.txt");
    public bool AllowWrites { get; set; } = true;
    public HoldingSettingsStore Store { get; private set; } = null!;
    public ControlledActors Actors { get; } = new();
    public ControlledOwnership Ownership { get; private set; } = null!;
    public SpaceLocalStoreAuthority Authority { get; private set; } = null!;
    public ControlledProvider Provider { get; } = new();
    public OwnedSpacesSession Owner { get; private set; } = null!;
    public SpaceDefinition Space { get; private set; } = null!;
    public SpaceContextReference Reference { get; private set; } = null!;

    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(DataDirectory);
        await File.WriteAllTextAsync(SourceSentinel, "source sentinel", TestContext.Current.CancellationToken);
        Store = new(new VersionedAtomicSettingsStore(this));
        var identity = await Store.GetStoreIdentityAsync(TestContext.Current.CancellationToken);
        Ownership = new(identity.StoreId, Actors);
        Authority = new(Store, Actors, Ownership, () => AllowWrites);
        Provider.Services[typeof(IVersionedSettingsStore)] = Store;
        Provider.Services[typeof(IResourceStoreIdentitySource)] = Store;
        Provider.Services[typeof(IAuthenticatedResourceActorSource)] = Actors;
        Provider.Services[typeof(IResourceStoreOwnershipAuthority)] = Ownership;
        Provider.Services[typeof(SpaceLocalStoreAuthority)] = Authority;
        Owner = OwnedSpacesSession.AttachOriginal(Provider, Store);
        Space = await Owner.Registry.CreateAsync("Native Bank", cancellationToken: TestContext.Current.CancellationToken);
        Reference = new(Guid.NewGuid(), SpaceContextReferenceKind.WriteArtifact, "write",
            Guid.NewGuid().ToString("D"), "v1", SpaceContextPermission.Read, SpaceContextIndexState.NotRequired,
            false, DateTimeOffset.UtcNow);
        Space = await Owner.Registry.UpdateAsync(Space with { ContextReferences = new[] { Reference } },
            Space.Revision, TestContext.Current.CancellationToken);
    }

    public RevisionBankPage CreatePage(int capacity = 64, bool show = true)
    {
        var page = new RevisionBankPage(Owner, () => Owner, Space.Id, capacity);
        _pages.Add(page); // Capture the returned original before any native property callback.
        if (show)
        {
            var window = new Window();
            _windows.Add(window);
            window.Width = 960;
            window.Height = 720;
            window.Content = page;
            window.Show();
        }
        return page;
    }

    public NativeSpacesPage CreateNativePage(Func<Guid, Task> deleteSpace)
    {
        var page = new NativeSpacesPage(Owner.Registry, null, null, deleteSpace: deleteSpace);
        _nativePages.Add(page);
        var window = new Window();
        _windows.Add(window);
        window.Width = 960;
        window.Height = 720;
        window.Content = page;
        window.Show();
        return page;
    }

    public void ReplaceWithSameGuardedOwnerSession() => Owner = OwnedSpacesSession.AttachOriginal(Provider, Store);
    public SpaceRegistry Reopen() => new(new VersionedAtomicSettingsStore(this));
    public Task<byte[]> ReadBytesAsync() => File.ReadAllBytesAsync(Path.Combine(DataDirectory, "settings.json"), TestContext.Current.CancellationToken);
    public void ObserveExactExpected(Exception error) => _observed.Add(error);

    public static async Task RunAsync(Func<RevisionBankOriginalFixture, Task> body)
    {
        var profile = new RevisionBankOriginalFixture();
        var failures = new List<Exception>();
        try { await profile.InitializeAsync(); await body(profile); }
        catch (Exception primary) { Add(primary); }
        profile.Store?.ReleaseAllHolds();
        foreach (var page in profile._pages)
        {
            try { await page.CloseAndDrainAsync(); }
            catch (Exception error) { if (!profile._observed.Contains(error)) Add(error); }
        }
        foreach (var page in profile._nativePages)
        {
            try { await page.CloseOriginalBankAndDeleteActionsAsync(); }
            catch (Exception error) { if (!profile._observed.Contains(error)) Add(error); }
            try { page.Dispose(); }
            catch (Exception error) { Add(error); }
        }
        foreach (var window in profile._windows)
        {
            try { window.Close(); }
            catch (Exception error) { Add(error); }
        }
        try { if (Directory.Exists(profile.DataDirectory)) Directory.Delete(profile.DataDirectory, recursive: true); }
        catch (Exception error) { Add(error); }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
        void Add(Exception error)
        {
            if (!failures.Any(previous => ReferenceEquals(previous, error))) failures.Add(error);
        }
    }

    internal sealed class ControlledProvider : IServiceProvider
    {
        public Dictionary<Type, object> Services { get; } = [];
        public object? GetService(Type serviceType) => Services.GetValueOrDefault(serviceType);
    }

    internal sealed class ControlledActors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Current { get; set; } =
            new("fixture-actor", "fixture-profile", null, null, "fixture-revision");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Current);
        }
    }

    internal sealed class ControlledOwnership(Guid storeId, ControlledActors actors) : IResourceStoreOwnershipReceiptAuthority
    {
        private readonly VerifiedResourceStoreOwnership _captured = new("spaces", storeId.ToString("D"),
            "fixture-profile", "fixture-owner-revision") { Receipt = new(1, "controlled-fixture-only") };
        public ValueTask<VerifiedResourceStoreOwnership?> GetVerifiedAsync(string kind, string id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<VerifiedResourceStoreOwnership?>(
                kind == "spaces" && id == storeId.ToString("D") ? _captured : null);
        }
        public ValueTask<bool> IsCurrentAsync(VerifiedResourceStoreOwnership captured,
            AuthenticatedResourceActor expectedActor, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ReferenceEquals(captured, _captured) && expectedActor == actors.Current &&
                expectedActor.ProfileId == _captured.ProfileId);
        }
    }

    internal sealed class Hold
    {
        public TaskCompletionSource<SettingsGuardedCompareExchangeResult> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SettingsGuardedCompareExchangeResult>? OriginalOwnerTask { get; internal set; }
        public SettingsGuardedCompareExchangeResult? OriginalAcknowledgedResult { get; internal set; }
    }

    internal sealed class ReadHold
    {
        public TaskCompletionSource<object?> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? OriginalOwnerTask { get; internal set; }
        public object? OriginalValue { get; internal set; }
    }

    internal sealed class HoldingSettingsStore(VersionedAtomicSettingsStore inner) :
        IVersionedSettingsStore, IResourceStoreIdentitySource, IVersionedSettingsGuardedCompareExchange
    {
        private readonly List<Hold> _holds = [];
        private readonly List<ReadHold> _readHolds = [];
        private Hold? _next;
        private ReadHold? _nextRead;
        private int _registryReadCalls;
        public int RegistryReadCalls => Volatile.Read(ref _registryReadCalls);
        public ReadHold HoldNextOriginalRegistryReadReturn()
        {
            if (_nextRead is not null) throw new InvalidOperationException("An original registry read is already held.");
            var hold = new ReadHold();
            _readHolds.Add(hold);
            _nextRead = hold;
            return hold;
        }
        public Hold HoldNextOriginalReturn()
        {
            if (_next is not null) throw new InvalidOperationException("Release the original hold before admitting another.");
            var hold = new Hold();
            _holds.Add(hold);
            _next = hold;
            return hold;
        }
        public void ReleaseAllHolds()
        {
            foreach (var hold in _holds) hold.Release.TrySetResult();
            foreach (var hold in _readHolds) hold.Release.TrySetResult();
        }
        public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) => inner.GetStoreIdentityAsync(token);
        public async Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        {
            ReadHold? hold = null;
            if (key == "spaces.registry")
            {
                Interlocked.Increment(ref _registryReadCalls);
                hold = Interlocked.Exchange(ref _nextRead, null);
            }
            var original = inner.GetAsync<T>(key, token);
            if (hold is not null) hold.OriginalOwnerTask = original;
            T? actual;
            try { actual = await original.ConfigureAwait(false); }
            catch (Exception error) { hold?.Entered.TrySetException(error); throw; }
            if (hold is not null)
            {
                hold.OriginalValue = actual;
                hold.Entered.TrySetResult(actual);
                await hold.Release.Task.ConfigureAwait(false);
            }
            return actual;
        }
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => inner.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => inner.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => inner.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => inner.ImportAsync(manifest, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken token) =>
            inner.CompareExchangeAsync(key, expected, replacement, token);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
            IReadOnlyDictionary<string, string?> guards, CancellationToken token) =>
            inner.CompareExchangeGuardedAsync(key, expected, replacement, guards, token);
        public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
            IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token)
        {
            var hold = Interlocked.Exchange(ref _next, null);
            var original = inner.CompareExchangeGuardedAsync(key, expected, replacement, guards, admission, token);
            if (hold is not null) hold.OriginalOwnerTask = original;
            var actual = await original.ConfigureAwait(false);
            if (hold is not null)
            {
                hold.OriginalAcknowledgedResult = actual;
                hold.Entered.TrySetResult(actual);
                await hold.Release.Task.ConfigureAwait(false);
            }
            return actual;
        }
    }
}
