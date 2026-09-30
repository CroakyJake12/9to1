using System.Globalization;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class MapsPlannerJourneyTests
{
    [Fact]
    public async Task Canonical_journey_attached_to_real_Planner_event_survives_restart_and_permission_revocation()
    {
        using var paths = new Paths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(default);
        var repository = new PlannerRepository(database);
        await repository.EnsureDefaultsAsync(default);
        var now = DateTimeOffset.UtcNow;
        var plannerEvent = Event(now);
        await repository.UpsertEventAsync(plannerEvent, default);
        plannerEvent = (await repository.GetEventAsync(plannerEvent.Id, default))!;
        var maps = new MapsJourneyService(new VersionedAtomicSettingsStore(paths));
        var journey = Journey(now);
        var saved = await maps.SaveJourneyAsync(0, journey);
        Assert.True(saved.Success, saved.Message);
        var policy = new Policy();
        var actors = new Actors();
        var facade = Facade(maps, repository, policy, actors);
        var attached = await facade.AttachAsync(1, plannerEvent.Id, plannerEvent.UpdatedAt, journey.JourneyId, 1);
        Assert.True(attached.Success, attached.Message);
        Assert.Equal(plannerEvent.Id, attached.Value!.EventID);
        Assert.Equal(journey.JourneyId, attached.Value.JourneyID);
        Assert.Equal(1, attached.Value.Revision);

        // Reopen both canonical stores, rather than copying a route/address into Planner.
        repository = new PlannerRepository(new SqliteDatabase(paths));
        maps = new MapsJourneyService(new VersionedAtomicSettingsStore(paths));
        facade = Facade(maps, repository, policy, actors);
        var view = await facade.ReadAsync(plannerEvent.Id, journey.JourneyId);
        Assert.True(view.Success, view.Message);
        Assert.Equal(attached.Value, view.Value!.Reference);
        Assert.Equal(journey.JourneyId, view.Value.Journey.JourneyId);
        var started = await facade.StartAsync(2, plannerEvent.Id, journey.JourneyId, 1);
        Assert.True(started.Success, started.Message);
        Assert.Equal(journey.JourneyId, started.Value!.JourneyId);
        Assert.Equal(journey.Steps[0].StepId, started.Value.CurrentStepId);

        var calendar = Assert.Single((await repository.GetCalendarsAsync(false, default)), item => item.Id == plannerEvent.CalendarId);
        await repository.UpsertCalendarAsync(calendar with { Permission = CalendarPermission.Reader }, default);
        Assert.Equal("Denied", (await facade.AttachAsync(3, plannerEvent.Id, plannerEvent.UpdatedAt, journey.JourneyId, 1, 1)).ErrorCode);
        Assert.Equal(3, (await maps.ReadAsync()).Revision);
        Assert.True((await facade.ReadAsync(plannerEvent.Id, journey.JourneyId)).Success);

        policy.ReadAllowed = false;
        Assert.Equal("Denied", (await facade.ReadAsync(plannerEvent.Id, journey.JourneyId)).ErrorCode);
        Assert.Equal("Denied", (await facade.StartAsync(3, plannerEvent.Id, journey.JourneyId, 1)).ErrorCode);
        Assert.Equal(3, (await maps.ReadAsync()).Revision);
        policy.ReadAllowed = true;
        actors.Profile = "another-profile";
        Assert.Equal("Denied", (await facade.ReadAsync(plannerEvent.Id, journey.JourneyId)).ErrorCode);
        Assert.Equal(3, (await maps.ReadAsync()).Revision);
    }

    [Fact]
    public async Task Stale_journey_reference_is_visible_but_cannot_start_without_explicit_reassociation()
    {
        using var paths = new Paths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(default);
        var repository = new PlannerRepository(database);
        await repository.EnsureDefaultsAsync(default);
        var now = DateTimeOffset.UtcNow;
        var plannerEvent = Event(now);
        await repository.UpsertEventAsync(plannerEvent, default);
        plannerEvent = (await repository.GetEventAsync(plannerEvent.Id, default))!;
        var maps = new MapsJourneyService(new VersionedAtomicSettingsStore(paths));
        var saved = (await maps.SaveJourneyAsync(0, Journey(now))).Value!;
        var facade = Facade(maps, repository, new Policy(), new Actors());
        Assert.True((await facade.AttachAsync(1, plannerEvent.Id, plannerEvent.UpdatedAt, saved.JourneyId, 1)).Success);
        Assert.True((await maps.SaveJourneyAsync(2, saved with { Name = "Edited itinerary" }, 1)).Success);
        var viewed = await facade.ReadAsync(plannerEvent.Id, saved.JourneyId);
        Assert.True(viewed.Success);
        Assert.True(viewed.Value!.JourneyChanged);
        Assert.Equal(saved.JourneyId, viewed.Value.Reference.JourneyID);
        Assert.Equal("RevisionConflict", (await facade.StartAsync(3, plannerEvent.Id, saved.JourneyId, 1)).ErrorCode);
        Assert.Empty((await maps.ReadAsync()).ActiveJourneys);
        Assert.True((await facade.AttachAsync(3, plannerEvent.Id, plannerEvent.UpdatedAt, saved.JourneyId, 2, 1)).Success);
        Assert.True((await facade.StartAsync(4, plannerEvent.Id, saved.JourneyId, 2)).Success);
    }

    [Fact]
    public async Task Missing_owner_resolver_denies_attach_without_persisting_a_relationship()
    {
        using var paths = new Paths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(default);
        var repository = new PlannerRepository(database);
        await repository.EnsureDefaultsAsync(default);
        var now = DateTimeOffset.UtcNow;
        var plannerEvent = Event(now);
        await repository.UpsertEventAsync(plannerEvent, default);
        plannerEvent = (await repository.GetEventAsync(plannerEvent.Id, default))!;
        var maps = new MapsJourneyService(new VersionedAtomicSettingsStore(paths));
        var saved = (await maps.SaveJourneyAsync(0, Journey(now))).Value!;
        var access = new PlannerEventJourneyAccessService(repository, new Policy());
        var facade = new MapsPlannerJourneyService(maps, access, new ResourceAuthorizationService(new Actors(), []));
        Assert.Equal("Denied", (await facade.AttachAsync(1, plannerEvent.Id, plannerEvent.UpdatedAt, saved.JourneyId, 1)).ErrorCode);
        Assert.Empty((await maps.ReadAsync()).PlannerJourneyReferences);
        Assert.Equal(1, (await maps.ReadAsync()).Revision);
    }

    [Fact]
    public async Task Maps_resolver_rechecks_live_store_binding_and_rejects_revocation_during_evaluation()
    {
        using var paths = new Paths();
        var maps = new MapsJourneyService(new VersionedAtomicSettingsStore(paths));
        var journey = (await maps.SaveJourneyAsync(0, Journey(DateTimeOffset.UtcNow))).Value!;
        var identity = await maps.GetStoreIdentityAsync(default);
        var owner = new BoundOwner(identity.StoreId.ToString("D"));
        var resolver = new MapsJourneyResourceResolver(maps, maps, owner);
        var actor = new AuthenticatedResourceActor("trusted-user", "local-profile", null, null, "session-1");
        var scope = new ResourceScope("maps.journey", journey.JourneyId.ToString("D"), "1", ResourceAccess.Read);
        Assert.True((await resolver.EvaluateAsync(actor, "maps.planner.read", scope, default)).Allowed);
        owner.RevokeAfterFirstRead = true;
        owner.Reads = 0;
        Assert.Equal("StoreOwnershipChanged", (await resolver.EvaluateAsync(actor, "maps.planner.read", scope, default)).Code);
        Assert.False((await resolver.EvaluateAsync(actor, "maps.planner.read", scope, default)).Allowed);
        Assert.Equal(1, (await maps.ReadAsync()).Revision);
    }

    private static MapsPlannerJourneyService Facade(MapsJourneyService maps, IPlannerRepository repository, Policy policy, Actors actors)
    {
        var events = new PlannerEventJourneyAccessService(repository, policy);
        return new(maps, events, new ResourceAuthorizationService(actors,
            [new PlannerEventResourceResolver(repository, new ProfilePlannerCalendarResourceBinding("local-profile")), new MapsJourneyResourceResolver(maps, maps, new BoundOwner(maps.GetStoreIdentityAsync(default).AsTask().GetAwaiter().GetResult().StoreId.ToString("D")))]));
    }
    private static PlannerEvent Event(DateTimeOffset now) => new(Guid.NewGuid(), PlannerDefaults.LocalCalendarId,
        "College appointment", "", "", now.AddHours(1), now.AddHours(2), false, null, null, false, null, null, now, now);
    private static MapSavedJourney Journey(DateTimeOffset now) => new(Guid.NewGuid(), "College via bench",
        [new(Guid.NewGuid(), MapJourneyStepKind.ManualInstruction, "Use the side entrance"),
         new(Guid.NewGuid(), MapJourneyStepKind.Wait, "Wait ten minutes", Duration: TimeSpan.FromMinutes(10))],
        MapObjectVisibility.Private, now, now, 0);
    private sealed class BoundOwner(string storeID) : IResourceStoreOwnershipAuthority
    {
        public bool RevokeAfterFirstRead { get; set; }
        public int Reads { get; set; }
        public ValueTask<VerifiedResourceStoreOwnership?> GetVerifiedAsync(string kind, string id, CancellationToken token) =>
            ValueTask.FromResult<VerifiedResourceStoreOwnership?>(!(RevokeAfterFirstRead && ++Reads > 1) && kind == "maps" && id == storeID
                ? new(kind, id, "local-profile", "controlled-binding-revision") : null);
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public string Profile { get; set; } = "local-profile";
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<AuthenticatedResourceActor?>(new("trusted-user", Profile, null, null, "session-1"));
    }
    private sealed class Policy : IPlannerEventJourneyPolicy
    {
        public bool ReadAllowed { get; set; } = true;
        public ValueTask<bool> MayReadAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken) => ValueTask.FromResult(ReadAllowed);
        public ValueTask<bool> MayAttachJourneyAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken) => ValueTask.FromResult(ReadAllowed && !plannerEvent.IsReadOnly);
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-maps-planner-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "planner.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public Paths() => Directory.CreateDirectory(DataDirectory);
        public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(DataDirectory, true); }
    }
}
