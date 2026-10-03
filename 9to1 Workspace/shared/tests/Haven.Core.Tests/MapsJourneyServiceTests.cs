using System.Text.Json;
using Haven.Application;

namespace Haven.Core.Tests;

public sealed class MapsJourneyServiceTests
{
    [Fact]
    public async Task Private_landmark_and_typed_wait_survive_restart_and_navigation()
    {
        var store = new MemorySettings();
        var service = new MapsJourneyService(store);
        var placeId = Guid.NewGuid();
        Assert.True((await service.CreateLandmarkAsync(0, placeId, "Riverside bench", new(54.776, -1.576), "bench")).Success);
        var journey = Journey(placeId);
        var saved = await service.SaveJourneyAsync(1, journey);
        Assert.True(saved.Success, saved.Message);
        var started = await service.StartNavigationAsync(2, journey.JourneyId);
        Assert.True(started.Success);
        service = new MapsJourneyService(store);
        var state = await service.ReadAsync();
        Assert.Equal(MapObjectVisibility.Private, state.Places.Single().Visibility);
        Assert.Equal(MapVerificationState.Unverified, state.Places.Single().Verification);
        Assert.Equal(journey.Steps[0].StepId, state.ActiveJourneys.Single().CurrentStepId);
        var next = await service.CompleteCurrentStepAsync(3, journey.JourneyId, false);
        Assert.True(next.Success);
        Assert.Equal(journey.Steps[1].StepId, next.Value!.CurrentStepId);
        var skipped = await service.CompleteCurrentStepAsync(4, journey.JourneyId, true);
        Assert.False(skipped.Success);
        Assert.Equal(4, (await service.ReadAsync()).Revision);
        var completed = await service.CompleteCurrentStepAsync(4, journey.JourneyId, false);
        Assert.True(completed.Success);
        Assert.NotNull(completed.Value!.CompletedAt);
        Assert.Null(completed.Value.CurrentStepId);
    }

    [Fact]
    public async Task Sharing_private_dependency_is_blocked_without_publishing_landmark()
    {
        var service = new MapsJourneyService(new MemorySettings());
        var placeId = Guid.NewGuid();
        Assert.True((await service.CreateLandmarkAsync(0, placeId, "Home", new(54, -1))).Success);
        var result = await service.SaveJourneyAsync(1, Journey(placeId) with { Visibility = MapObjectVisibility.Shared });
        Assert.False(result.Success);
        Assert.Equal("PrivateDependency", result.ErrorCode);
        var state = await service.ReadAsync();
        Assert.Empty(state.Journeys);
        Assert.Equal(MapObjectVisibility.Private, state.Places.Single().Visibility);
    }

    [Fact]
    public async Task Editing_active_journey_does_not_silently_remove_user_intent()
    {
        var service = new MapsJourneyService(new MemorySettings());
        var placeId = Guid.NewGuid();
        await service.CreateLandmarkAsync(0, placeId, "Bench", new(54, -1));
        var saved = await service.SaveJourneyAsync(1, Journey(placeId));
        await service.StartNavigationAsync(2, saved.Value!.JourneyId);
        var update = await service.SaveJourneyAsync(3, saved.Value with { Name = "Renamed walk" }, expectedJourneyRevision: 1);
        Assert.True(update.Success);
        var advance = await service.CompleteCurrentStepAsync(4, saved.Value.JourneyId, false);
        Assert.False(advance.Success);
        Assert.Equal("RevisionConflict", advance.ErrorCode);
        Assert.Equal(4, (await service.ReadAsync()).Revision);
    }

    [Fact]
    public void User_defined_path_is_explicit_and_invalid_coordinate_is_rejected()
    {
        var journey = Journey(Guid.NewGuid());
        var step = journey.Steps[0] with { UserDefinedPath = [new(54, -1), new(54.01, -1.01)] };
        Assert.True(step.IsUserDefinedPath);
        MapJourneyLogic.Validate(journey with { Steps = [step, journey.Steps[1]] });
        Assert.Throws<InvalidDataException>(() => MapJourneyLogic.Validate(journey with
        { Steps = [step with { Coordinate = new(double.NaN, -1) }] }));
    }

    private static MapSavedJourney Journey(Guid placeId)
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), "Walk to college", [new(Guid.NewGuid(), MapJourneyStepKind.Travel, "Walk to bench", PlaceId: placeId),
            new(Guid.NewGuid(), MapJourneyStepKind.Wait, "Sit for ten minutes", Duration: TimeSpan.FromMinutes(10))], MapObjectVisibility.Private, now, now, 0);
    }

    private sealed class MemorySettings : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        private readonly Dictionary<string, string> _values = new();
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class =>
            Task.FromResult(_values.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : null);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class
        { _values[key] = JsonSerializer.Serialize(value); return Task.CompletedTask; }
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson, string? replacementJson, CancellationToken token)
        {
            lock (_values)
            {
                _values.TryGetValue(key, out var current);
                if (current != expectedJson) return Task.FromResult(new SettingsCompareExchangeResult(false, current, 0));
                if (replacementJson is null) _values.Remove(key); else _values[key] = replacementJson;
                return Task.FromResult(new SettingsCompareExchangeResult(true, replacementJson, 0));
            }
        }
        public Task RemoveAsync(string key, CancellationToken token) { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => Task.FromResult(new SettingsExportManifest { Settings = new(_values) });
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => throw new NotSupportedException();
    }
}
