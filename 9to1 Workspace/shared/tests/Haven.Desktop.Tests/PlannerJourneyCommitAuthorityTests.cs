using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;

namespace Haven.Desktop.Tests;

public sealed class PlannerJourneyCommitAuthorityTests
{
    [Fact]
    public async Task Actual_imported_Planner_receipt_pins_event_calendar_and_current_permission()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-planner-journey-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = new SqliteDatabase(new Paths(root)); await database.InitializeAsync(token);
            var repository = new PlannerRepository(database); await repository.EnsureDefaultsAsync(token);
            var now = DateTimeOffset.UtcNow;
            var proposed = new PlannerEvent(Guid.NewGuid(), PlannerDefaults.LocalCalendarId, "Appointment", "Notes", "Canonical location",
                now.AddHours(1), now.AddHours(2), false, null, null, false, null, null, now, now);
            await repository.UpsertEventAsync(proposed, token);
            var plannerEvent = (await repository.GetEventAsync(proposed.Id, token))!;
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var actors = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var actor = (await actors.GetCurrentAsync(token))!;
            var permissions = new HomePermissionTrustService(home, (app, action) =>
                app == "9to1.home.local-profile" && action == "home.profile.importStore"
                    ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null);
            var evidence = new PlannerLocalStoreEvidenceProvider(database, database);
            var ownership = new HomeLocalStoreOwnership(home, actors, new HomeLocalStoreEvidenceRegistry([evidence]), permissions);
            var receipts = new HomeResourceStoreOwnershipAuthority(ownership, actors);
            var authority = new PlannerJourneyCommitAuthority(repository, actors, receipts);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.CaptureAsync(actor, plannerEvent, true, token));
            var setup = new HomeLocalStoreSetupSession("planner", database, actors, ownership, evidence);
            Assert.False((await setup.InspectAsync(token)).CanBindEmpty); // Existing canonical migrations/data are never auto-bound.
            var request = await setup.RequestImportAsync(token);
            Assert.True((await permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            await setup.CompleteImportAsync(token);
            var captured = await authority.CaptureAsync(actor, plannerEvent, true, token);
            Assert.True(await captured.CheckCurrentAsync(token));
            Assert.Equal(plannerEvent.Id, captured.EventId); Assert.Equal(plannerEvent.CalendarId, captured.CalendarId);
            var calendar = Assert.Single(await repository.GetCalendarsAsync(false, token), item => item.Id == plannerEvent.CalendarId);
            await repository.UpsertCalendarAsync(calendar with { Permission = CalendarPermission.Reader }, token);
            Assert.False(await captured.CheckCurrentAsync(token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.CaptureAsync(actor, plannerEvent, true, token));
            var read = await authority.CaptureAsync(actor, plannerEvent, false, token);
            Assert.True(await read.CheckCurrentAsync(token));
            await repository.UpsertCalendarAsync(calendar with { Permission = CalendarPermission.Owner }, token);
            Assert.False(await read.CheckCurrentAsync(token));
            read = await authority.CaptureAsync(actor, plannerEvent, false, token);
            var other = calendar with { Id = Guid.NewGuid(), ProviderCalendarId = Guid.NewGuid().ToString("N"), Name = "Moved target", Permission = CalendarPermission.Owner };
            await repository.UpsertCalendarAsync(other, token);
            await repository.UpsertEventAsync(plannerEvent with { CalendarId = other.Id, UpdatedAt = plannerEvent.UpdatedAt.AddSeconds(1) }, token);
            Assert.False(await read.CheckCurrentAsync(token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.CaptureAsync(actor, plannerEvent, false, token));
            var moved = (await repository.GetEventAsync(plannerEvent.Id, token))!;
            Assert.True(await (await authority.CaptureAsync(actor, moved, true, token)).CheckCurrentAsync(token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.CaptureAsync(actor with { AuthenticationRevision = actor.AuthenticationRevision + "-stale" }, moved, true, token));
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
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
