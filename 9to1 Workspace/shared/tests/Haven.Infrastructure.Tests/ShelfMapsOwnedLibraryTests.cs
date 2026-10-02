using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Application.Shelf;
using Haven.Core;
using Haven.Core.Shelf;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;
namespace Haven.Infrastructure.Tests;

public sealed class ShelfMapsOwnedLibraryTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_SQLite_Home_settings_owner_requires_individual_approval_and_retains_exact_known_commit_on_retry(bool maps)
    {
        using var fixture = new Fixture(maps); await fixture.InitializeAsync();
        Assert.NotEqual(fixture.SettingsID, (await new SqliteDatabase(fixture.Paths).GetStoreIdentityAsync(default)).StoreId);
        var display = await fixture.LoadAsync(); var review = await fixture.ReviewAsync(display);
        var before = await File.ReadAllBytesAsync(fixture.SettingsFile);
        var denied = await fixture.CommitAsync(review); Assert.False(denied.Committed); Assert.Equal("ApprovalRequired", denied.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile));
        Assert.True((await fixture.Permissions.DecideAsync(fixture.RequestID(review), HomeApprovalChoice.Accept)).Succeeded);
        var committed = await fixture.CommitAsync(review); Assert.True(committed.Committed); Assert.True(committed.AuditRecorded);
        var persisted = await File.ReadAllBytesAsync(fixture.SettingsFile); Assert.False(before.SequenceEqual(persisted));
        var retry = await fixture.CommitAsync(review); Assert.True(retry.Committed);
        Assert.Equal(persisted, await File.ReadAllBytesAsync(fixture.SettingsFile));
        if (maps) Assert.Equal(1, Assert.Single((await new MapsJourneyService(new VersionedAtomicSettingsStore(fixture.Paths)).ReadAsync()).Journeys).Revision);
        else Assert.Single((await new ShelfLibraryService(new VersionedAtomicSettingsStore(fixture.Paths)).ReadAsync()).Library.Items);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Suspended_actual_identity_read_rejects_actor_change_before_first_library_display_without_mutation_or_pending(bool maps)
    {
        using var fixture = new Fixture(maps); await fixture.InitializeAsync();
        var settingsBytes = await File.ReadAllBytesAsync(fixture.SettingsFile); var homeBytes = await File.ReadAllBytesAsync(fixture.HomeFile);
        fixture.Settings.HoldIdentity = true;
        var pending = fixture.LoadAsync(); await fixture.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Actor.Override = fixture.Actor.Original with { AuthenticationRevision = "changed-while-actual-identity-read" };
        fixture.Settings.Release.TrySetResult(); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(fixture.SettingsFile));
        Assert.Equal(homeBytes, await File.ReadAllBytesAsync(fixture.HomeFile)); await fixture.AssertNoPendingAsync();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_UUID_replacement_before_review_cannot_be_adopted_by_old_display_and_supplied_bytes_are_preserved(bool maps)
    {
        using var fixture = new Fixture(maps); await fixture.InitializeAsync(); var display = await fixture.LoadAsync();
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(fixture.SettingsFile))!.AsObject();
        envelope[nameof(SettingsExportManifest.StoreIdentity)]![nameof(SettingsStoreIdentity.StoreId)] = Guid.NewGuid();
        var supplied = JsonSerializer.SerializeToUtf8Bytes(envelope); await File.WriteAllBytesAsync(fixture.SettingsFile, supplied);
        var homeBytes = await File.ReadAllBytesAsync(fixture.HomeFile);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ReviewAsync(display)); // Actual first root-refresh refusal.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.ReviewAsync(display)); // Then actual B identity still refuses old private token.
        Assert.Equal(supplied, await File.ReadAllBytesAsync(fixture.SettingsFile));
        Assert.Equal(homeBytes, await File.ReadAllBytesAsync(fixture.HomeFile)); await fixture.AssertNoPendingAsync();
    }
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task Actual_settings_final_publication_lease_rejects_revoked_actor_or_disposed_display(bool maps, bool dispose)
    {
        using var fixture = new Fixture(maps); await fixture.InitializeAsync(); var display = await fixture.LoadAsync();
        var review = await fixture.ReviewAsync(display);
        Assert.True((await fixture.Permissions.DecideAsync(fixture.RequestID(review), HomeApprovalChoice.Accept)).Succeeded);
        var before = await File.ReadAllBytesAsync(fixture.SettingsFile); fixture.Settings.HoldPublication = true;
        var pending = fixture.CommitAsync(review); await fixture.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (dispose) ((IDisposable)display).Dispose();
        else fixture.Actor.Override = fixture.Actor.Original with { AuthenticationRevision = "revoked-at-actual-publication" };
        fixture.Settings.Release.TrySetResult(); var result = await pending;
        Assert.False(result.Committed); Assert.True(result.AuditRecorded);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile)); await fixture.AssertNoPendingAsync();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Owning_proposal_count_lie_stops_at_sentinel_before_actual_identity_or_Home_work(bool maps)
    {
        using var fixture = new Fixture(maps); await fixture.InitializeAsync(); var display = await fixture.LoadAsync();
        var beforeSettings = await File.ReadAllBytesAsync(fixture.SettingsFile); var beforeHome = await File.ReadAllBytesAsync(fixture.HomeFile);
        var tags = new LyingList<string>("original tag");
        var steps = new LyingList<MapJourneyStep>(new(Guid.NewGuid(), MapJourneyStepKind.ManualInstruction, "Original instruction"));
        fixture.Settings.HoldIdentity = true;
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.ReviewCustomAsync(display, tags, steps));
        Assert.Equal(2, maps ? steps.Consumed : tags.Consumed); Assert.False(fixture.Settings.Entered.Task.IsCompleted);
        Assert.Equal(beforeSettings, await File.ReadAllBytesAsync(fixture.SettingsFile));
        Assert.Equal(beforeHome, await File.ReadAllBytesAsync(fixture.HomeFile)); await fixture.AssertNoPendingAsync();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_owning_review_detaches_caller_lists_before_suspended_identity_then_commits_only_reviewed_values(bool maps)
    {
        using var fixture = new Fixture(maps); await fixture.InitializeAsync(); var display = await fixture.LoadAsync();
        var tags = new[] { "original tag" };
        var steps = new[] { new MapJourneyStep(Guid.NewGuid(), MapJourneyStepKind.ManualInstruction, "Original instruction") };
        fixture.Settings.HoldIdentity = true;
        var pending = fixture.ReviewCustomAsync(display, tags, steps); await fixture.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        tags[0] = "Caller changed tag"; steps[0] = steps[0] with { Instruction = "Caller changed instruction" };
        fixture.Settings.Release.TrySetResult(); var review = await pending;
        Assert.True((await fixture.Permissions.DecideAsync(fixture.RequestID(review), HomeApprovalChoice.Accept)).Succeeded);
        Assert.True((await fixture.CommitAsync(review)).Committed);
        if (maps) Assert.Equal("Original instruction", Assert.Single(Assert.Single((await new MapsJourneyService(new VersionedAtomicSettingsStore(fixture.Paths)).ReadAsync()).Journeys).Steps).Instruction);
        else Assert.Equal("original tag", Assert.Single(Assert.Single((await new ShelfLibraryService(new VersionedAtomicSettingsStore(fixture.Paths)).ReadAsync()).Library.Items).Tags!));
    }
    private sealed class LyingList<T>(T value) : IReadOnlyList<T>
    {
        public int Count => 1;
        public int Consumed { get; private set; }
        public T this[int index] => throw new InvalidOperationException("No index allocation from reported Count.");
        public IEnumerator<T> GetEnumerator() { for (var index = 0; index < 1000000; index++) { Consumed++; yield return value; } }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_canonical_proposal_validation_denies_invalid_target_or_missing_place_before_Home_pending(bool maps)
    {
        using var fixture = new Fixture(maps); await fixture.InitializeAsync(); var display = await fixture.LoadAsync();
        var settingsBytes = await File.ReadAllBytesAsync(fixture.SettingsFile); var homeBytes = await File.ReadAllBytesAsync(fixture.HomeFile);
        if (maps) await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.ReviewInvalidCanonicalAsync(display));
        else await Assert.ThrowsAsync<ArgumentException>(() => fixture.ReviewInvalidCanonicalAsync(display));
        Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(fixture.SettingsFile));
        Assert.Equal(homeBytes, await File.ReadAllBytesAsync(fixture.HomeFile)); await fixture.AssertNoPendingAsync();
    }
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task Actual_inner_publication_lost_return_uses_exact_receipt_or_retains_unknown_without_replay(bool maps, bool hideObservation)
    {
        using var fixture = new Fixture(maps); await fixture.InitializeAsync(); var display = await fixture.LoadAsync(); var review = await fixture.ReviewAsync(display);
        Assert.True((await fixture.Permissions.DecideAsync(fixture.RequestID(review), HomeApprovalChoice.Accept)).Succeeded);
        fixture.Settings.LoseWriteReturnOnce = true; fixture.Settings.HideAfterLostReturn = hideObservation;
        var first = await fixture.CommitAsync(review); var committedBytes = await File.ReadAllBytesAsync(fixture.SettingsFile);
        Assert.Equal(1, fixture.Settings.GuardedWriteCalls);
        if (hideObservation)
        {
            Assert.False(first.Committed); Assert.True(first.CompletionUnknown); Assert.False(first.AuditRecorded);
            var pending = await fixture.CommitAsync(review); Assert.True(pending.CompletionUnknown);
            Assert.Equal(1, fixture.Settings.GuardedWriteCalls); Assert.Equal(committedBytes, await File.ReadAllBytesAsync(fixture.SettingsFile));
            fixture.Settings.HideLibraryReads = false;
        }
        else { Assert.True(first.Committed); Assert.False(first.CompletionUnknown); }
        var recovered = await fixture.CommitAsync(review); Assert.True(recovered.Committed); Assert.True(recovered.AuditRecorded);
        Assert.False(recovered.CompletionUnknown); Assert.Equal(1, fixture.Settings.GuardedWriteCalls);
        Assert.Equal(committedBytes, await File.ReadAllBytesAsync(fixture.SettingsFile));
        Assert.Equal(HomePermissionRequestState.Succeeded, (await fixture.Permissions.GetAuthorizationAsync(fixture.RequestID(review))).State);
    }
    [Theory]
    [InlineData(false, 0)] [InlineData(true, 0)] [InlineData(false, 1)] [InlineData(true, 1)] [InlineData(false, 2)] [InlineData(true, 2)]
    public async Task Actual_rejected_begin_or_claim_retains_exact_audit_attempt_without_Begin_Claim_or_owner_write_replay(bool maps, int failure)
    {
        using var fixture = new Fixture(maps); await fixture.InitializeAsync(); var display = await fixture.LoadAsync(); var review = await fixture.ReviewAsync(display);
        Assert.True((await fixture.Permissions.DecideAsync(fixture.RequestID(review), HomeApprovalChoice.Accept)).Succeeded);
        var before = await File.ReadAllBytesAsync(fixture.SettingsFile);
        if (failure == 0) fixture.Home.LoseExecutingWriteReturn = true;
        if (failure == 1) fixture.Home.AfterExecutingWrite = () => fixture.Actor.Override = fixture.Actor.Original with { AuthenticationRevision = "revoked-before-private-claim" };
        if (failure == 2) fixture.Home.AfterExecutingWrite = () => fixture.Actor.ThrowOnce = true;
        var first = await fixture.CommitAsync(review); Assert.False(first.Committed);
        Assert.Equal(1, fixture.Home.ExecutingWrites); Assert.Equal(0, fixture.Settings.GuardedWriteCalls);
        var retry = await fixture.CommitAsync(review); Assert.False(retry.Committed);
        Assert.Equal(1, fixture.Home.ExecutingWrites); Assert.Equal(0, fixture.Settings.GuardedWriteCalls);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile)); await fixture.AssertNoPendingAsync();
        Assert.Equal(HomePermissionRequestState.Failed, (await fixture.Permissions.GetAuthorizationAsync(fixture.RequestID(review))).State);
    }
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task Actual_pending_publication_origin_loss_returns_exact_unavailable_review_for_explicit_Home_closure(bool maps, bool dispose)
    {
        using var fixture = new Fixture(maps); await fixture.InitializeAsync(); var display = await fixture.LoadAsync();
        var settingsBytes = await File.ReadAllBytesAsync(fixture.SettingsFile);
        fixture.Home.AfterPendingWrite = () =>
        {
            if (dispose) ((IDisposable)display).Dispose();
            else fixture.Actor.Override = fixture.Actor.Original with { AuthenticationRevision = "changed-after-durable-pending" };
        };
        var review = await fixture.ReviewAsync(display);
        Assert.False(maps ? ((IMapsLibraryReview)review).OriginAvailable : ((IShelfLibraryReview)review).OriginAvailable);
        var requestID = fixture.RequestID(review);
        Assert.Equal(HomePermissionRequestState.PendingApproval, (await fixture.Permissions.GetAuthorizationAsync(requestID)).State);
        Assert.Equal(1, fixture.Home.PendingWrites); Assert.Equal(0, fixture.Home.ExecutingWrites);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.CommitAsync(review));
        Assert.Equal(0, fixture.Settings.GuardedWriteCalls); Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(fixture.SettingsFile));
        Assert.True((await fixture.Permissions.DecideAsync(requestID, HomeApprovalChoice.Decline)).Succeeded);
        Assert.Equal(HomePermissionRequestState.Denied, (await fixture.Permissions.GetAuthorizationAsync(requestID)).State);
        await fixture.AssertNoPendingAsync();
        Assert.Equal(1, fixture.Home.PendingWrites); Assert.Equal(0, fixture.Home.ExecutingWrites);
        Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(fixture.SettingsFile));
    }
    private sealed class Fixture : IDisposable
    {
        public Paths Paths { get; } = new();
        public string SettingsFile => Path.Combine(Paths.DataDirectory, "settings.json");
        public string HomeFile => Path.Combine(Paths.DataDirectory, "home.json");
        public HeldSettings Settings { get; }
        public ObservedActualHome Home { get; }
        public HomeLocalProfileIdentity Profiles { get; }
        public ActualActor Actor { get; private set; } = null!;
        public HomePermissionTrustService Permissions { get; }
        public Guid SettingsID { get; private set; }
        private readonly bool _maps;
        private HomeShelfLibraryOwner? _shelf;
        private HomeMapsLibraryOwner? _journeys;
        public Fixture(bool maps)
        {
            _maps = maps; Settings = new(new VersionedAtomicSettingsStore(Paths)); Home = new(new FileHomeCoreStateStore(HomeFile));
            Profiles = new(Home, new OperatingSystemPrincipalSource());
            var shelfPolicy = new ShelfOwnedLibraryActionPolicies(); var mapsPolicy = new MapsOwnedLibraryActionPolicies();
            Permissions = new(Home, (app, action) => shelfPolicy.TryGet(app, action) ?? mapsPolicy.TryGet(app, action));
        }
        public async Task InitializeAsync()
        {
            Actor = new((await Profiles.GetCurrentAsync(default)) ?? throw new InvalidOperationException("Actual OS/Home actor required."), Profiles);
            SettingsID = (await Settings.GetStoreIdentityAsync(default)).StoreId;
            var evidence = _maps ? (IHomeLocalStoreEvidenceProvider)new MapsOwnedLibraryEvidence(Settings) : new ShelfOwnedLibraryEvidence(Settings);
            var ownership = new HomeLocalStoreOwnership(Home, Profiles, new HomeLocalStoreEvidenceRegistry([evidence]), Permissions);
            var authority = new HomeResourceStoreOwnershipAuthority(ownership, Actor);
            var shelf = new ShelfLibraryService(Settings); var maps = new MapsJourneyService(Settings);
            ICanonicalResourceAccessResolver resolver = _maps ? new MapsOwnedLibraryAccessResolver(maps, Actor, authority)
                : new ShelfOwnedLibraryAccessResolver(shelf, Actor, authority);
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(Actor, [resolver]), Permissions);
            _shelf = new(shelf, Actor, authority, broker, Permissions); _journeys = new(maps, Actor, authority, broker, Permissions);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => LoadAsync()); // No implicit ownership.
            await ownership.BindNewEmptyAsync(_maps ? "maps" : "shelf", SettingsID.ToString("D"));
        }
        public async Task<object> LoadAsync() => _maps
            ? (object)(await _journeys!.LoadForDisplayAsync(Actor.Original)).Selection
            : (await _shelf!.LoadForDisplayAsync(Actor.Original)).Selection;
        public async Task<object> ReviewAsync(object display)
        {
            if (_maps)
            {
                var journey = new MapSavedJourney(Guid.NewGuid(), "Owned ordinary walk",
                    [new(Guid.NewGuid(), MapJourneyStepKind.ManualInstruction, "Walk to the library")],
                    MapObjectVisibility.Private, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0);
                return await _journeys!.ReviewAsync((IMapsLibraryDisplay)display, journey);
            }
            return await _shelf!.ReviewAsync((IShelfLibraryDisplay)display,
                new ShelfLaunchItem(Guid.NewGuid(), "Owned app", new(ShelfTargetKind.InstalledApplication, "actual-retained-app-id")));
        }
        public async Task<object> ReviewCustomAsync(object display, IReadOnlyList<string> tags, IReadOnlyList<MapJourneyStep> steps)
        {
            if (_maps) return await _journeys!.ReviewAsync((IMapsLibraryDisplay)display,
                new MapSavedJourney(Guid.NewGuid(), "Owned reviewed walk", steps, MapObjectVisibility.Private, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0));
            return await _shelf!.ReviewAsync((IShelfLibraryDisplay)display,
                new ShelfLaunchItem(Guid.NewGuid(), "Owned app", new(ShelfTargetKind.InstalledApplication, "actual-retained-app-id"), Tags: tags));
        }
        public async Task<object> ReviewInvalidCanonicalAsync(object display)
        {
            if (_maps) return await _journeys!.ReviewAsync((IMapsLibraryDisplay)display,
                new MapSavedJourney(Guid.NewGuid(), "Missing canonical place", [new(Guid.NewGuid(), MapJourneyStepKind.Travel,
                    "Travel to actual place", PlaceId: Guid.NewGuid())], MapObjectVisibility.Private, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0));
            return await _shelf!.ReviewAsync((IShelfLibraryDisplay)display,
                new ShelfLaunchItem(Guid.NewGuid(), "Invalid file", new(ShelfTargetKind.File, "missing-file-location", IsDirectory: false)));
        }
        public string RequestID(object review) => _maps ? ((IMapsLibraryReview)review).RequestID : ((IShelfLibraryReview)review).RequestID;
        public async Task<(bool Committed, string Code, bool AuditRecorded, bool CompletionUnknown)> CommitAsync(object review)
        {
            if (_maps) { var result = await _journeys!.CommitAsync((IMapsLibraryReview)review); return (result.Committed, result.Code, result.AuditRecorded, result.CompletionUnknown); }
            var saved = await _shelf!.CommitAsync((IShelfLibraryReview)review); return (saved.Committed, saved.Code, saved.AuditRecorded, saved.CompletionUnknown);
        }
        public async Task AssertNoPendingAsync()
        {
            var read = await Home.ReadAsync(); Assert.True(read.IsSuccess);
            var record = read.State!.Records.SingleOrDefault(item => item.RecordId == "home.permissions-trust");
            if (record is null) return;
            var requests = record.Payload.GetProperty("Requests").Deserialize<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest[]>()!;
            Assert.DoesNotContain(requests, request => request.State == HomePermissionRequestState.PendingApproval);
        }
        public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(Paths.DataDirectory, true); }
    }
    private sealed class ObservedActualHome(FileHomeCoreStateStore actual) : IHomeCoreStateStore
    {
        public Action? AfterPendingWrite { get; set; }
        public int PendingWrites { get; private set; }
        public bool LoseExecutingWriteReturn { get; set; }
        public Action? AfterExecutingWrite { get; set; }
        public int ExecutingWrites { get; private set; }
        public Task<HomeStateReadResult> ReadAsync(CancellationToken token = default) => actual.ReadAsync(token);
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRevision, CancellationToken token = default)
        {
            var result = await actual.WriteAsync(record, expectedRevision, token);
            if (result.IsSuccess && record.RecordId == "home.permissions-trust"
                && record.Payload.GetProperty("Requests").Deserialize<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest[]>()!
                    .Any(request => request.State == HomePermissionRequestState.PendingApproval))
            {
                PendingWrites++;
                var afterPending = AfterPendingWrite; AfterPendingWrite = null; afterPending?.Invoke();
            }
            if (result.IsSuccess && record.RecordId == "home.permissions-trust"
                && record.Payload.GetProperty("Requests").Deserialize<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest[]>()!
                    .Any(request => request.State == HomePermissionRequestState.Executing))
            {
                ExecutingWrites++;
                var after = AfterExecutingWrite; AfterExecutingWrite = null; after?.Invoke();
                if (LoseExecutingWriteReturn) { LoseExecutingWriteReturn = false; throw new IOException("Lost return AFTER actual Home Begin publication."); }
            }
            return result;
        }
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expectedRevision,
            AuthenticatedResourceActor expectedActor, IHomeStateCommitActorGuard guard, CancellationToken token = default)
            => actual.WriteGuardedAsync(record, expectedRevision, expectedActor, guard, token);
    }
    private sealed class ActualActor(AuthenticatedResourceActor original, IAuthenticatedResourceActorSource actual) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Original { get; } = original;
        public AuthenticatedResourceActor? Override { get; set; }
        public bool ThrowOnce { get; set; }
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        {
            if (ThrowOnce) { ThrowOnce = false; throw new IOException("Held actual claim actor observation unavailable."); }
            return Override ?? await actual.GetCurrentAsync(token);
        }
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
