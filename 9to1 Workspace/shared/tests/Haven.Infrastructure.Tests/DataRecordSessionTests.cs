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

    [Fact]
    public async Task Populated_legacy_root_cannot_be_silently_claimed_by_a_profile()
    {
        using var paths = new Paths(); var actors = new Actors(); var repository = new DataWorkbookRepository(paths);
        await repository.SaveAsync(DataWorkbook.Create("Existing"), "legacy fixture", default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => DataLocalWorkbookAuthority.BindAsync(repository, actors, actors.Current.ProfileId, default));
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
