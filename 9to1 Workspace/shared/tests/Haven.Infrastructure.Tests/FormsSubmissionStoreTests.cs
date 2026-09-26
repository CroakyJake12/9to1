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
    public async Task Identical_normalised_retry_across_store_restart_does_not_change_workbook_revision_or_bytes()
    {
        var repository = new DataWorkbookRepository(new TestPaths(_dataDirectory));
        var submittedAt = DateTimeOffset.UtcNow;
        var initial = new FormsSubmission(" response-retry ", " feedback ", " Feedback ",
            new Dictionary<string, string> { ["message"] = " Original " }, submittedAt);
        await new DataWorkbookFormsSubmissionStore(repository).SaveAsync(initial, CancellationToken.None);
        var path = Path.Combine(_dataDirectory, "Data", "Workbooks", DataWorkbookAppLinks.FormsResponses.ToString("D"), "current.json");
        var originalBytes = await File.ReadAllBytesAsync(path);
        var originalVersion = Assert.IsType<DataWorkbook>(await repository.LoadAsync(DataWorkbookAppLinks.FormsResponses, CancellationToken.None)).Version;

        var retried = new FormsSubmission("response-retry", "feedback", "Feedback",
            new Dictionary<string, string> { [" message "] = "Original" }, submittedAt);
        await new DataWorkbookFormsSubmissionStore(repository).SaveAsync(retried, CancellationToken.None);

        var responses = await new DataWorkbookFormsSubmissionStore(repository).GetLatestAsync(CancellationToken.None);
        var response = Assert.Single(responses);
        Assert.Equal("response-retry", response.Id);
        Assert.Equal("Original", response.Values["message"]);
        Assert.Equal(originalVersion, Assert.IsType<DataWorkbook>(await repository.LoadAsync(DataWorkbookAppLinks.FormsResponses, CancellationToken.None)).Version);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Divergent_reuse_of_response_id_returns_stable_conflict_without_changing_workbook()
    {
        var repository = new DataWorkbookRepository(new TestPaths(_dataDirectory));
        var initial = new FormsSubmission("response-conflict", "feedback", "Feedback",
            new Dictionary<string, string> { ["message"] = "Original" }, DateTimeOffset.UtcNow);
        await new DataWorkbookFormsSubmissionStore(repository).SaveAsync(initial, CancellationToken.None);
        var path = Path.Combine(_dataDirectory, "Data", "Workbooks", DataWorkbookAppLinks.FormsResponses.ToString("D"), "current.json");
        var originalBytes = await File.ReadAllBytesAsync(path);
        var before = Assert.IsType<DataWorkbook>(await repository.LoadAsync(DataWorkbookAppLinks.FormsResponses, CancellationToken.None));
        var beforeRow = before.Sheets[0].Cells.Where(cell => cell.Row == 1).OrderBy(cell => cell.Column).Select(cell => (cell.Column, cell.Value)).ToArray();

        var conflict = await Assert.ThrowsAsync<FormsSubmissionConflictException>(() =>
            new DataWorkbookFormsSubmissionStore(repository).SaveAsync(initial with
            {
                Values = new Dictionary<string, string> { ["message"] = "Different result" }
            }, CancellationToken.None));

        Assert.Equal("FormsResponseIdConflict", conflict.Code);
        Assert.Equal("response-conflict", conflict.ResponseId);
        Assert.False(conflict.CanRetry);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        var after = Assert.IsType<DataWorkbook>(await repository.LoadAsync(DataWorkbookAppLinks.FormsResponses, CancellationToken.None));
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(beforeRow, after.Sheets[0].Cells.Where(cell => cell.Row == 1).OrderBy(cell => cell.Column).Select(cell => (cell.Column, cell.Value)).ToArray());
        Assert.Equal("Original", Assert.Single(await new DataWorkbookFormsSubmissionStore(repository).GetLatestAsync(CancellationToken.None)).Values["message"]);
    }

    [Fact]
    public async Task Concurrent_store_instances_do_not_overwrite_each_others_responses()
    {
        var repository = new DataWorkbookRepository(new TestPaths(_dataDirectory));
        var stores = Enumerable.Range(0, 8).Select(_ => new DataWorkbookFormsSubmissionStore(repository)).ToArray();
        var saves = stores.Select((store, index) => store.SaveAsync(
            new FormsSubmission($"response-{index}", "feedback", "Feedback",
                new Dictionary<string, string> { ["message"] = $"Message {index}" }, DateTimeOffset.UtcNow.AddSeconds(index)),
            CancellationToken.None));

        await Task.WhenAll(saves);

        var reopened = await new DataWorkbookFormsSubmissionStore(repository).GetLatestAsync(CancellationToken.None);
        Assert.Equal(8, reopened.Count);
        Assert.Equal(Enumerable.Range(0, 8).Select(index => $"response-{index}").Order(), reopened.Select(item => item.Id).Order());
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
    public async Task Forms_typed_answers_keep_values_references_version_and_provenance_in_the_Data_workbook()
    {
        var repository = new DataWorkbookRepository(new TestPaths(_dataDirectory));
        var store = new DataWorkbookFormsSubmissionStore(repository);
        var workbookId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var submittedAt = DateTimeOffset.UtcNow;
        var submission = new FormsSubmission("response-typed", "registration", "Registration",
            new Dictionary<string, string> { ["name"] = "Ada" }, submittedAt)
        {
            FormVersionId = "registration:3",
            StartedAt = submittedAt.AddMinutes(-4),
            DataBindingId = "binding-registration",
            DataTargetWorkbookId = workbookId,
            DataTargetTableId = tableId,
            DataWriteStatus = FormsDataWriteStatus.Pending,
            Revision = 3,
            Answers = new Dictionary<string, FormsAnswer>(StringComparer.Ordinal)
            {
                ["age"] = new("age", "integer", JsonSerializer.SerializeToElement(17)),
                ["household"] = new("household", "repeating-records", JsonSerializer.SerializeToElement(new[]
                {
                    new { memberId = "member-1", name = "Grace", guardian = new { recordId = "guardian-7" } }
                })),
                ["school"] = new("school", "reference", JsonSerializer.SerializeToElement("school-42"), workbookId, tableId, "schoolId", "school-42")
            }
        };

        await store.SaveAsync(submission, CancellationToken.None);

        var stored = Assert.Single(await new DataWorkbookFormsSubmissionStore(repository).GetLatestAsync(CancellationToken.None));
        Assert.Equal("registration:3", stored.FormVersionId);
        Assert.Equal(submittedAt.AddMinutes(-4), stored.StartedAt);
        Assert.Equal(FormsDataWriteStatus.Pending, stored.DataWriteStatus);
        Assert.Equal(3, stored.Revision);
        Assert.Equal(workbookId, stored.DataTargetWorkbookId);
        Assert.Equal(tableId, stored.DataTargetTableId);
        Assert.Equal(JsonValueKind.Number, stored.Answers["age"].Value.ValueKind);
        Assert.Equal(17, stored.Answers["age"].Value.GetInt32());
        Assert.Equal("repeating-records", stored.Answers["household"].ValueType);
        Assert.Equal("member-1", stored.Answers["household"].Value[0].GetProperty("memberId").GetString());
        Assert.Equal("school-42", stored.Answers["school"].DataRecordId);
    }

    [Fact]
    public async Task Legacy_forms_response_rows_migrate_without_overwriting_user_columns()
    {
        var repository = new DataWorkbookRepository(new TestPaths(_dataDirectory));
        var workbook = DataWorkbook.Create("Forms responses");
        workbook.Id = DataWorkbookAppLinks.FormsResponses;
        workbook.Sheets[0].Name = "Responses";
        workbook.Metadata["haven.app"] = "forms";
        var sheet = workbook.Sheets[0];
        var legacyHeaders = new[] { "Response ID", "Form ID", "Form Title", "Submitted At (UTC)", "Values (JSON)", "My Notes" };
        for (var column = 0; column < legacyHeaders.Length; column++)
            sheet.SetCell(0, column, legacyHeaders[column]);
        sheet.SetCell(1, 0, "response-legacy");
        sheet.SetCell(1, 1, "feedback");
        sheet.SetCell(1, 2, "Feedback");
        sheet.SetCell(1, 3, DateTimeOffset.UtcNow.ToString("O"));
        sheet.SetCell(1, 4, "{\"message\":\"legacy answer\"}");
        sheet.SetCell(1, 5, "keep this Data-owned column");
        await repository.SaveAsync(workbook, "legacy Forms fixture", CancellationToken.None);

        var stored = Assert.Single(await new DataWorkbookFormsSubmissionStore(repository).GetLatestAsync(CancellationToken.None));

        Assert.Equal("legacy-unversioned", stored.FormVersionId);
        Assert.Equal("legacy answer", stored.Values["message"]);
        Assert.Equal(JsonValueKind.String, stored.Answers["message"].Value.ValueKind);
        var migrated = Assert.IsType<DataWorkbook>(await repository.LoadAsync(DataWorkbookAppLinks.FormsResponses, CancellationToken.None));
        Assert.Equal("2", migrated.Metadata["haven.forms.responseSchemaVersion"]);
        Assert.Equal("My Notes", migrated.Sheets[0].GetCell(0, 5)?.Value);
        Assert.Equal("keep this Data-owned column", migrated.Sheets[0].GetCell(1, 5)?.Value);
        Assert.Contains(migrated.Sheets[0].Cells, cell => cell.Row == 0 && cell.Value == "Form Version ID");
    }

    [Fact]
    public async Task Response_history_is_not_evicted_and_keyset_pages_have_no_duplicates()
    {
        const int responseCount = 503;
        var repository = new DataWorkbookRepository(new TestPaths(_dataDirectory));
        var workbook = DataWorkbook.Create("Forms responses");
        workbook.Id = DataWorkbookAppLinks.FormsResponses;
        workbook.Sheets[0].Name = "Responses";
        workbook.Metadata["haven.app"] = "forms";
        workbook.Metadata["haven.forms.responseSchemaVersion"] = "2";
        var sheet = workbook.Sheets[0];
        var headers = new[]
        {
            "Response ID", "Form ID", "Form Title", "Submitted At (UTC)", "Values (JSON)",
            "Form Version ID", "Started At (UTC)", "Typed Answers (JSON)", "Data Binding ID",
            "Data Target Workbook ID", "Data Target Table ID", "Data Write Status", "Revision"
        };
        for (var column = 0; column < headers.Length; column++) sheet.SetCell(0, column, headers[column]);
        var submittedAt = DateTimeOffset.UtcNow;
        for (var index = 0; index < responseCount; index++)
        {
            var row = index + 1;
            sheet.SetCell(row, 0, $"response-{index:D4}");
            sheet.SetCell(row, 1, "feedback");
            sheet.SetCell(row, 2, "Feedback");
            sheet.SetCell(row, 3, submittedAt.ToString("O"));
            sheet.SetCell(row, 4, "{}");
            sheet.SetCell(row, 5, "feedback:1");
            sheet.SetCell(row, 11, FormsDataWriteStatus.NotBound.ToString());
            sheet.SetCell(row, 12, "1");
        }
        workbook.Tables.Add(new DataTableDefinition
        {
            Name = "FormsResponses",
            SheetId = sheet.Id,
            Range = new DataCellRange { StartRow = 0, StartColumn = 0, EndRow = responseCount, EndColumn = headers.Length - 1 }
        });
        await repository.SaveAsync(workbook, "paged Forms fixture", CancellationToken.None);

        var store = new DataWorkbookFormsSubmissionStore(repository);
        var all = await store.GetLatestAsync(CancellationToken.None);
        var paged = new List<FormsSubmission>();
        FormsSubmissionCursor? cursor = null;
        do
        {
            var page = await store.GetPageAsync(new FormsSubmissionPageRequest(100, cursor), CancellationToken.None);
            paged.AddRange(page.Items);
            cursor = page.Next;
        } while (cursor is not null);

        Assert.Equal(responseCount, all.Count);
        Assert.Equal(responseCount, paged.Count);
        Assert.Equal(responseCount, paged.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("response-0000", paged[0].Id);
        Assert.Equal($"response-{responseCount - 1:D4}", paged[^1].Id);
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
