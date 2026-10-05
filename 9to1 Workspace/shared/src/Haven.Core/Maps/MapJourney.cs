namespace Haven.Core;

public enum MapObjectVisibility { Private, Shared, Public }
public enum MapVerificationState { Unverified, CommunityConfirmed, Verified, Removed }
public enum MapJourneyStepKind { Travel, Stop, Wait, Activity, ManualInstruction }
public enum MapStepIntent { Required, Preferred, Optional }
public enum MapJourneyStepStatus { Pending, Active, Completed, Skipped }

public sealed record MapProviderReference(string ProviderId, string RecordId, string Attribution, DateTimeOffset? UpdatedAt);

/// <summary>Provider records supply provenance; this identity survives provider replacement.</summary>
public sealed record CanonicalMapPlace(Guid PlaceId, string Name, GeoPoint Coordinate, string? Category,
    string? Notes, MapObjectVisibility Visibility, MapVerificationState Verification,
    IReadOnlyList<MapProviderReference> Providers, DateTimeOffset CreatedAt, DateTimeOffset ModifiedAt, long Revision);

public sealed record MapJourneyStep(Guid StepId, MapJourneyStepKind Kind, string Instruction,
    MapStepIntent Intent = MapStepIntent.Required, Guid? PlaceId = null, GeoPoint? Coordinate = null,
    MapTravelProfile? TravelProfile = null, TimeSpan? Duration = null,
    IReadOnlyList<GeoPoint>? UserDefinedPath = null)
{
    public bool IsUserDefinedPath => UserDefinedPath is { Count: > 0 };
}

public sealed record MapSavedJourney(Guid JourneyId, string Name, IReadOnlyList<MapJourneyStep> Steps,
    MapObjectVisibility Visibility, DateTimeOffset CreatedAt, DateTimeOffset ModifiedAt, long Revision);

public sealed record MapJourneyProgress(Guid JourneyId, long JourneyRevision, Guid? CurrentStepId,
    IReadOnlyDictionary<Guid, MapJourneyStepStatus> Statuses, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt = null);

/// <summary>Canonical cross-app relationship; no copied appointment/address content.</summary>
public sealed record MapPlannerJourneyReference(Guid EventID, Guid CalendarID, DateTimeOffset EventRevision,
    Guid JourneyID, long JourneyRevision, long Revision);

/// <summary>Pure shared validation/runtime rules; manual actions and Dulche use the same typed steps.</summary>
public static class MapJourneyLogic
{
    public static bool IsCoordinateValid(GeoPoint? coordinate) => coordinate is not null
        && double.IsFinite(coordinate.Latitude) && double.IsFinite(coordinate.Longitude)
        && coordinate.Latitude is >= -90 and <= 90 && coordinate.Longitude is >= -180 and <= 180;

    public static void Validate(MapSavedJourney journey)
    {
        ArgumentNullException.ThrowIfNull(journey);
        if (journey.JourneyId == Guid.Empty || string.IsNullOrWhiteSpace(journey.Name) || journey.Revision < 0
            || !Enum.IsDefined(journey.Visibility) || journey.Steps is not { Count: > 0 }
            || journey.Steps.Any(step => step is null))
            throw new InvalidDataException("Journey identity, name or steps are invalid.");
        if (journey.Steps.Select(step => step.StepId).Distinct().Count() != journey.Steps.Count)
            throw new InvalidDataException("Journey step identities must be unique.");
        foreach (var step in journey.Steps)
        {
            if (step.StepId == Guid.Empty || !Enum.IsDefined(step.Kind) || !Enum.IsDefined(step.Intent)
                || string.IsNullOrWhiteSpace(step.Instruction) || step.PlaceId == Guid.Empty
                || step.Coordinate is not null && !IsCoordinateValid(step.Coordinate)
                || step.TravelProfile is { } profile && !Enum.IsDefined(profile)
                || step.Duration is { } duration && duration < TimeSpan.Zero
                || step.UserDefinedPath?.Any(point => !IsCoordinateValid(point)) == true
                || step.UserDefinedPath is { Count: 1 })
                throw new InvalidDataException("Journey contains an invalid step.");
            if (step.Kind == MapJourneyStepKind.Travel && step.PlaceId is null && step.Coordinate is null)
                throw new InvalidDataException("A travel step needs a canonical place or explicit coordinate.");
            if (step.Kind == MapJourneyStepKind.Wait && step.Duration is not { Ticks: > 0 })
                throw new InvalidDataException("A wait step needs a positive duration.");
        }
    }

