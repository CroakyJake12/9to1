using Haven.Core;

namespace Haven.Application;

public sealed partial class MapsJourneyService
{
    // Only the authorised cross-app facade calls this commit; the normal library remains the sole store.
    internal Task<MapsJourneyResult<MapPlannerJourneyReference>> CommitPlannerReferenceAsync(long libraryRevision,
        PlannerEvent authorisedEvent, Guid journeyID, long expectedJourneyRevision, long? expectedLinkRevision,
        ISettingsCommitAdmission admission, CancellationToken cancellationToken) => MutateAsync(libraryRevision, library =>
        {
            var journey = library.Journeys.FirstOrDefault(item => item.JourneyId == journeyID)
                ?? throw new KeyNotFoundException("Journey not found.");
            if (journey.Revision != expectedJourneyRevision) throw new JourneyRevisionException();
            if (authorisedEvent.DeletedAt is not null || authorisedEvent.IsReadOnly)
                throw new InvalidOperationException("Planner appointment no longer accepts a journey reference.");
            var current = library.PlannerJourneyReferences.FirstOrDefault(reference => reference.EventID == authorisedEvent.Id && reference.JourneyID == journeyID);
            if (current?.Revision != expectedLinkRevision) throw new JourneyRevisionException();
            var saved = new MapPlannerJourneyReference(authorisedEvent.Id, authorisedEvent.CalendarId, authorisedEvent.UpdatedAt,
                journeyID, journey.Revision, checked((current?.Revision ?? 0) + 1));
            return (library with { PlannerJourneyReferences = library.PlannerJourneyReferences
                .Where(reference => reference.EventID != authorisedEvent.Id || reference.JourneyID != journeyID).Append(saved).ToArray() }, saved);
        }, cancellationToken, admission);
}
