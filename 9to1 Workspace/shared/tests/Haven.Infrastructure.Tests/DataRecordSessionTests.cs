using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class DataRecordSessionTests
{
    [Fact]
    public async Task Same_canonical_record_survives_views_undo_and_cold_reopen_and_stale_writer_refuses()
    {
        using var paths = new Paths(); var actors = new Actors();
        var repository = new DataWorkbookRepository(paths);
        var authority = await DataLocalWorkbookAuthority.BindAsync(repository, actors, actors.Current.ProfileId, default);
        await using var first = new DataRecordSession(repository, authority, actors);
        var newDatabase = DataRecordSession.NewRecordDatabase("Projects");
        await first.CreateAsync(newDatabase);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.SaveAsync(first.Snapshot, "unguarded", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.DeleteAsync(newDatabase.Id, default));
        var table = first.Snapshot.Tables.Single(); var name = table.RelationalSchema!.Fields.Single(f => f.Name == "Name").FieldID;
        var status = table.RelationalSchema.Fields.Single(f => f.Name == "Status").FieldID;
        await first.CreateRecordAsync(table.Id, new Dictionary<Guid, DataScalarRecordValue>
        { [name] = Text("One"), [status] = Text("To do") });
        var record = first.Snapshot.Tables.Single().Records.Single().RecordID;
        var views = first.Snapshot.RecordViews;
        Assert.All(views, v => Assert.Equal(record, Assert.Single(first.Project(v.ViewID).Rows).RecordID));
        await using var stale = new DataRecordSession(repository, authority, actors);
        await stale.OpenAsync(first.Reference);
        await first.MoveCardAsync(views.Single(v => v.Kind == DataRecordViewKind.Kanban).ViewID, record, "Doing");
        await Assert.ThrowsAsync<InvalidOperationException>(() => stale.UpdateRecordAsync(table.Id, record,
            new Dictionary<Guid, DataScalarRecordValue> { [name] = Text("stale overwrite") }));
        await first.UndoAsync(); Assert.Equal("To do", DataTableIdentity.ReadCell(first.Snapshot, table.Id, record, status)!.Value);
        await first.RedoAsync(); Assert.Equal("Doing", DataTableIdentity.ReadCell(first.Snapshot, table.Id, record, status)!.Value);
        var timeline = views.Single(v => v.Kind == DataRecordViewKind.Timeline);
        await first.SetTimelineRangeAsync(timeline.ViewID, record, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));
        var coldRepository = new DataWorkbookRepository(paths);
        var coldAuthority = await DataLocalWorkbookAuthority.BindAsync(coldRepository, actors, actors.Current.ProfileId, default);
        await using var cold = new DataRecordSession(coldRepository, coldAuthority, actors);
        await cold.OpenAsync(await coldAuthority.ResolveLocalAsync(newDatabase.Id));
        Assert.Equal(record, cold.Snapshot.Tables.Single().Records.Single().RecordID);
        Assert.Equal("Doing", DataTableIdentity.ReadCell(cold.Snapshot, table.Id, record, status)!.Value);
        Assert.Equal(3, cold.Snapshot.RecordViews.Count);
        Assert.NotNull(Assert.Single(cold.Project(timeline.ViewID).Rows).Start);
    }

    [Fact]
    public async Task Filter_and_sort_are_saved_views_not_removed_or_copied_records_and_profile_change_refuses()
    {
        using var paths = new Paths(); var actors = new Actors(); var repository = new DataWorkbookRepository(paths);
        var authority = await DataLocalWorkbookAuthority.BindAsync(repository, actors, actors.Current.ProfileId, default);
        await using var session = new DataRecordSession(repository, authority, actors);
        await session.CreateAsync(DataRecordSession.NewRecordDatabase("Shared"));
        var table = session.Snapshot.Tables.Single(); var field = table.RelationalSchema!.Fields.Single(f => f.Name == "Name").FieldID;
        await session.CreateRecordAsync(table.Id, new Dictionary<Guid, DataScalarRecordValue> { [field] = Text("Beta") });
        await session.CreateRecordAsync(table.Id, new Dictionary<Guid, DataScalarRecordValue> { [field] = Text("Alpha") });
        var view = session.Snapshot.RecordViews.Single(v => v.Kind == DataRecordViewKind.Table);
        var filtered = view with { Filters = [new(field, DataRecordFilterOperator.Contains, "Alpha")], Sorts = [new(field)] };
        await session.SetViewsAsync(session.Snapshot.RecordViews.Select(v => v.ViewID == view.ViewID ? filtered : v).ToArray());
        Assert.Single(session.Project(view.ViewID).Rows); Assert.Equal(2, session.Snapshot.Tables.Single().Records.Count);
        var before = session.Reference;
        actors.Current = actors.Current with { ProfileId = "another-profile" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.CreateRecordAsync(table.Id,
            new Dictionary<Guid, DataScalarRecordValue> { [field] = Text("Denied") }));
        Assert.Equal(before, session.Reference);
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("   ", true, null)]
    [InlineData("not-a-date", true, "Invalid end date")]
    [InlineData("", false, "Invalid end date")]
    public void Timeline_optional_empty_end_is_open_ended_but_malformed_or_required_end_is_reported(
        string endText, bool nullable, string? expectedIssue)
    {
        var workbook = DataRecordSession.NewRecordDatabase("Timeline projection");
        var table = workbook.Tables.Single();
        var start = table.RelationalSchema!.Fields.Single(field => field.Name == "Start").FieldID;
        var end = table.RelationalSchema.Fields.Single(field => field.Name == "End").FieldID;
        var prepared = DataRecordCreation.Prepare(workbook, table.Id, Guid.NewGuid(), workbook.Version, workbook.RevisionId,
            new Dictionary<Guid, DataScalarRecordValue>
            { [start] = new(DataCellKind.Date, JsonSerializer.SerializeToElement("2026-11-01")) });
        workbook = Assert.IsType<DataWorkbook>(prepared.Workbook);
        table = workbook.Tables.Single();
        table.RelationalSchema = table.RelationalSchema! with
        { Fields = table.RelationalSchema.Fields.Select(field => field.FieldID == end ? field with { Nullable = nullable } : field).ToArray() };
        var row = table.Records.Single(); var column = table.Fields.Single(field => field.FieldID == end).SheetColumn;
        workbook.Sheets.Single(sheet => sheet.Id == table.SheetId).SetCell(row.SheetRow, column, endText, kind: DataCellKind.Date);
        var view = workbook.RecordViews.Single(view => view.Kind == DataRecordViewKind.Timeline);
        var projected = Assert.Single(DataRecordViews.Project(workbook, view.ViewID).Rows);
        Assert.NotNull(projected.Start); Assert.Null(projected.End);
        Assert.Equal(expectedIssue, projected.TimelineIssue);
    }

    [Fact]
    public async Task Populated_legacy_root_cannot_be_silently_claimed_by_a_profile()
    {
        using var paths = new Paths(); var actors = new Actors(); var repository = new DataWorkbookRepository(paths);
        await repository.SaveAsync(DataWorkbook.Create("Existing"), "legacy fixture", default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => DataLocalWorkbookAuthority.BindAsync(repository, actors, actors.Current.ProfileId, default));
    }
    [Fact]
    public async Task Backup_only_legacy_workbook_can_be_recovered_and_saved_without_losing_previous_revision()
    {
        using var paths = new Paths(); var repository = new DataWorkbookRepository(paths);
        var original = DataWorkbook.Create("Original backup");
        var first = await repository.SaveAsync(original, "First", default);
        original.Title = "Current before removal";
        var second = await repository.SaveAsync(original, "Second", default);
        File.Delete(second.CurrentPath);
        var recovered = (await repository.LoadAsync(original.Id, default))!;
        Assert.True(recovered.Recovery.RecoveredFromBackup);
        Assert.Equal("Original backup", recovered.Title);
        recovered.Title = "Recovered and continued";
        var receipt = await repository.SaveAsync(recovered, "Recover", default);
        Assert.Equal(first.Version + 1, receipt.Version);
        Assert.Equal("Recovered and continued", (await repository.LoadAsync(original.Id, default))!.Title);
        Assert.True(File.Exists(second.BackupPath));
    }

    [Fact]
    public async Task Guarded_record_session_reopens_and_edits_a_backup_only_artifact()
    {
        using var paths = new Paths(); var actors = new Actors(); var repository = new DataWorkbookRepository(paths);
        var factory = new DataLocalRecordSessionFactory(repository, actors);
        Guid id, tableId, recordId, fieldId;
        await using (var original = await factory.CreateAsync(actors.Current.ProfileId, "Backup recovery"))
        {
            id = original.Reference.WorkbookID;
            var table = original.Snapshot.Tables.Single(); tableId = table.Id;
            fieldId = table.RelationalSchema!.Fields.Single(f => f.Name == "Name").FieldID;
            await original.CreateRecordAsync(tableId, new Dictionary<Guid, DataScalarRecordValue> { [fieldId] = Text("Saved in backup") });
            recordId = original.Snapshot.Tables.Single().Records.Single().RecordID;
            await original.UpdateRecordAsync(tableId, recordId, new Dictionary<Guid, DataScalarRecordValue> { [fieldId] = Text("Removed current") });
        }
        var directory = Path.Combine(paths.DataDirectory, "Data", "Workbooks", id.ToString("D"));
        File.Delete(Path.Combine(directory, "current.json"));
        await using var recovered = await factory.OpenAsync(actors.Current.ProfileId, id);
        Assert.Equal("Saved in backup", DataTableIdentity.ReadCell(recovered.Snapshot, tableId, recordId, fieldId)!.Value);
        await recovered.UpdateRecordAsync(tableId, recordId, new Dictionary<Guid, DataScalarRecordValue> { [fieldId] = Text("Continued after recovery") });
        await using var reopened = await factory.OpenAsync(actors.Current.ProfileId, id);
        Assert.Equal("Continued after recovery", DataTableIdentity.ReadCell(reopened.Snapshot, tableId, recordId, fieldId)!.Value);
        Assert.True(File.Exists(Path.Combine(directory, "previous.json")));
    }

    [Fact]
    public async Task Recovery_admission_pins_the_backup_bytes_as_well_as_the_primary()
    {
        using var paths = new Paths(); var actors = new Actors(); var repository = new DataWorkbookRepository(paths);
        var authority = await DataLocalWorkbookAuthority.BindAsync(repository, actors, actors.Current.ProfileId, default);
        await using var session = new DataRecordSession(repository, authority, actors);
        await session.CreateAsync(DataRecordSession.NewRecordDatabase("First"));
        var table = session.Snapshot.Tables.Single();
        var field = table.RelationalSchema!.Fields.Single(f => f.Name == "Name").FieldID;
        await session.CreateRecordAsync(table.Id, new Dictionary<Guid, DataScalarRecordValue> { [field] = Text("Current") });
        var directory = Path.Combine(paths.DataDirectory, "Data", "Workbooks", session.Reference.WorkbookID.ToString("D"));
        File.Delete(Path.Combine(directory, "current.json"));
        var reference = await authority.ResolveLocalAsync(session.Reference.WorkbookID);
        var admission = await authority.CaptureAsync(reference.StoreID, reference.WorkbookID, reference.Version,
            reference.RevisionID, "data.workbooks.read", actors.Current, default);
        Assert.NotNull(admission);
        var backup = (await repository.LoadAsync(reference.WorkbookID, default))!;
        backup.Title = "Changed after admission";
        await File.WriteAllTextAsync(Path.Combine(directory, "previous.json"), JsonSerializer.Serialize(backup, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.False(await admission!.CheckAsync(new(reference.StoreID, reference.WorkbookID, reference.Version,
            reference.RevisionID, DataWorkbookCommitPhase.Publication), default));
    }

    private static DataScalarRecordValue Text(string value) => new(DataCellKind.Text, JsonSerializer.SerializeToElement(value));
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        internal AuthenticatedResourceActor Current = new("local:owner", "profile-a", null, null, "current-session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<AuthenticatedResourceActor?>(Current); }
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "data-record-session-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
