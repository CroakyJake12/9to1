using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class FormsSubmissionStoreTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "haven-app-data-donor-" + Guid.NewGuid().ToString("N"));

    private sealed class TestPaths(string dataDirectory) : IAppPaths
    {
        public string DataDirectory { get; } = dataDirectory;
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }

    [Fact]
    public async Task Forms_responses_round_trip_in_the_Data_app_workbook_with_backup_recovery()
    {
        var paths = new TestPaths(_dataDirectory);
        var repository = new DataWorkbookRepository(paths);
        var store = new DataWorkbookFormsSubmissionStore(repository);
        var first = new FormsSubmission("response-1", "feedback", "Feedback",
            new Dictionary<string, string> { ["name"] = "Ada", ["message"] = "First" }, DateTimeOffset.UtcNow.AddMinutes(-1));
        var second = new FormsSubmission("response-2", "feedback", "Feedback",
            new Dictionary<string, string> { ["name"] = "Grace", ["message"] = "Second" }, DateTimeOffset.UtcNow);

        await store.SaveAsync(first, CancellationToken.None);
        await store.SaveAsync(second, CancellationToken.None);

        var reloaded = await new DataWorkbookFormsSubmissionStore(repository).GetLatestAsync(CancellationToken.None);
        Assert.Equal(["response-2", "response-1"], reloaded.Select(item => item.Id).ToArray());
        Assert.Equal("Grace", reloaded[0].Values["name"]);
        Assert.Equal(DataWorkbookAppLinks.FormsResponses, store.DataWorkbookId);
        var workbook = Assert.IsType<DataWorkbook>(await repository.LoadAsync(store.DataWorkbookId, CancellationToken.None));
        Assert.Equal("forms", workbook.Metadata["haven.app"]);
        Assert.Equal("Response ID", workbook.Sheets[0].GetCell(0, 0)?.Value);
        Assert.Equal("FormsResponses", Assert.Single(workbook.Tables).Name);
        Assert.True(File.Exists(Path.Combine(_dataDirectory, "Data", "Workbooks", store.DataWorkbookId.ToString("D"), "previous.json")));
    }

    [Fact]
    public async Task Forms_response_fields_are_bounded_and_normalised_before_workbook_storage()
    {
        var repository = new DataWorkbookRepository(new TestPaths(_dataDirectory));
        var store = new DataWorkbookFormsSubmissionStore(repository);
        var submission = new FormsSubmission(
            "  bounded  ", " feedback ", new string('t', FormsSubmissionLogic.MaxFormTitleLength + 20),
            new Dictionary<string, string>
            {
                [" name "] = "  Ada  ",
                ["message"] = new string('x', FormsSubmissionLogic.MaxTextLength + 20)
            },
            DateTimeOffset.UtcNow);

        await store.SaveAsync(submission, CancellationToken.None);

        var stored = Assert.Single(await store.GetLatestAsync(CancellationToken.None));
        Assert.Equal("bounded", stored.Id);
        Assert.Equal("feedback", stored.FormId);
        Assert.Equal(FormsSubmissionLogic.MaxFormTitleLength, stored.FormTitle.Length);
        Assert.Equal("Ada", stored.Values["name"]);
        Assert.Equal(FormsSubmissionLogic.MaxTextLength, stored.Values["message"].Length);
    }

    [Fact]
    public async Task Maps_migrates_legacy_places_to_the_Data_workbook_and_keeps_recent_history_local()
    {
        var paths = new TestPaths(_dataDirectory);
        var repository = new DataWorkbookRepository(paths);
        var legacyDirectory = Path.Combine(_dataDirectory, "maps");
        Directory.CreateDirectory(legacyDirectory);
        var legacyFile = Path.Combine(legacyDirectory, MapsSavedPlaceStore.StoreFileName);
        var legacyPlace = new SavedMapPlace("place-1", "Old Town", "Visit", new GeoPoint(51.5, -0.12), DateTimeOffset.UtcNow);
        var legacy = new MapsSavedPlaceDocument([legacyPlace], ["London", "Bristol"]);
        await File.WriteAllTextAsync(legacyFile, JsonSerializer.Serialize(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var store = new MapsSavedPlaceStore(paths, repository);

        var places = await store.GetSavedPlacesAsync(CancellationToken.None);
        var recent = await store.GetRecentSearchesAsync(CancellationToken.None);

        Assert.Equal("place-1", Assert.Single(places).Id);
        Assert.Equal(["London", "Bristol"], recent);
        Assert.Equal(DataWorkbookAppLinks.MapsSavedPlaces, store.DataWorkbookId);
        Assert.False(File.Exists(legacyFile));
        Assert.True(File.Exists(legacyFile + ".migrated"));
        Assert.True(File.Exists(Path.Combine(legacyDirectory, "recent-searches.json")));
        var workbook = Assert.IsType<DataWorkbook>(await repository.LoadAsync(store.DataWorkbookId, CancellationToken.None));
        Assert.Equal("maps", workbook.Metadata["haven.app"]);
        Assert.Equal("Old Town", workbook.Sheets[0].GetCell(1, 1)?.Value);
        Assert.Equal("MapsSavedPlaces", Assert.Single(workbook.Tables).Name);
    }

    [Fact]
    public async Task Maps_updates_keep_user_added_Data_workbook_columns_intact()
    {
        var paths = new TestPaths(_dataDirectory);
        var repository = new DataWorkbookRepository(paths);
        var store = new MapsSavedPlaceStore(paths, repository);
        var original = new SavedMapPlace("place-2", "Harbour", null, new GeoPoint(50.8, -1.1), DateTimeOffset.UtcNow);
        await store.SaveAsync(original, CancellationToken.None);

        var workbook = Assert.IsType<DataWorkbook>(await repository.LoadAsync(store.DataWorkbookId, CancellationToken.None));
        workbook.Sheets[0].SetCell(1, 6, "my category");
        await repository.SaveAsync(workbook, "Data app user column", CancellationToken.None);
        await store.SaveAsync(original with { Note = "Parking nearby", SavedAt = DateTimeOffset.UtcNow.AddMinutes(1) }, CancellationToken.None);

        workbook = Assert.IsType<DataWorkbook>(await repository.LoadAsync(store.DataWorkbookId, CancellationToken.None));
        Assert.Equal("my category", workbook.Sheets[0].GetCell(1, 6)?.Value);
        Assert.Equal("Parking nearby", workbook.Sheets[0].GetCell(1, 2)?.Value);
        Assert.Single(await store.GetSavedPlacesAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
