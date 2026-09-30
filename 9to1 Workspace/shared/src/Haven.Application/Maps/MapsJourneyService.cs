using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed record MapsJourneyLibrary(int SchemaVersion, long Revision, IReadOnlyList<CanonicalMapPlace> Places,
    IReadOnlyList<MapSavedJourney> Journeys, IReadOnlyList<MapJourneyProgress> ActiveJourneys)
{
    public IReadOnlyList<MapPlannerJourneyReference> PlannerJourneyReferences { get; init; } = [];
    public static MapsJourneyLibrary Empty { get; } = new(1, 0, [], [], []);
}

public sealed record MapsJourneyResult<T>(T? Value, string? ErrorCode, string? Message)
{
    public bool Success => ErrorCode is null;
}

/// <summary>Private landmarks and saved custom journeys, persisted through the shared user settings store.</summary>
public sealed partial class MapsJourneyService(IVersionedSettingsStore settings) : IResourceStoreIdentitySource
{
    private const string Key = "maps.journeys.v1";
    private readonly IVersionedSettingsStore _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken cancellationToken) =>
        _settings is IResourceStoreIdentitySource source ? source.GetStoreIdentityAsync(cancellationToken)
        : ValueTask.FromException<ResourceStoreIdentity>(new InvalidOperationException("Maps store has no durable identity authority."));

    public async Task<MapsJourneyLibrary> ReadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { return await LoadAsync(token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public Task<MapsJourneyResult<CanonicalMapPlace>> CreateLandmarkAsync(long revision, Guid placeId, string name,
        GeoPoint coordinate, string? category = null, string? notes = null, CancellationToken token = default) =>
        MutateAsync(revision, library =>
        {
            if (library.Places.Any(place => place.PlaceId == placeId)) throw new InvalidOperationException("Place identity already exists.");
            var now = DateTimeOffset.UtcNow;
            var place = new CanonicalMapPlace(placeId, name.Trim(), coordinate, category, notes, MapObjectVisibility.Private,
                MapVerificationState.Unverified, [], now, now, 1);
            ValidatePlace(place);
            return (library with { Places = library.Places.Append(place).ToArray() }, place);
        }, token);

    public Task<MapsJourneyResult<MapSavedJourney>> SaveJourneyAsync(long revision, MapSavedJourney journey,
        long? expectedJourneyRevision = null, CancellationToken token = default) =>
        MutateAsync(revision, library =>
        {
            MapJourneyLogic.Validate(journey);
            var current = library.Journeys.FirstOrDefault(item => item.JourneyId == journey.JourneyId);
            if (current is not null && expectedJourneyRevision != current.Revision)
                throw new JourneyRevisionException();
            if (current is null && expectedJourneyRevision is not null) throw new KeyNotFoundException("Journey not found.");
            foreach (var step in journey.Steps)
                if (step.PlaceId is { } id && !library.Places.Any(place => place.PlaceId == id))
                    throw new KeyNotFoundException("A referenced canonical place is unavailable.");
            if (journey.Visibility != MapObjectVisibility.Private && MapJourneyLogic.PrivateSharingDependencies(journey, library.Places).Count > 0)
                throw new PrivateDependencyException();
            var now = DateTimeOffset.UtcNow;
            var saved = journey with { Revision = checked((current?.Revision ?? 0) + 1), CreatedAt = current?.CreatedAt ?? now, ModifiedAt = now };
            var journeys = library.Journeys.Where(item => item.JourneyId != saved.JourneyId).Append(saved).ToArray();
            return (library with { Journeys = journeys }, saved);
        }, token);

    public Task<MapsJourneyResult<MapSavedJourney>> DuplicateJourneyAsync(long revision, Guid id, string name, CancellationToken token = default) =>
        MutateAsync(revision, library =>
        {
            var original = library.Journeys.FirstOrDefault(item => item.JourneyId == id) ?? throw new KeyNotFoundException("Journey not found.");
            var now = DateTimeOffset.UtcNow;
            var copy = original with { JourneyId = Guid.NewGuid(), Name = name.Trim(), Visibility = MapObjectVisibility.Private,
                Steps = original.Steps.Select(step => step with { StepId = Guid.NewGuid() }).ToArray(), CreatedAt = now, ModifiedAt = now, Revision = 1 };
            MapJourneyLogic.Validate(copy);
            return (library with { Journeys = library.Journeys.Append(copy).ToArray() }, copy);
        }, token);

    public Task<MapsJourneyResult<MapJourneyProgress>> StartNavigationAsync(long revision, Guid journeyId, CancellationToken token = default) =>
        MutateAsync(revision, library =>
        {
            var journey = library.Journeys.FirstOrDefault(item => item.JourneyId == journeyId) ?? throw new KeyNotFoundException("Journey not found.");
            var existing = library.ActiveJourneys.FirstOrDefault(item => item.JourneyId == journeyId && item.CompletedAt is null);
            if (existing is not null) return (library, existing);
            var progress = MapJourneyLogic.Start(journey, DateTimeOffset.UtcNow);
            return (library with { ActiveJourneys = library.ActiveJourneys.Where(item => item.JourneyId != journeyId).Append(progress).ToArray() }, progress);
        }, token);

    public Task<MapsJourneyResult<MapJourneyProgress>> CompleteCurrentStepAsync(long revision, Guid journeyId, bool skip,
        CancellationToken token = default) =>
        MutateAsync(revision, library =>
        {
            var journey = library.Journeys.FirstOrDefault(item => item.JourneyId == journeyId) ?? throw new KeyNotFoundException("Journey not found.");
            var progress = library.ActiveJourneys.FirstOrDefault(item => item.JourneyId == journeyId) ?? throw new KeyNotFoundException("Navigation has not started.");
            var next = MapJourneyLogic.Advance(journey, progress, skip, DateTimeOffset.UtcNow);
            return (library with { ActiveJourneys = library.ActiveJourneys.Select(item => item.JourneyId == journeyId ? next : item).ToArray() }, next);
        }, token);

    private async Task<MapsJourneyResult<T>> MutateAsync<T>(long revision, Func<MapsJourneyLibrary, (MapsJourneyLibrary Library, T Value)> apply, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_settings is not IVersionedSettingsCompareExchange atomic)
                return new(default, "AtomicStoreUnavailable", "Maps requires atomic storage.");
            var stored = await _settings.ExportAsync(token).ConfigureAwait(false);
            stored.Settings.TryGetValue(Key, out var expectedJson);
            var current = expectedJson is null ? MapsJourneyLibrary.Empty : JsonSerializer.Deserialize<MapsJourneyLibrary>(expectedJson)
                ?? throw new InvalidDataException("Stored maps library is null.");
            Validate(current);
            if (revision != current.Revision) return new(default, "RevisionConflict", "Maps changed; refresh before applying this action.");
            var (next, value) = apply(current);
            next = next with { Revision = checked(revision + 1) };
            Validate(next);
            if (!(await atomic.CompareExchangeAsync(Key, expectedJson, JsonSerializer.Serialize(next), token).ConfigureAwait(false)).Exchanged)
                return new(default, "RevisionConflict", "Maps changed; refresh before applying this action.");
            return new(value, null, null);
        }
        catch (JourneyRevisionException) { return new(default, "RevisionConflict", "The saved journey changed."); }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("RevisionConflict:", StringComparison.Ordinal))
        { return new(default, "RevisionConflict", exception.Message); }
        catch (PrivateDependencyException) { return new(default, "PrivateDependency", "Explicitly share or remove private place dependencies before sharing this journey."); }
        catch (KeyNotFoundException exception) { return new(default, "NotFound", exception.Message); }
        catch (JsonException) { return new(default, "InvalidData", "Stored Maps data is incompatible or corrupt; prior state was preserved."); }
        catch (InvalidDataException exception) { return new(default, "InvalidData", exception.Message); }
        catch (ArgumentException exception) { return new(default, "InvalidArgument", exception.Message); }
        catch (InvalidOperationException exception) { return new(default, "InvalidOperation", exception.Message); }
        catch (IOException) { return new(default, "StorageUnavailable", "Maps could not be saved; check storage before retrying."); }
        finally { _gate.Release(); }
    }

    private async Task<MapsJourneyLibrary> LoadAsync(CancellationToken token)
    {
        var library = await _settings.GetAsync<MapsJourneyLibrary>(Key, token).ConfigureAwait(false) ?? MapsJourneyLibrary.Empty;
        Validate(library);
        return library;
    }

    private static void Validate(MapsJourneyLibrary library)
    {
        if (library.SchemaVersion != 1 || library.Revision < 0 || library.Places is null || library.Journeys is null || library.ActiveJourneys is null
            || library.Places.Any(place => place is null) || library.Journeys.Any(journey => journey is null) || library.ActiveJourneys.Any(progress => progress is null)
            || library.Places.Select(place => place.PlaceId).Distinct().Count() != library.Places.Count
            || library.Journeys.Select(journey => journey.JourneyId).Distinct().Count() != library.Journeys.Count
            || library.ActiveJourneys.Select(progress => progress.JourneyId).Distinct().Count() != library.ActiveJourneys.Count)
            throw new InvalidDataException("Maps journey library is invalid or uses an unsupported schema.");
        if (library.PlannerJourneyReferences is null || library.PlannerJourneyReferences.Any(reference => reference is null)
            || library.PlannerJourneyReferences.Select(reference => (reference.EventID, reference.JourneyID)).Distinct().Count() != library.PlannerJourneyReferences.Count)
            throw new InvalidDataException("Maps Planner references are invalid.");
        foreach (var reference in library.PlannerJourneyReferences)
            if (reference.EventID == Guid.Empty || reference.CalendarID == Guid.Empty || reference.JourneyID == Guid.Empty
                || reference.JourneyRevision < 1 || reference.Revision < 1
                || !library.Journeys.Any(journey => journey.JourneyId == reference.JourneyID && journey.Revision >= reference.JourneyRevision))
                throw new InvalidDataException("Maps Planner reference identity or pinned revision is invalid.");
        foreach (var place in library.Places) ValidatePlace(place);
        foreach (var journey in library.Journeys) MapJourneyLogic.Validate(journey);
        foreach (var progress in library.ActiveJourneys)
        {
            var journey = library.Journeys.FirstOrDefault(journey => journey.JourneyId == progress.JourneyId)
                ?? throw new InvalidDataException("Navigation references an unavailable journey.");
            MapJourneyLogic.ValidateProgress(progress, journey);
        }
    }

    private static void ValidatePlace(CanonicalMapPlace place)
    {
        if (place.PlaceId == Guid.Empty || string.IsNullOrWhiteSpace(place.Name) || !MapJourneyLogic.IsCoordinateValid(place.Coordinate)
            || !Enum.IsDefined(place.Visibility) || !Enum.IsDefined(place.Verification) || place.Revision < 1 || place.Providers is null)
            throw new InvalidDataException("Canonical place identity, coordinate or metadata is invalid.");
    }

    private sealed class JourneyRevisionException : Exception;
    private sealed class PrivateDependencyException : Exception;
}
