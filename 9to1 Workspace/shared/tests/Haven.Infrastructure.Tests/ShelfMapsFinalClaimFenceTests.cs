using System.Text.Json;
using Haven.Application;
using Haven.Application.Shelf;
using Haven.Core;
using Haven.Core.Shelf;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class ShelfMapsFinalClaimFenceTests
{
    [Theory]
    [InlineData(false, "blocked")] [InlineData(true, "blocked")]
    [InlineData(false, "terminal")] [InlineData(true, "terminal")]
    [InlineData(false, "binding")] [InlineData(true, "binding")]
    public async Task Actual_physical_settings_held_before_final_Home_acquisition_denies_late_action_or_original_receipt_change(bool maps, string change)
    {
        using var f = new Fixture(maps); await f.InitializeAsync();
        var review = await f.ReviewAsync(); await f.ApproveAsync(review);
        var before = await File.ReadAllBytesAsync(f.SettingsFile);
        f.Settings.HoldAdmission = true;
        var commit = f.CommitAsync(review);
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(commit.IsCompleted); Assert.Equal(HomePermissionRequestState.Executing,
                (await f.Permissions.ReadRequestObservationAsync(f.RequestID(review)))!.State);
            if (change == "blocked") Assert.True((await f.Permissions.BlockCallerAsync(f.Actor.ActorId)).Succeeded);
            else if (change == "terminal") Assert.True((await f.Permissions.RecordExecutionAsync(f.RequestID(review),
                new(HomePermissionRequestState.Cancelled, "ExternalTerminal", "Actual permission terminal transition before final owner acquisition.", []))).Succeeded);
            else
            {
                var state = (await f.Home.ReadAsync()).State!;
                var record = Assert.Single(state.Records, item => item.RecordType == "home.local-store-ownership");
                var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
                Assert.True((await f.Home.WriteAsync(record with { Revision = record.Revision + 1,
                    Payload = JsonSerializer.SerializeToElement(binding with { ObservedStoreRevision = "changed-original-receipt" }) }, record.Revision)).IsSuccess);
            }
        }
        finally { f.Settings.Release.TrySetResult(); }
        var result = await commit.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.Committed); Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
        Assert.Equal(1, f.Settings.GuardedWriteCalls);
        await f.FinishAsync(review); Assert.Equal(1, f.Settings.GuardedWriteCalls);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Genuine_Settings_first_Home_lease_holds_late_block_until_physical_owner_commit_then_preserves_known_receipt(bool maps)
    {
        using var f = new Fixture(maps); await f.InitializeAsync(); var review = await f.ReviewAsync(); await f.ApproveAsync(review);
        f.Settings.HoldPublication = true;
        var commit = f.CommitAsync(review); Task<HomePermissionOperationResult>? blocked = null;
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            blocked = f.Permissions.BlockCallerAsync(f.Actor.ActorId);
            Assert.False(blocked.IsCompleted); Assert.False(commit.IsCompleted);
        }
        finally { f.Settings.Release.TrySetResult(); }
        var result = await commit.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Committed); Assert.True((await blocked!.WaitAsync(TimeSpan.FromSeconds(10))).Succeeded);
        await f.AssertOneCommittedAsync(); var bytes = await File.ReadAllBytesAsync(f.SettingsFile);
        var finish = await f.FinishAsync(review); Assert.True(finish.Committed);
        Assert.Equal(1, f.Settings.GuardedWriteCalls); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.SettingsFile));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_commit_return_loss_and_hidden_receipt_stays_unknown_until_original_evidence_without_Home_reentry_deadlock(bool maps)
    {
        using var f = new Fixture(maps); await f.InitializeAsync(); var review = await f.ReviewAsync(); await f.ApproveAsync(review);
        f.Settings.LoseWriteReturnOnce = true; f.Settings.HideAfterLostReturn = true;
        var lost = await f.CommitAsync(review).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(lost.CompletionUnknown); Assert.False(lost.Committed); Assert.False(lost.AuditRecorded);
        var bytes = await File.ReadAllBytesAsync(f.SettingsFile); var home = await File.ReadAllBytesAsync(f.HomeFile);
        var hidden = await f.FinishAsync(review); Assert.True(hidden.CompletionUnknown);
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile));
        f.Settings.HideLibraryReads = false;
        var recovered = await f.FinishAsync(review).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(recovered.Committed); Assert.True(recovered.AuditRecorded);
        Assert.Equal(1, f.Settings.GuardedWriteCalls); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.SettingsFile));
        await f.AssertOneCommittedAsync(); var terminal = await File.ReadAllBytesAsync(f.HomeFile);
        Assert.True((await f.FinishAsync(review)).Committed);
        Assert.Equal(terminal, await File.ReadAllBytesAsync(f.HomeFile)); Assert.Equal(1, f.Settings.GuardedWriteCalls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_actual_final_fence_composition_denies_without_owner_write_or_replay(bool maps)
    {
        using var f = new Fixture(maps, includeFence: false); await f.InitializeAsync(); var review = await f.ReviewAsync(); await f.ApproveAsync(review);
        var before = await File.ReadAllBytesAsync(f.SettingsFile);
        var home = await File.ReadAllBytesAsync(f.HomeFile);
        Assert.False((await f.CommitAsync(review)).Committed);
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile));
        Assert.Equal(HomePermissionRequestState.Approved, (await f.Permissions.ReadRequestObservationAsync(f.RequestID(review)))!.State);
        Assert.Equal(0, f.Settings.GuardedWriteCalls); Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
        Assert.False((await f.FinishAsync(review)).Committed); Assert.Equal(0, f.Settings.GuardedWriteCalls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_owner_library_revision_change_before_settings_transaction_preserves_competing_root_without_commit(bool maps)
    {
        using var f = new Fixture(maps); await f.InitializeAsync(); var review = await f.ReviewAsync(); await f.ApproveAsync(review);
        f.Settings.HoldBeforeWrite = true;
        var commit = f.CommitAsync(review); byte[] competing;
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await f.CompeteAsync(); competing = await File.ReadAllBytesAsync(f.SettingsFile);
        }
        finally { f.Settings.Release.TrySetResult(); }
        Assert.False((await commit.WaitAsync(TimeSpan.FromSeconds(10))).Committed);
        Assert.Equal(competing, await File.ReadAllBytesAsync(f.SettingsFile));
        Assert.Equal(1, f.Settings.GuardedWriteCalls);
        Assert.False((await f.FinishAsync(review)).Committed);
        Assert.Equal(competing, await File.ReadAllBytesAsync(f.SettingsFile));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_original_capability_competing_completion_waits_for_physical_owner_publication_and_cannot_erase_receipt(bool maps)
    {
        using var f = new Fixture(maps); await f.InitializeAsync();
        var review = await f.ReviewAsync(); await f.ApproveAsync(review);
        f.Settings.HoldPublication = true;
        var commit = f.CommitAsync(review);
        Task<HomePermissionOperationResult>? completing = null;
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Adversarial holder of the genuine original opaque handle; no minted or donor authority.
            // Test-only reflection leaves the production review API private.
            var property = review.GetType().GetProperty("Capability");
            Assert.NotNull(property);
            var actual = Assert.IsType<HomeResourceExecutionCapability>(property!.GetValue(review));
            completing = f.Broker.CompleteExecutionAsync(actual,
                new(HomePermissionRequestState.Cancelled, "CompetingOriginalCompletion", "Original handle completion races actual publication.", []));
            Assert.False(completing.IsCompleted); Assert.False(commit.IsCompleted);
        }
        finally { f.Settings.Release.TrySetResult(); }
        Assert.True((await commit.WaitAsync(TimeSpan.FromSeconds(10))).Committed);
        await completing!.WaitAsync(TimeSpan.FromSeconds(10));
        await f.AssertOneCommittedAsync();
        var bytes = await File.ReadAllBytesAsync(f.SettingsFile);
        Assert.True((await f.FinishAsync(review)).Committed);
        Assert.Equal(1, f.Settings.GuardedWriteCalls);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.SettingsFile));
    }

    private sealed class Fixture(bool maps, bool includeFence = true) : IDisposable
    {
        public Paths Paths { get; } = new();
        public string SettingsFile => Path.Combine(Paths.DataDirectory, "settings.json");
        public string HomeFile => Path.Combine(Paths.DataDirectory, "home.json");
        public HeldSettings Settings { get; private set; } = null!;
        public FileHomeCoreStateStore Home { get; private set; } = null!;
        public HomePermissionTrustService Permissions { get; private set; } = null!;
        public AuthenticatedResourceActor Actor { get; private set; } = null!;
        public HomeResourceOperationBroker Broker { get; private set; } = null!;
        private HomeMapsLibraryOwner? _maps; private HomeShelfLibraryOwner? _shelf;
        private MapsJourneyService _journeys = null!; private ShelfLibraryService _library = null!;
        public async Task InitializeAsync()
        {
            Settings = new(new VersionedAtomicSettingsStore(Paths)); Home = new(HomeFile);
            var profiles = new HomeLocalProfileIdentity(Home, new OperatingSystemPrincipalSource());
            Actor = (await profiles.GetCurrentAsync(default))!;
            var mapPolicy = new MapsOwnedLibraryActionPolicies(); var shelfPolicy = new ShelfOwnedLibraryActionPolicies();
            Permissions = new(Home, (app, action) => mapPolicy.TryGet(app, action) ?? shelfPolicy.TryGet(app, action));
            _journeys = new(Settings); _library = new(Settings);
            var evidence = maps ? (IHomeLocalStoreEvidenceProvider)new MapsOwnedLibraryEvidence(Settings) : new ShelfOwnedLibraryEvidence(Settings);
            var ownership = new HomeLocalStoreOwnership(Home, profiles, new HomeLocalStoreEvidenceRegistry([evidence]), Permissions);
            var authority = new HomeResourceStoreOwnershipAuthority(ownership, profiles);
            ICanonicalResourceAccessResolver resolver = maps ? new MapsOwnedLibraryAccessResolver(_journeys, profiles, authority)
                : new ShelfOwnedLibraryAccessResolver(_library, profiles, authority);
            var broker = Broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(profiles, [resolver]), Permissions);
            var source = includeFence ? new HomeOwnedLibraryCommitFenceSource(Home, profiles, authority, broker) : null;
            _maps = new(_journeys, profiles, authority, broker, Permissions, source);
            _shelf = new(_library, profiles, authority, broker, Permissions, source);
            await ownership.BindNewEmptyAsync(maps ? "maps" : "shelf", (await Settings.GetStoreIdentityAsync(default)).StoreId.ToString("D"));
        }
        public async Task<object> ReviewAsync()
        {
            if (maps)
            {
                var display = await _maps!.LoadForDisplayAsync(Actor);
                return await _maps.ReviewAsync(display.Selection, new(Guid.NewGuid(), "Original journey",
                    [new(Guid.NewGuid(), MapJourneyStepKind.ManualInstruction, "Original required instruction")],
                    MapObjectVisibility.Private, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0));
            }
            var shelf = await _shelf!.LoadForDisplayAsync(Actor);
            return await _shelf.ReviewAsync(shelf.Selection, new(Guid.NewGuid(), "Original app", new(ShelfTargetKind.InstalledApplication, "original-app")));
        }
        public string RequestID(object review) => maps ? ((IMapsLibraryReview)review).RequestID : ((IShelfLibraryReview)review).RequestID;
        public async Task ApproveAsync(object review) => Assert.True((await Permissions.DecideAsync(RequestID(review), HomeApprovalChoice.Accept)).Succeeded);
        public async Task<(bool Committed, bool AuditRecorded, bool CompletionUnknown)> CommitAsync(object review)
        {
            if (maps) { var r = await _maps!.CommitAsync((IMapsLibraryReview)review); return (r.Committed, r.AuditRecorded, r.CompletionUnknown); }
            var s = await _shelf!.CommitAsync((IShelfLibraryReview)review); return (s.Committed, s.AuditRecorded, s.CompletionUnknown);
        }
        public async Task<(bool Committed, bool AuditRecorded, bool CompletionUnknown)> FinishAsync(object review)
        {
            if (maps) { var r = await _maps!.FinishAsync((IMapsLibraryReview)review); return (r.Committed, r.AuditRecorded, r.CompletionUnknown); }
            var s = await _shelf!.FinishAsync((IShelfLibraryReview)review); return (s.Committed, s.AuditRecorded, s.CompletionUnknown);
        }
        public async Task CompeteAsync()
        {
            if (maps)
            {
                var other = new MapSavedJourney(Guid.NewGuid(), "Competing journey", [new(Guid.NewGuid(), MapJourneyStepKind.ManualInstruction, "Other")],
                    MapObjectVisibility.Private, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0);
                Assert.True((await new MapsJourneyService(Settings).SaveJourneyAsync(0, other)).Success);
            }
            else Assert.True((await new ShelfLibraryService(Settings).AddItemAsync(0, new(Guid.NewGuid(), "Competing app", new(ShelfTargetKind.InstalledApplication, "other-app")))).Success);
        }
        public async Task AssertOneCommittedAsync()
        {
            if (maps) Assert.NotNull(Assert.Single((await _journeys.ReadAsync()).Journeys));
            else Assert.NotNull(Assert.Single((await _library.ReadAsync()).Library.Items));
        }
        public void Dispose() { try { Directory.Delete(Paths.DataDirectory, true); } catch (IOException) { } }
    }
    private sealed class HeldSettings(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore,
        IResourceStoreIdentitySource, IVersionedSettingsGuardedCompareExchange
    {
        public bool LoseWriteReturnOnce { get; set; }
        public bool HideAfterLostReturn { get; set; }
        public bool HideLibraryReads { get; set; }
        public int GuardedWriteCalls { get; private set; }
        public bool HoldIdentity { get; set; }
        public bool HoldPublication { get; set; }
        public bool HoldAdmission { get; set; }
        public bool HoldBeforeWrite { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token)
        {
            var identity = await actual.GetStoreIdentityAsync(token);
            if (HoldIdentity) { HoldIdentity = false; Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return identity;
        }
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class =>
            HideLibraryReads && (key == "shelf.library.v1" || key == "maps.journeys.v1")
                ? Task.FromException<T?>(new IOException("Actual library observation temporarily unavailable after lost return."))
                : actual.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => actual.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => actual.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => actual.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => actual.ImportAsync(value, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken token)
            => actual.CompareExchangeAsync(key, expected, replacement, token);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, CancellationToken token)
            => actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, token);
        public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token)
        {
            GuardedWriteCalls++;
            if (HoldBeforeWrite) { HoldBeforeWrite = false; Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            var result = await actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, new HeldAdmission(this, admission), token);
            if (result.Exchanged && LoseWriteReturnOnce)
            {
                LoseWriteReturnOnce = false; HideLibraryReads = HideAfterLostReturn;
                throw new IOException("Decorator loses return AFTER actual inner atomic physical publication.");
            }
            return result;
        }
        private sealed class HeldAdmission(HeldSettings owner, ISettingsCommitAdmission actual) : ISettingsCommitAdmission
        {
            public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token)
            {
                if (owner.HoldAdmission && context.Phase == SettingsCommitPhase.Admission)
                { owner.HoldAdmission = false; owner.Entered.TrySetResult(); await owner.Release.Task.WaitAsync(token); }
                if (owner.HoldPublication && context.Phase == SettingsCommitPhase.Publication)
                { owner.HoldPublication = false; owner.Entered.TrySetResult(); await owner.Release.Task.WaitAsync(token); }
                return await actual.CheckAsync(context, token);
            }
        }
    }
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("astra-owned-shelf-maps-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "actual.sqlite");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