    public static MapJourneyProgress Start(MapSavedJourney journey, DateTimeOffset now)
    {
        Validate(journey);
        var statuses = journey.Steps.ToDictionary(step => step.StepId, _ => MapJourneyStepStatus.Pending);
        statuses[journey.Steps[0].StepId] = MapJourneyStepStatus.Active;
        return new(journey.JourneyId, journey.Revision, journey.Steps[0].StepId, statuses, now);
    }

    public static MapJourneyProgress Advance(MapSavedJourney journey, MapJourneyProgress progress, bool skip, DateTimeOffset now)
    {
        Validate(journey);
        ValidateProgress(progress, journey);
        if (progress.JourneyId != journey.JourneyId || progress.JourneyRevision != journey.Revision)
            throw new InvalidOperationException("RevisionConflict: review the updated journey before continuing.");
        if (progress.CurrentStepId is not { } currentId) return progress;
        var index = journey.Steps.ToList().FindIndex(step => step.StepId == currentId);
        if (index < 0 || progress.Statuses.Count != journey.Steps.Count
            || journey.Steps.Any(step => !progress.Statuses.ContainsKey(step.StepId))
            || progress.Statuses[currentId] != MapJourneyStepStatus.Active)
            throw new InvalidDataException("Journey progress does not match the journey steps.");
        if (skip && journey.Steps[index].Intent == MapStepIntent.Required)
            throw new InvalidOperationException("RequiredStep: edit or complete the required instruction before continuing.");
        var statuses = progress.Statuses.ToDictionary(pair => pair.Key, pair => pair.Value);
        statuses[currentId] = skip ? MapJourneyStepStatus.Skipped : MapJourneyStepStatus.Completed;
        var next = index + 1 < journey.Steps.Count ? journey.Steps[index + 1].StepId : (Guid?)null;
        if (next is { } nextId) statuses[nextId] = MapJourneyStepStatus.Active;
        return progress with { Statuses = statuses, CurrentStepId = next, CompletedAt = next is null ? now : null };
    }

    /// <summary>Sharing never silently publishes a route's private place dependencies.</summary>
    public static IReadOnlyList<Guid> PrivateSharingDependencies(MapSavedJourney journey, IReadOnlyList<CanonicalMapPlace> places)
    {
        Validate(journey);
        var referenced = journey.Steps.Where(step => step.PlaceId.HasValue).Select(step => step.PlaceId!.Value).Distinct();
        return referenced.Where(id =>
        {
            var place = places.FirstOrDefault(place => place.PlaceId == id);
            return place is null || place.Visibility == MapObjectVisibility.Private
                || journey.Visibility == MapObjectVisibility.Public && place.Visibility != MapObjectVisibility.Public;
        }).ToArray();
    }

    public static void ValidateProgress(MapJourneyProgress progress, MapSavedJourney journey)
    {
        if (progress.JourneyId != journey.JourneyId || progress.JourneyRevision < 1 || progress.JourneyRevision > journey.Revision
            || progress.Statuses is not { Count: > 0 } || progress.Statuses.Any(pair => pair.Key == Guid.Empty || !Enum.IsDefined(pair.Value))
            || progress.CompletedAt is { } completed && completed < progress.StartedAt)
            throw new InvalidDataException("Journey progress identity, revision or statuses are invalid.");
        var active = progress.Statuses.Where(pair => pair.Value == MapJourneyStepStatus.Active).Select(pair => pair.Key).ToArray();
        if (progress.CurrentStepId is { } current)
        {
            if (active.Length != 1 || active[0] != current || progress.CompletedAt is not null)
                throw new InvalidDataException("Journey progress must contain exactly its current active step.");
        }
        else if (active.Length != 0 || progress.CompletedAt is null || progress.Statuses.Values.Any(status => status is MapJourneyStepStatus.Pending))
            throw new InvalidDataException("A completed journey cannot retain pending or active steps.");
        // An edited saved journey deliberately retains an older pinned progress record. Resume
        // reports RevisionConflict; the previous authored instructions are not silently mapped.
        if (progress.JourneyRevision == journey.Revision && !progress.Statuses.Keys.ToHashSet().SetEquals(journey.Steps.Select(step => step.StepId)))
            throw new InvalidDataException("Journey progress references another revision's steps.");
    }
}
