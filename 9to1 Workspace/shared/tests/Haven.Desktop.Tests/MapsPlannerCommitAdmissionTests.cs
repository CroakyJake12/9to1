using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;

namespace Haven.Desktop.Tests;

public sealed class MapsPlannerCommitAdmissionTests
{
    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    public async Task Actual_Maps_settings_lease_rechecks_both_owners_before_relationship_or_navigation_publication(bool starting, int change)
    {
        using var paths = new Paths();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var token = timeout.Token;
        var database = new SqliteDatabase(paths); await database.InitializeAsync(token);
        var planner = new PlannerRepository(database); await planner.EnsureDefaultsAsync(token);
        var now = DateTimeOffset.UtcNow;
        var proposed = new PlannerEvent(Guid.NewGuid(), PlannerDefaults.LocalCalendarId, "Appointment", "", "",
            now.AddHours(1), now.AddHours(2), false, null, null, false, null, null, now, now);
        await planner.UpsertEventAsync(proposed, token);
        var appointment = (await planner.GetEventAsync(proposed.Id, token))!;
        var home = new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "home.json"));
        var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        var actors = new Actors((await profiles.GetCurrentAsync(token))!);
        var actual = new VersionedAtomicSettingsStore(paths);
        using var settings = new LeaseBarrierStore(actual, Path.Combine(paths.DataDirectory, "settings.json.lock"));
        var mapsEvidence = new MapsLocalStoreEvidenceProvider(settings, settings);
        var plannerEvidence = new PlannerLocalStoreEvidenceProvider(database, database);
        var permissions = new HomePermissionTrustService(home, (app, action) =>
            app == "9to1.home.local-profile" && action == "home.profile.importStore"
                ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null);
        var ownership = new HomeLocalStoreOwnership(home, profiles,
            new HomeLocalStoreEvidenceRegistry([mapsEvidence, plannerEvidence]), permissions);
        var receipts = new HomeResourceStoreOwnershipAuthority(ownership, actors);
        var mapsIdentity = await settings.GetStoreIdentityAsync(token);
        await ownership.BindNewEmptyAsync("maps", mapsIdentity.StoreId.ToString("D"), token);
        var setup = new HomeLocalStoreSetupSession("planner", database, actors, ownership, plannerEvidence);
        await setup.InspectAsync(token);
        var import = await setup.RequestImportAsync(token);
        Assert.True((await permissions.DecideAsync(import.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
        await setup.CompleteImportAsync(token);
        var maps = new MapsJourneyService(settings);
        var journey = new MapSavedJourney(Guid.NewGuid(), "Saved itinerary",
            [new(Guid.NewGuid(), MapJourneyStepKind.ManualInstruction, "Use the side entrance")],
            MapObjectVisibility.Private, now, now, 0);
        Assert.True((await maps.SaveJourneyAsync(0, journey, token: token)).Success);
        var resources = new ResourceAuthorizationService(actors,
            [new PlannerEventResourceResolver(planner, new ProfilePlannerCalendarResourceBinding(actors.Current.ProfileId)),
             new MapsJourneyResourceResolver(maps, maps, receipts)]);
        var facade = new MapsPlannerJourneyService(maps,
            new PlannerEventJourneyAccessService(planner, new ResourceAuthorisedPlannerEventJourneyPolicy(resources)), resources,
            new MapsJourneyCommitAuthority(maps, actors, receipts), new PlannerJourneyCommitAuthority(planner, actors, receipts));
        if (starting)
            Assert.True((await facade.AttachAsync(1, appointment.Id, appointment.UpdatedAt, journey.JourneyId, 1, cancellationToken: token)).Success);
        var before = await File.ReadAllBytesAsync(Path.Combine(paths.DataDirectory, "settings.json"), token);
        settings.Armed = true;
        async Task<string?> Mutate() => starting
            ? (await facade.StartAsync(2, appointment.Id, journey.JourneyId, 1, token)).ErrorCode
            : (await facade.AttachAsync(1, appointment.Id, appointment.UpdatedAt, journey.JourneyId, 1, cancellationToken: token)).ErrorCode;
        var pending = Mutate();
        await settings.Waiting.Task.WaitAsync(token);
        Assert.False(pending.IsCompleted);
        if (change == 0) actors.Current = actors.Current with { AuthenticationRevision = "changed-while-waiting" };
        if (change is 1 or 2)
        {
            var kind = change == 1 ? "maps" : "planner";
            var record = Assert.Single((await home.ReadAsync(token)).State!.Records,
                record => record.RecordType == "home.local-store-ownership" && record.Payload.Deserialize<HomeLocalStoreBinding>()!.ResourceKind == kind);
            var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await home.WriteAsync(record with { Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "revoked" }) }, record.Revision, token)).IsSuccess);
        }
        if (change == 3)
        {
            var calendar = Assert.Single(await planner.GetCalendarsAsync(false, token), item => item.Id == appointment.CalendarId);
            // Both paths pin the exact Calendar snapshot, even where Reader would allow a fresh navigation request.
            await planner.UpsertCalendarAsync(calendar with { Permission = CalendarPermission.Reader }, token);
        }
        if (change == 4)
            await planner.UpsertEventAsync(appointment with { Title = "Changed after admission", UpdatedAt = appointment.UpdatedAt.AddSeconds(1) }, token);
        settings.Release();
        Assert.Equal(change == -1 ? null : "Denied", await pending);
        var retained = await maps.ReadAsync(token);
        Assert.Equal(change == -1 ? (starting ? 3 : 2) : (starting ? 2 : 1), retained.Revision);
        if (change != -1)
        {
            Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(paths.DataDirectory, "settings.json"), token));
            Assert.Empty(retained.ActiveJourneys);
            if (!starting) Assert.Empty(retained.PlannerJourneyReferences);
        }
        else if (starting) Assert.Single(retained.ActiveJourneys);
        else Assert.Single(retained.PlannerJourneyReferences);
    }

    private sealed class Actors(AuthenticatedResourceActor current) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current { get; set; } = current;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class LeaseBarrierStore(VersionedAtomicSettingsStore inner, string leasePath) : IVersionedSettingsStore,
        IVersionedSettingsGuardedCompareExchange, IResourceStoreIdentitySource, IDisposable
    {
        private FileStream? _lease;
        public bool Armed { get; set; }
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expectedJson, string? replacementJson,
            IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token)
        {
            if (!Armed) return inner.CompareExchangeGuardedAsync(key, expectedJson, replacementJson, guards, admission, token);
            Armed = false;
            _lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var pending = inner.CompareExchangeGuardedAsync(key, expectedJson, replacementJson, guards, admission, token);
            Assert.False(pending.IsCompleted);
            Waiting.TrySetResult();
            return pending;
        }
        public void Release() { _lease?.Dispose(); _lease = null; }
        public void Dispose() => Release();
        public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) => inner.GetStoreIdentityAsync(token);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expectedJson, string? replacementJson,
            IReadOnlyDictionary<string, string?> guards, CancellationToken token) => inner.CompareExchangeGuardedAsync(key, expectedJson, replacementJson, guards, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson, string? replacementJson, CancellationToken token) => inner.CompareExchangeAsync(key, expectedJson, replacementJson, token);
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => inner.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => inner.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => inner.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => inner.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => inner.ImportAsync(manifest, token);
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("maps-planner-commit-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "store.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(DataDirectory, true); }
    }
}
