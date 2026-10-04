using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

var tempRoot = Path.GetFullPath(Path.GetTempPath());
var runRoot = Path.GetFullPath(Path.Combine(tempRoot, "9to1-worker32-forms-" + Guid.NewGuid().ToString("N")));
if (!runRoot.StartsWith(tempRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("The Forms storage runtime fixture escaped the temporary directory.");
Directory.CreateDirectory(runRoot);
try
{
    await VerifyTypedResponseRoundTripAsync(Path.Combine(runRoot, "typed"));
    await VerifyLegacyMigrationAsync(Path.Combine(runRoot, "legacy"));
    await VerifyPagedHistoryAsync(Path.Combine(runRoot, "pages"));
    await VerifyCorruptTypedDataIsNotSilentlySkippedAsync(Path.Combine(runRoot, "corrupt"));
}
finally
{
    if (Directory.Exists(runRoot)) Directory.Delete(runRoot, recursive: true);
}

Console.WriteLine("Forms typed storage, migration, history paging and corruption checks passed.");

static async Task VerifyTypedResponseRoundTripAsync(string directory)
{
    var repository = new DataWorkbookRepository(new TestPaths(directory));
    var store = new DataWorkbookFormsSubmissionStore(repository);
    var targetWorkbookId = Guid.NewGuid();
    var targetTableId = Guid.NewGuid();
    var submittedAt = DateTimeOffset.UtcNow;
    var submission = new FormsSubmission("response-typed", "registration", "Registration",
        new Dictionary<string, string> { ["name"] = "Ada" }, submittedAt)
    {
        FormVersionId = "registration:3",
        StartedAt = submittedAt.AddMinutes(-4),
        DataBindingId = "binding-registration",
        DataTargetWorkbookId = targetWorkbookId,
        DataTargetTableId = targetTableId,
        DataWriteStatus = FormsDataWriteStatus.Pending,
        Revision = 3,
        Answers = new Dictionary<string, FormsAnswer>(StringComparer.Ordinal)
        {
            ["age"] = new("age", "integer", JsonSerializer.SerializeToElement(17)),
            ["household"] = new("household", "repeating-records", JsonSerializer.SerializeToElement(new[]
            {
                new { memberId = "member-1", name = "Grace", guardian = new { recordId = "guardian-7" } }
            })),
            ["school"] = new("school", "reference", JsonSerializer.SerializeToElement("school-42"), targetWorkbookId, targetTableId, "schoolId", "school-42")
        }
    };

    await store.SaveAsync(submission, CancellationToken.None);
    await store.SaveAsync(submission, CancellationToken.None);
    var stored = Single(await new DataWorkbookFormsSubmissionStore(repository).GetLatestAsync(CancellationToken.None));
    Require(stored.FormVersionId == "registration:3", "Form version was not preserved.");
    Require(stored.StartedAt == submittedAt.AddMinutes(-4), "Start time was not preserved.");
    Require(stored.DataWriteStatus == FormsDataWriteStatus.Pending && stored.Revision == 3, "Data-write state or revision was not preserved.");
    Require(stored.DataTargetWorkbookId == targetWorkbookId && stored.DataTargetTableId == targetTableId, "Stable Data target identity was not preserved.");
    Require(stored.Answers["age"].Value.ValueKind == JsonValueKind.Number && stored.Answers["age"].Value.GetInt32() == 17, "Numeric answer type was lost.");
    Require(stored.Answers["household"].Value[0].GetProperty("memberId").GetString() == "member-1", "Nested repeating answer structure was lost.");
    Require(stored.Answers["school"].DataRecordId == "school-42", "Stable related-record identity was lost.");

    var workbook = Single(await repository.ListAsync(CancellationToken.None));
    Require(workbook.Version == 2, "An identical retry wrote the same response twice.");
}

static async Task VerifyLegacyMigrationAsync(string directory)
{
    var repository = new DataWorkbookRepository(new TestPaths(directory));
    var workbook = DataWorkbook.Create("Forms responses");
    workbook.Id = DataWorkbookAppLinks.FormsResponses;
    workbook.Sheets[0].Name = "Responses";
    workbook.Metadata["haven.app"] = "forms";
    var sheet = workbook.Sheets[0];
    var oldHeaders = new[] { "Response ID", "Form ID", "Form Title", "Submitted At (UTC)", "Values (JSON)", "My Notes" };
    for (var column = 0; column < oldHeaders.Length; column++) sheet.SetCell(0, column, oldHeaders[column]);
    sheet.SetCell(1, 0, "response-legacy");
    sheet.SetCell(1, 1, "feedback");
    sheet.SetCell(1, 2, "Feedback");
    sheet.SetCell(1, 3, DateTimeOffset.UtcNow.ToString("O"));
    sheet.SetCell(1, 4, "{\"message\":\"legacy answer\"}");
    sheet.SetCell(1, 5, "preserve this user value");
    await repository.SaveAsync(workbook, "legacy fixture", CancellationToken.None);

    var response = Single(await new DataWorkbookFormsSubmissionStore(repository).GetLatestAsync(CancellationToken.None));
    Require(response.FormVersionId == "legacy-unversioned", "Legacy form version absence was not represented explicitly.");
    Require(response.Answers["message"].Value.GetString() == "legacy answer", "Legacy string answer was not migrated.");
    var migrated = await repository.LoadAsync(DataWorkbookAppLinks.FormsResponses, CancellationToken.None)
        ?? throw new InvalidOperationException("Migrated response workbook disappeared.");
    Require(migrated.Metadata["haven.forms.responseSchemaVersion"] == "2", "Response schema version was not recorded.");
    Require(migrated.Sheets[0].GetCell(0, 5)?.Value == "My Notes", "Migration overwrote a Data-owned column heading.");
    Require(migrated.Sheets[0].GetCell(1, 5)?.Value == "preserve this user value", "Migration overwrote a Data-owned cell.");
}

static async Task VerifyPagedHistoryAsync(string directory)
{
    const int count = 503;
    var repository = new DataWorkbookRepository(new TestPaths(directory));
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
    for (var index = 0; index < count; index++)
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
        Range = new DataCellRange { StartRow = 0, StartColumn = 0, EndRow = count, EndColumn = headers.Length - 1 }
    });
    await repository.SaveAsync(workbook, "paged fixture", CancellationToken.None);

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

    Require(all.Count == count && paged.Count == count, "More than 500 responses were silently evicted.");
    Require(paged.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == count, "A response was duplicated across pages.");
    Require(paged[0].Id == "response-0000" && paged[^1].Id == $"response-{count - 1:D4}", "Keyset page ordering was unstable.");
}

