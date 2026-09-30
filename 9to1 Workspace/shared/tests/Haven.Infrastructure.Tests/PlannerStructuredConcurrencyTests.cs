using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class PlannerStructuredConcurrencyTests
{
    [Fact]
    public async Task Journey_event_authority_rechecks_calendar_permission_profile_and_revision()
    {
        using var paths = new Paths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(CancellationToken.None);
        var repository = new PlannerRepository(database);
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var plannerEvent = new PlannerEvent(Guid.NewGuid(), PlannerDefaults.LocalCalendarId, "Walk to library", "", "",
            now.AddHours(1), now.AddHours(2), false, null, null, false, null, null, now, now);
        await repository.UpsertEventAsync(plannerEvent, CancellationToken.None);
        var actors = new Actors();
        var authority = new ResourceAuthorizationService(actors,
            [new PlannerEventResourceResolver(repository, new ProfilePlannerCalendarResourceBinding("profile-one"))]);
        var revision = now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var scope = new ResourceScope("planner.event", plannerEvent.Id.ToString("N"), revision, ResourceAccess.Write);
        Assert.NotNull(await authority.AuthorizeAsync("maps.planner.attach", [scope]));
        actors.Current = actors.Current with { ProfileId = "other-profile" };
        Assert.Null(await authority.AuthorizeAsync("maps.planner.attach", [scope]));
        actors.Current = actors.Current with { ProfileId = "profile-one", OrganisationId = Guid.NewGuid(), AccountId = Guid.NewGuid() };
        Assert.Null(await authority.AuthorizeAsync("maps.planner.attach", [scope]));
        actors.Current = actors.Current with { OrganisationId = null, AccountId = null };
        var calendar = (await repository.GetCalendarsAsync(false, CancellationToken.None)).Single(item => item.Id == plannerEvent.CalendarId);
        await repository.UpsertCalendarAsync(calendar with { Permission = CalendarPermission.Reader }, CancellationToken.None);
        Assert.Null(await authority.AuthorizeAsync("maps.planner.attach", [scope]));
        Assert.NotNull(await authority.AuthorizeAsync("maps.planner.read", [scope with { Access = ResourceAccess.Read }]));
        Assert.Null(await authority.AuthorizeAsync("maps.planner.read", [scope with { Access = ResourceAccess.Read, Revision = now.AddSeconds(-1).ToString("O") }]));
        await repository.UpsertCalendarAsync(calendar, CancellationToken.None);
        await repository.DeleteEventAsync(plannerEvent.Id, now, CancellationToken.None);
        Assert.Null(await authority.AuthorizeAsync("maps.planner.read", [scope with { Access = ResourceAccess.Read }]));
    }

    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current = new("owner", "profile-one", null, null, "signed-session-1");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }

    [Fact]
    public async Task Concurrent_writers_preserve_one_revision_and_deleted_assignments_require_restore()
    {
        using var paths = new Paths();
        var cancellationToken = CancellationToken.None;
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(cancellationToken);
        var first = new PlannerRepository(database);
        var second = new PlannerRepository(new SqliteDatabase(paths));
        var service = new PlannerStructuredEntityService(first);
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var assignment = new PlannerAssignment(id, "Deadline", null, PlannerAssignmentStatus.NotStarted,
            null, now.AddDays(2), null, null, null, "owner", "personal", [], [], [], [], [], 1, now, now);
        var created = await service.SaveAssignmentAsync(assignment, null, cancellationToken);
        async Task<bool> WriteAsync(PlannerRepository repository, string name)
        {
            try
            {
                await repository.UpsertAsync(created with { Name = name }, 1, "PlannerAssignmentChanged", null, "{}", cancellationToken);
                return true;
            }
            catch (PlannerRevisionConflictException) { return false; }
        }
        var outcomes = await Task.WhenAll(Task.Run(() => WriteAsync(first, "First"), cancellationToken),
            Task.Run(() => WriteAsync(second, "Second"), cancellationToken));
        Assert.Single(outcomes, outcome => outcome);
        var updated = await first.GetAsync(PlannerStructuredEntityKind.Assignment, id, cancellationToken);
        Assert.Equal(2, updated!.Revision);
        Assert.Equal(2, (await first.GetPendingAutomationEventsAsync(100, cancellationToken)).Count);
        var deleted = await first.SetDeletedAsync(PlannerStructuredEntityKind.Assignment, id, 2, now, cancellationToken);
        Assert.Null(await service.GetAssignmentAsync(id, cancellationToken));
        await Assert.ThrowsAsync<PlannerRevisionConflictException>(() => second.UpsertAsync(deleted, 3,
            "PlannerAssignmentChanged", null, "{}", cancellationToken));
        Assert.Equal(3, (await first.GetPendingAutomationEventsAsync(100, cancellationToken)).Count);
        await service.RestoreAsync(PlannerStructuredEntityKind.Assignment, id, 3, cancellationToken);
        var restored = await service.GetAssignmentAsync(id, cancellationToken);
        Assert.Equal(id, restored!.AssignmentId);
        Assert.Equal(4, restored.Revision);
    }

    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-planner-cas-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "planner.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public Paths() => Directory.CreateDirectory(DataDirectory);
        public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(DataDirectory, true); }
    }
}
