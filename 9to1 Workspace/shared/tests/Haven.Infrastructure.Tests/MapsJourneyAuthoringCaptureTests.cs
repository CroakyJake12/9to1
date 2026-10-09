using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class MapsJourneyAuthoringCaptureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lying_step_or_path_count_stops_at_first_excess_value_before_actual_storage(bool path)
    {
        using var paths = new Paths(); var settings = new VersionedAtomicSettingsStore(paths);
        await settings.GetStoreIdentityAsync(default);
        var file = Path.Combine(paths.DataDirectory, "settings.json"); var before = await File.ReadAllBytesAsync(file);
        var (journey, steps, points) = Draft();
        var suppliedSteps = new LyingJourneyList<MapJourneyStep>(steps[0]);
        var suppliedPath = new LyingJourneyList<GeoPoint>(points[0]);
        journey = journey with { Steps = path ? new[] { steps[0] with { UserDefinedPath = suppliedPath } } : suppliedSteps };
        var result = await new MapsJourneyService(settings).SaveJourneyAsync(0, journey);
        Assert.False(result.Success); Assert.Equal("InvalidData", result.ErrorCode);
        Assert.Equal(2, path ? suppliedPath.Consumed : suppliedSteps.Consumed);
        Assert.Equal(before, await File.ReadAllBytesAsync(file));
        Assert.Empty((await new MapsJourneyService(new VersionedAtomicSettingsStore(paths)).ReadAsync()).Journeys);
    }
    private sealed class LyingJourneyList<T>(T value) : IReadOnlyList<T>
    {
        public int Consumed { get; private set; }
        public int Count => 1;
        public T this[int index] => throw new NotSupportedException("Caller indexing unavailable.");
        public IEnumerator<T> GetEnumerator()
        { for (var index = 0; index < 1_000_000; index++) { Consumed++; yield return value; } }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public async Task Held_save_freezes_ordered_authored_intent_and_user_path_before_actual_storage_await()
    {
        using var paths = new Paths(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); var token = timeout.Token;
        var (journey, steps, path) = Draft(); var held = new HeldStore(new VersionedAtomicSettingsStore(paths));
        var pending = new MapsJourneyService(held).SaveJourneyAsync(0, journey, token: token);
        await held.Entered.Task.WaitAsync(token);
        steps[0] = steps[0] with { Instruction = "Changed after call", Intent = MapStepIntent.Optional };
        steps.Add(new(Guid.NewGuid(), MapJourneyStepKind.ManualInstruction, "Unreviewed additional action"));
        path[0] = new(20, 30); held.Release.TrySetResult();
        var saved = await pending; Assert.True(saved.Success, saved.Message);
        var actual = Assert.Single((await new MapsJourneyService(new VersionedAtomicSettingsStore(paths)).ReadAsync(token)).Journeys);
        Assert.Equal(journey.JourneyId, actual.JourneyId); Assert.Equal(2, actual.Steps.Count);
        Assert.Equal("Take the campus shortcut", actual.Steps[0].Instruction); Assert.Equal(MapStepIntent.Required, actual.Steps[0].Intent);
        Assert.Equal(new GeoPoint(54, -1), actual.Steps[0].UserDefinedPath![0]); Assert.True(actual.Steps[0].IsUserDefinedPath);
        Assert.Equal(MapJourneyStepKind.Wait, actual.Steps[1].Kind); Assert.Equal(TimeSpan.FromMinutes(10), actual.Steps[1].Duration);
        Assert.Equal(JsonSerializer.Serialize(saved.Value), JsonSerializer.Serialize(actual));
    }

    [Fact]
    public async Task Later_caller_edits_do_not_change_returned_saved_revision_or_reopened_route()
    {
        using var paths = new Paths(); var (journey, steps, path) = Draft();
        var service = new MapsJourneyService(new VersionedAtomicSettingsStore(paths));
        var saved = await service.SaveJourneyAsync(0, journey); Assert.True(saved.Success); var before = JsonSerializer.Serialize(saved.Value);
        steps.Reverse(); path.Clear(); steps.Add(new(Guid.NewGuid(), MapJourneyStepKind.Activity, "Later draft only"));
        Assert.Equal(before, JsonSerializer.Serialize(saved.Value));
        var actual = Assert.Single((await new MapsJourneyService(new VersionedAtomicSettingsStore(paths)).ReadAsync()).Journeys);
        Assert.Equal(before, JsonSerializer.Serialize(actual)); Assert.Equal(1, actual.Revision);
    }

    [Fact]
    public async Task Prepared_old_edit_cannot_overwrite_an_independent_actual_saved_revision()
    {
        using var paths = new Paths(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); var token = timeout.Token;
        var (journey, _, _) = Draft(); var peer = new MapsJourneyService(new VersionedAtomicSettingsStore(paths));
        var original = await peer.SaveJourneyAsync(0, journey, token: token); Assert.True(original.Success);
        var held = new HeldStore(new VersionedAtomicSettingsStore(paths)); var waiting = new MapsJourneyService(held);
        var pending = waiting.SaveJourneyAsync(1, original.Value! with { Name = "Prepared old edit" }, 1, token);
        await held.Entered.Task.WaitAsync(token);
        var winning = await peer.SaveJourneyAsync(1, original.Value! with { Name = "Peer current edit" }, 1, token); Assert.True(winning.Success);
        var before = JsonSerializer.Serialize(await peer.ReadAsync(token)); held.Release.TrySetResult();
        var denied = await pending; Assert.False(denied.Success); Assert.Equal("RevisionConflict", denied.ErrorCode);
        var actual = await new MapsJourneyService(new VersionedAtomicSettingsStore(paths)).ReadAsync(token);
        Assert.Equal(before, JsonSerializer.Serialize(actual)); Assert.Equal(2, actual.Revision); Assert.Equal(2, Assert.Single(actual.Journeys).Revision);
    }

    [Fact]
    public async Task Invalid_coordinates_return_structured_failure_without_serializing_or_replacing_saved_route()
    {
        using var paths = new Paths(); var (journey, _, _) = Draft(); var service = new MapsJourneyService(new VersionedAtomicSettingsStore(paths));
        var saved = await service.SaveJourneyAsync(0, journey); Assert.True(saved.Success); var before = JsonSerializer.Serialize(await service.ReadAsync());
        var invalid = saved.Value! with { Steps = [saved.Value!.Steps[0] with { Coordinate = new(double.NaN, -1) }, saved.Value.Steps[1]] };
        var denied = await service.SaveJourneyAsync(1, invalid, 1); Assert.False(denied.Success); Assert.Equal("InvalidData", denied.ErrorCode);
        Assert.Equal(before, JsonSerializer.Serialize(await new MapsJourneyService(new VersionedAtomicSettingsStore(paths)).ReadAsync()));
    }

    private static (MapSavedJourney Journey, List<MapJourneyStep> Steps, List<GeoPoint> Path) Draft()
    {
        var path = new List<GeoPoint> { new(54, -1), new(54.01, -1.01) };
        var steps = new List<MapJourneyStep> {
            new(Guid.NewGuid(), MapJourneyStepKind.Travel, "Take the campus shortcut", Coordinate: new(54.01, -1.01), TravelProfile: MapTravelProfile.Walking, UserDefinedPath: path),
            new(Guid.NewGuid(), MapJourneyStepKind.Wait, "Wait ten minutes", Duration: TimeSpan.FromMinutes(10)) };
        return (new(Guid.NewGuid(), "Walk to college", steps, MapObjectVisibility.Private, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0), steps, path);
    }
    private sealed class HeldStore(VersionedAtomicSettingsStore inner) : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => inner.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => inner.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => inner.RemoveAsync(key, token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => inner.ImportAsync(manifest, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson, string? replacementJson, CancellationToken token) => inner.CompareExchangeAsync(key, expectedJson, replacementJson, token);
        public async Task<SettingsExportManifest> ExportAsync(CancellationToken token)
        { var snapshot = await inner.ExportAsync(token); Entered.TrySetResult(); await Release.Task.WaitAsync(token); return snapshot; }
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("astra-maps-draft-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