static async Task VerifyCorruptTypedDataIsNotSilentlySkippedAsync(string directory)
{
    var repository = new DataWorkbookRepository(new TestPaths(directory));
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
    sheet.SetCell(1, 0, "response-corrupt");
    sheet.SetCell(1, 1, "feedback");
    sheet.SetCell(1, 2, "Feedback");
    sheet.SetCell(1, 3, DateTimeOffset.UtcNow.ToString("O"));
    sheet.SetCell(1, 4, "{}");
    sheet.SetCell(1, 5, "feedback:1");
    sheet.SetCell(1, 7, "not valid JSON");
    await repository.SaveAsync(workbook, "corruption fixture", CancellationToken.None);

    try
    {
        _ = await new DataWorkbookFormsSubmissionStore(repository).GetLatestAsync(CancellationToken.None);
        throw new InvalidOperationException("Corrupt typed response data was silently skipped.");
    }
    catch (InvalidDataException failure) when (failure.Message.Contains("typed answer JSON", StringComparison.Ordinal))
    {
    }
}

static FormsSubmission Single(IReadOnlyList<FormsSubmission> submissions) =>
    submissions.Count == 1 ? submissions[0] : throw new InvalidOperationException($"Expected one response; found {submissions.Count}.");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class TestPaths(string dataDirectory) : IAppPaths
{
    public string DataDirectory { get; } = dataDirectory;
}
