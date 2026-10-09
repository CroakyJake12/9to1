using System.Globalization;
using Haven.Core;

namespace Haven.Application;

public sealed record MapsPlannerJourneyView(MapPlannerJourneyReference Reference, PlannerEvent Event,
    MapSavedJourney Journey, bool EventChanged, bool JourneyChanged);

/// <summary>Native cross-app facade: canonical owning services plus authenticated resource intersection.</summary>
public sealed class MapsPlannerJourneyService(MapsJourneyService journeys, PlannerEventJourneyAccessService events,
    ResourceAuthorizationService resources, IMapsJourneyCommitAuthority? mapsCommitAuthority = null,
    PlannerJourneyCommitAuthority? plannerCommitAuthority = null)
{
    public async Task<MapsJourneyResult<MapPlannerJourneyReference>> AttachAsync(long libraryRevision, Guid eventID,
        DateTimeOffset expectedEventRevision, Guid journeyID, long expectedJourneyRevision, long? expectedReferenceRevision = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plannerEvent = await events.GetAuthorisedAsync(eventID, true, expectedEventRevision, cancellationToken).ConfigureAwait(false);
            var library = await journeys.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (library.Revision != libraryRevision) return Conflict<MapPlannerJourneyReference>();
            var journey = library.Journeys.FirstOrDefault(item => item.JourneyId == journeyID);
            if (journey is null) return Missing<MapPlannerJourneyReference>();
            if (journey.Revision != expectedJourneyRevision) return Conflict<MapPlannerJourneyReference>();
            var actor = await AuthoriseAsync("maps.planner.attach", plannerEvent, journey, ResourceAccess.Write, ResourceAccess.Write, cancellationToken).ConfigureAwait(false);
            if (actor is null || mapsCommitAuthority is null || plannerCommitAuthority is null)
                return Denied<MapPlannerJourneyReference>();
            // Resource owners recheck current authority; revisions make a stale proposal fail closed.
            plannerEvent = await events.GetAuthorisedAsync(eventID, true, expectedEventRevision, cancellationToken).ConfigureAwait(false);
            var mapsAdmission = await mapsCommitAuthority.CaptureAsync(journeyID, expectedJourneyRevision,
                "maps.planner.attach", actor, cancellationToken).ConfigureAwait(false);
            if (mapsAdmission is null) return Denied<MapPlannerJourneyReference>();
            var plannerReceipt = await plannerCommitAuthority.CaptureAsync(actor, plannerEvent, true, cancellationToken).ConfigureAwait(false);
            return await journeys.CommitPlannerReferenceAsync(libraryRevision, plannerEvent, journeyID, expectedJourneyRevision,
                expectedReferenceRevision, new CrossOwnerAdmission(mapsAdmission, plannerReceipt), cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException) { return Denied<MapPlannerJourneyReference>(); }
        catch (PlannerEventUnavailableException) { return Missing<MapPlannerJourneyReference>(); }
        catch (PlannerEventRevisionConflictException) { return Conflict<MapPlannerJourneyReference>(); }
    }

    public async Task<MapsJourneyResult<MapsPlannerJourneyView>> ReadAsync(Guid eventID, Guid journeyID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plannerEvent = await events.GetAuthorisedAsync(eventID, false, cancellationToken: cancellationToken).ConfigureAwait(false);
            var library = await journeys.ReadAsync(cancellationToken).ConfigureAwait(false);
            var reference = library.PlannerJourneyReferences.FirstOrDefault(item => item.EventID == eventID && item.JourneyID == journeyID);
            var journey = library.Journeys.FirstOrDefault(item => item.JourneyId == journeyID);
            if (reference is null || journey is null) return Missing<MapsPlannerJourneyView>();
            if (await AuthoriseAsync("maps.planner.read", plannerEvent, journey, ResourceAccess.Read, ResourceAccess.Read, cancellationToken).ConfigureAwait(false) is null)
                return Denied<MapsPlannerJourneyView>();
            return new(new(reference, plannerEvent, journey,
                reference.EventRevision != plannerEvent.UpdatedAt || reference.CalendarID != plannerEvent.CalendarId,
                reference.JourneyRevision != journey.Revision), null, null);
        }
        catch (UnauthorizedAccessException) { return Denied<MapsPlannerJourneyView>(); }
        catch (PlannerEventUnavailableException) { return Missing<MapsPlannerJourneyView>(); }
        catch (PlannerEventRevisionConflictException) { return Conflict<MapsPlannerJourneyView>(); }
    }

    public async Task<MapsJourneyResult<MapJourneyProgress>> StartAsync(long libraryRevision, Guid eventID, Guid journeyID,
        long expectedReferenceRevision, CancellationToken cancellationToken = default)
    {
        var viewed = await ReadAsync(eventID, journeyID, cancellationToken).ConfigureAwait(false);
        if (!viewed.Success) return new(null, viewed.ErrorCode, viewed.Message);
        var view = viewed.Value!;
        if (view.Reference.Revision != expectedReferenceRevision || view.EventChanged || view.JourneyChanged)
            return Conflict<MapJourneyProgress>();
        var actor = await AuthoriseAsync("maps.planner.start", view.Event, view.Journey, ResourceAccess.Read, ResourceAccess.Execute, cancellationToken).ConfigureAwait(false);
        if (actor is null || mapsCommitAuthority is null || plannerCommitAuthority is null)
            return Denied<MapJourneyProgress>();
        // No cached Calendar decision survives a revoke or moved appointment.
        try { await events.GetAuthorisedAsync(eventID, false, view.Event.UpdatedAt, cancellationToken).ConfigureAwait(false); }
        catch (UnauthorizedAccessException) { return Denied<MapJourneyProgress>(); }
        catch (PlannerEventUnavailableException) { return Missing<MapJourneyProgress>(); }
        catch (PlannerEventRevisionConflictException) { return Conflict<MapJourneyProgress>(); }
        try
        {
            var mapsAdmission = await mapsCommitAuthority.CaptureAsync(journeyID, view.Journey.Revision,
                "maps.planner.start", actor, cancellationToken).ConfigureAwait(false);
            if (mapsAdmission is null) return Denied<MapJourneyProgress>();
            var plannerReceipt = await plannerCommitAuthority.CaptureAsync(actor, view.Event, false, cancellationToken).ConfigureAwait(false);
            return await journeys.StartNavigationAdmittedAsync(libraryRevision, journeyID,
                new CrossOwnerAdmission(mapsAdmission, plannerReceipt), cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException) { return Denied<MapJourneyProgress>(); }
    }

    private async ValueTask<AuthenticatedResourceActor?> AuthoriseAsync(string actionID, PlannerEvent plannerEvent, MapSavedJourney journey,
        ResourceAccess eventAccess, ResourceAccess journeyAccess, CancellationToken cancellationToken) =>
        await resources.AuthorizeAsync(actionID,
        [new("planner.event", plannerEvent.Id.ToString("N"), plannerEvent.UpdatedAt.ToString("O", CultureInfo.InvariantCulture), eventAccess),
         new("maps.journey", journey.JourneyId.ToString("N"), journey.Revision.ToString(CultureInfo.InvariantCulture), journeyAccess)], cancellationToken).ConfigureAwait(false);

    // Home, Planner SQL and Maps settings remain separate stores. Both callbacks observe current
    // authority at Maps lease admission and immediately before publication; no global atomicity is claimed.
    private sealed class CrossOwnerAdmission(ISettingsCommitAdmission maps, PlannerJourneyCommitReceipt planner)
        : ISettingsCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken cancellationToken) =>
            await maps.CheckAsync(context, cancellationToken).ConfigureAwait(false)
            && await planner.CheckCurrentAsync(cancellationToken).ConfigureAwait(false)
            && await maps.CheckAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private static MapsJourneyResult<T> Denied<T>() => new(default, "Denied", "Current resource permissions do not allow this action.");
    private static MapsJourneyResult<T> Conflict<T>() => new(default, "RevisionConflict", "The canonical appointment, journey or reference changed; refresh before continuing.");
    private static MapsJourneyResult<T> Missing<T>() => new(default, "NotFound", "The canonical journey reference is unavailable.");
}
