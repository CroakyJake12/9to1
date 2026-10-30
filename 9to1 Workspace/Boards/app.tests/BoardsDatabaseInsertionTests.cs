using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsDatabaseInsertionTests
{
    [Theory]
    [InlineData("above", "database")]
    [InlineData("below", "database-tabs")]
    [InlineData("replace", "database-timeline")]
    public Task Relative_database_is_one_Board_edit_and_preserves_the_actual_Data_artifact(string placement, string command) =>
        Run(async (adapter, vm, source) =>
        {
            var before = adapter.Snapshot;
            OnUi(() => vm.InsertKindRelativeToAsync(command, "anchor", placement));
            Assert.Equal(1, source.Creates);
            Assert.False(vm.HasPendingDatabaseLink);
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            var page = saved.Sections[0].Pages[0];
            var inserted = Assert.Single(page.Blocks, block => block.Kind == HavenRichBlockKind.Database);
            Assert.Equal(placement == "below" ? 2 : 1, page.Blocks.IndexOf(inserted));
            Assert.Equal(placement != "replace", page.Blocks.Any(block => block.Id == "anchor"));
            var reference = Assert.IsType<HavenBoardDatabaseReference>(inserted.Database);
            var data = Assert.IsType<DataRecordSession>(source.Created);
            var view = data.Snapshot.RecordViews.Single(view => view.ViewID == reference.ViewID);
            Assert.Equal(command == "database-timeline" ? DataRecordViewKind.Timeline : DataRecordViewKind.Table, view.Kind);
            var table = data.Snapshot.Tables.Single();
            var field = table.RelationalSchema!.Fields.Single(field => field.Name == "Name").FieldID;
            await data.CreateRecordAsync(table.Id, new Dictionary<Guid, DataScalarRecordValue>
                { [field] = new(DataCellKind.Text, JsonSerializer.SerializeToElement("Canonical Data record")) });
            var record = Assert.Single(data.Snapshot.Tables.Single().Records).RecordID;
            var committed = adapter.Snapshot;
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            Assert.True(await adapter.RedoAsync()); Assert.Equal(committed, adapter.Snapshot);
            var reopened = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            Assert.Equal(reference, reopened.Sections[0].Pages[0].Blocks.Single(block => block.Id == inserted.Id).Database);
            await using var coldData = await source.OpenDatabaseAsync(reference.WorkbookID);
            Assert.Equal(reference.StoreID, coldData.Reference.StoreID);
            Assert.Equal(record, Assert.Single(coldData.Snapshot.Tables.Single().Records).RecordID);
            Assert.DoesNotContain("Canonical Data record", await File.ReadAllTextAsync(adapter.FilePath!));
        });

    [Theory]
    [InlineData("above", false, "one,INSERT,two,three")]
    [InlineData("below", false, "one,two,INSERT,three")]
    [InlineData("replace", false, "one,INSERT,three")]
    [InlineData("above", true, "INSERT,one,two,three")]
    [InlineData("below", true, "one,two,three,INSERT")]
    [InlineData("replace", true, "INSERT")]
    public Task Database_list_destination_keeps_items_and_one_exact_undo(string placement, bool whole, string order) =>
        Run(async (adapter, vm, source) =>
        {
            var before = adapter.Snapshot;
            OnUi(() => vm.InsertKindRelativeToAsync("database", whole ? "anchor" : "anchor:two", placement));
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            var blocks = saved.Sections[0].Pages[0].Blocks.Where(block => block.Id is not ("first" or "last")).ToArray();
            var database = Assert.Single(blocks, block => block.Kind == HavenRichBlockKind.Database);
            Assert.Equal(order.Split(','), blocks.SelectMany(block => block == database ? new[] { "INSERT" } : block.Items.Select(item => item.Id)));
            Assert.All(blocks.Where(block => block != database), block => Assert.Equal("quote", block.StyleId));
            Assert.All(blocks.SelectMany(block => block.Items), item => { Assert.True(item.Checked); Assert.True(item.Bold); });
            var after = adapter.Snapshot;
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            Assert.True(await adapter.RedoAsync()); Assert.Equal(after, adapter.Snapshot); Assert.Equal(1, source.Creates);
        }, rich =>
        {
            var list = rich.Sections[0].Pages[0].Blocks[1]; list.Kind = HavenRichBlockKind.Checklist; list.StyleId = "quote";
            list.Items = new[] { "one", "two", "three" }.Select(id => new HavenRichListItem { Id = id, Text = id, Checked = true, Bold = true }).ToList();
        });

    [Theory]
    [InlineData("above")]
    [InlineData("below")]
    [InlineData("replace")]
    public Task Positioned_database_only_replaces_the_whole_block_without_changing_geometry(string placement) =>
        Run(async (adapter, vm, source) =>
        {
            var before = adapter.Snapshot;
            OnUi(() => vm.InsertKindRelativeToAsync("database", "anchor", placement));
            if (placement != "replace")
            { Assert.Equal(0, source.Creates); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo); return; }
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            var block = Assert.Single(saved.Sections[0].Pages[0].Blocks, block => block.Kind == HavenRichBlockKind.Database);
            Assert.Equal(HavenRichBlockLayouts.Absolute, block.Layout);
            Assert.Equal((21d, 43d, 430d, 280d, 5), (block.X, block.Y, block.Width, block.Height, block.ZOrder));
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
        }, rich =>
        {
            var block = rich.Sections[0].Pages[0].Blocks[1]; block.Layout = HavenRichBlockLayouts.Absolute;
            block.X = 21; block.Y = 43; block.Width = 430; block.Height = 280; block.ZOrder = 5;
        });

    [Theory]
    [InlineData("page-away-and-back")]
    [InlineData("view-away-and-back")]
    [InlineData("dispose")]
    public Task Late_creation_keeps_Data_without_touching_a_retired_origin_and_retry_never_recreates(string transition) =>
        Run(async (adapter, vm, source) =>
        {
            var before = adapter.Snapshot;
            source.BeforeReturn = async () => await Dispatcher.UIThread.InvokeAsync<Task>(async () =>
            {
                Assert.Null(await vm.PrepareDatabaseCloseAsync(CancellationToken.None));
                await vm.InsertKindRelativeToAsync("database", "last", "below");
                Assert.Equal(1, source.Creates); // concurrent action refused before Data creation
                if (transition == "page-away-and-back")
                { var page = vm.SelectedPageId; vm.SelectPageById("other"); vm.SelectPageById(page); }
                else if (transition == "view-away-and-back")
                { await vm.DispatchAsync("ToggleEditView", null); await vm.DispatchAsync("ToggleEditView", null); }
                else vm.Dispose();
            }).GetTask().Unwrap();
            OnUi(() => vm.InsertKindRelativeToAsync("database", "anchor", "replace"));
            Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo); Assert.True(vm.HasPendingDatabaseLink);
            await using var retained = await source.OpenDatabaseAsync(source.Created!.Reference.WorkbookID);
            Assert.NotEmpty(retained.Snapshot.RecordViews);
            if (transition == "dispose") return;
            OnUi(() => vm.InsertKindAsync("database-retry"));
            Assert.False(vm.HasPendingDatabaseLink); Assert.Equal(1, source.Creates);
            Assert.Equal(source.Created.Reference.WorkbookID, vm.PageBlocks[1].Database!.WorkbookID);
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
        }, rich => rich.Sections[0].Pages.Add(new HavenRichPage { Id = "other", Title = "Other" }));

    [Fact]
    public Task Changed_origin_requires_an_explicit_new_destination_and_reuses_the_created_artifact() =>
        Run(async (adapter, vm, source) =>
        {
            source.BeforeReturn = async () => await Dispatcher.UIThread.InvokeAsync(() => vm.PageBlocks[1].Text = "Newer text");
            OnUi(() => vm.InsertKindRelativeToAsync("database", "anchor", "replace"));
            Assert.True(vm.HasPendingDatabaseLink); Assert.Equal("Newer text", vm.PageBlocks[1].Text);
            OnUi(() => vm.InsertKindAsync("database-retry"));
            Assert.True(vm.HasPendingDatabaseLink); Assert.DoesNotContain(vm.PageBlocks, block => block.Database is not null);
            await adapter.SaveAsync(); var before = adapter.Snapshot;
            OnUi(() => vm.InsertKindRelativeToAsync("database-retry", "last", "above"));
            Assert.False(vm.HasPendingDatabaseLink); Assert.Equal(1, source.Creates);
            Assert.Equal("Newer text", vm.PageBlocks[1].Text); Assert.NotNull(vm.PageBlocks[2].Database); Assert.Equal("last", vm.PageBlocks[3].Id);
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Creation_failure_or_cancellation_preserves_content_and_allows_a_fresh_attempt(bool cancelled) =>
        Run(async (adapter, vm, source) =>
        {
            var before = adapter.Snapshot;
            source.CreationFailure = cancelled ? new OperationCanceledException("cancelled") : new IOException("store unavailable");
            OnUi(() => vm.InsertKindRelativeToAsync("database", "anchor", "replace"));
            Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo); Assert.False(vm.HasPendingDatabaseLink);
            Assert.Null(source.Created);
            source.CreationFailure = null;
            OnUi(() => vm.InsertKindRelativeToAsync("database", "anchor", "replace"));
            Assert.NotNull(vm.PageBlocks[1].Database); Assert.Equal(2, source.Creates);
            await adapter.SaveAsync();
        });

    [Fact]
    public async Task Failed_Board_save_retries_the_same_link_without_another_artifact_or_history_edit()
    {
        using var source = new Source(); var saveAllowed = false; byte[]? saved = null;
        await using var adapter = await ContractSessionAdapter.OpenExternallyWritableAsync(HavenBoardDocumentCodec.CreateNew(),
            (bytes, _) => { if (!saveAllowed) throw new IOException("Source write refused"); saved = bytes.ToArray(); return ValueTask.FromResult(true); });
        var vm = TestUiThread.Run(() => new BoardsViewModel(adapter, source));
        try
        {
            var before = adapter.Snapshot;
            OnUi(() => vm.InsertKindRelativeToAsync("database", vm.PageBlocks[0].Id, "replace"));
            Assert.True(vm.HasPendingDatabaseLink); var block = Assert.Single(vm.PageBlocks); var after = adapter.Snapshot;
            Assert.NotNull(block.Database); Assert.Contains("Source write refused", vm.Get("StatusText")?.ToString());
            OnUi(async () => Assert.Null(await vm.PrepareDatabaseCloseAsync(CancellationToken.None)));
            OnUi(() => vm.DispatchAsync("Undo", null).AsTask()); Assert.Equal(after, adapter.Snapshot);
            saveAllowed = true;
            OnUi(() => vm.InsertKindAsync("database-retry"));
            Assert.False(vm.HasPendingDatabaseLink); Assert.Equal(1, source.Creates); Assert.Equal(block.Id, Assert.Single(vm.PageBlocks).Id);
            Assert.Equal(block.Database, HavenBoardDocumentCodec.Deserialize(saved!).RichNotes!.Sections[0].Pages[0].Blocks[0].Database);
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            Assert.True(await adapter.RedoAsync()); Assert.Equal(after, adapter.Snapshot);
        }
        finally { OnUi(() => vm.DisposeDatabasesAsync().AsTask()); TestUiThread.Run(vm.Dispose); }
    }

    [Fact]
    public Task Relative_view_choice_creates_a_new_database_and_toolbar_view_choice_still_changes_the_existing_view() =>
        Run(async (adapter, vm, source) =>
        {
            OnUi(() => vm.InsertKindRelativeToAsync("database", "anchor", "replace"));
            var original = vm.PageBlocks[1].Database!; var id = vm.PageBlocks[1].Id;
            OnUi(() => vm.InsertKindAsync("database-timeline", id));
            Assert.Equal(1, source.Creates); Assert.Equal(original.WorkbookID, vm.PageBlocks[1].Database!.WorkbookID);
            Assert.NotEqual(original.ViewID, vm.PageBlocks[1].Database!.ViewID);
            var selected = vm.PageBlocks[1].Database;
            await adapter.SaveAsync();
            OnUi(() => vm.InsertKindRelativeToAsync("database-tabs", id, "below"));
            Assert.Equal(2, source.Creates); Assert.Equal(selected, vm.PageBlocks[1].Database);
            Assert.NotEqual(original.WorkbookID, vm.PageBlocks[2].Database!.WorkbookID);
        });

    internal static Task Run(Func<ContractSessionAdapter, BoardsViewModel, Source, Task> action,
        Action<HavenRichNotes>? seed = null) => BoardsRelativeInsertionTests.Run(async (adapter, _) =>
        {
            using var source = new Source();
            var vm = TestUiThread.Run(() => new BoardsViewModel(adapter, source));
            try { await action(adapter, vm, source); }
            finally
            {
                OnUi(() => vm.DisposeDatabasesAsync().AsTask());
                TestUiThread.Run(vm.Dispose);
            }
        }, seed);

    [Fact]
    public async Task Failed_save_can_retry_after_selecting_another_view_of_the_same_database()
    {
        using var source = new Source(); var saveAllowed = false;
        await using var adapter = await ContractSessionAdapter.OpenExternallyWritableAsync(HavenBoardDocumentCodec.CreateNew(),
            (_, _) => saveAllowed ? ValueTask.FromResult(true) : throw new IOException("Refused"));
        var vm = TestUiThread.Run(() => new BoardsViewModel(adapter, source));
        try
        {
            OnUi(() => vm.InsertKindRelativeToAsync("database", vm.PageBlocks[0].Id, "replace"));
            Assert.True(vm.HasPendingDatabaseLink); var block = Assert.Single(vm.PageBlocks); var original = block.Database!;
            OnUi(() => vm.InsertKindAsync("database-timeline", block.Id));
            var selected = Assert.Single(vm.PageBlocks).Database!;
            Assert.Equal(original.WorkbookID, selected.WorkbookID); Assert.NotEqual(original.ViewID, selected.ViewID);
            saveAllowed = true; OnUi(() => vm.InsertKindAsync("database-retry"));
            Assert.False(vm.HasPendingDatabaseLink); Assert.Equal(selected, Assert.Single(vm.PageBlocks).Database);
            Assert.Equal(1, source.Creates);
        }
        finally { OnUi(() => vm.DisposeDatabasesAsync().AsTask()); TestUiThread.Run(vm.Dispose); }
    }

    [Fact]
    public Task Hidden_pane_retained_Data_view_cannot_change_its_Board_reference() => Run(async (adapter, vm, source) =>
    {
        var tabber = vm.PageBlocks.First(block => block.Kind == "tabber");
        var selectedPane = tabber.Tabber!.Panes.Single(pane => pane.Id == tabber.Tabber.SelectedPaneId);
        var child = TestUiThread.Run(() => vm.CreatePaneModel(selectedPane.PageId));
        try
        {
            OnUi(() => child.InsertKindRelativeToAsync("database", child.PageBlocks[0].Id, "replace"));
            var block = Assert.Single(child.PageBlocks); var reference = block.Database!;
            var nextView = source.Created!.Snapshot.RecordViews.First(view => view.ViewID != reference.ViewID).ViewID;
            OnUi(() => vm.ChangeTabberAsync(tabber.Id, "select", tabber.Tabber.Panes.First(pane => pane.Id != selectedPane.Id).Id));
            var before = adapter.Snapshot;
            TestUiThread.Run(() => child.UpdateDatabaseReference(child.SelectedPageId, block.Id, reference with { ViewID = nextView }));
            await adapter.SaveAsync(); Assert.Equal(before, adapter.Snapshot);
        }
        finally { OnUi(() => child.DisposeDatabasesAsync().AsTask()); TestUiThread.Run(child.Dispose); }
    }, BoardsTabberInsertionTests.SeedTabbers);

    internal static void OnUi(Func<Task> action) => BoardsTabberInsertionTests.OnUi(action);

    // Only the host/profile actor is supplied by the fixture. Creation, authority,
    // durable Data repository, session, record operations and reopen are production.
    internal sealed class Source : IBoardsEmbeddedArtifactHost, IBoardsDatabaseSource, IDisposable
    {
        private readonly Paths _paths = new();
        private readonly DataLocalRecordSessionFactory _factory;
        internal int Creates;
        internal DataRecordSession? Created;
        internal Func<Task>? BeforeReturn { get; set; }
        internal Exception? CreationFailure { get; set; }
        public Source() => _factory = new(new DataWorkbookRepository(_paths), new Actors());
        public async Task<DataRecordSession> CreateDatabaseAsync(string title, CancellationToken token = default)
        {
            Creates++;
            if (CreationFailure is not null) throw CreationFailure;
            Created = await _factory.CreateAsync("cycle8-test", title, token);
            if (BeforeReturn is not null) await BeforeReturn();
            return Created;
        }
        public Task<DataRecordSession> OpenDatabaseAsync(Guid id, CancellationToken token = default) =>
            _factory.OpenAsync("cycle8-test", id, token);
        public void BindOwningControl(Control control) { }
        public Task<IBoardsEmbeddedArtifactSession> OpenAsync(BoardEmbeddedArtifactReference reference,
            BoardEmbeddedArtifactMode mode, CancellationToken token = default) => throw new NotSupportedException();
        public Task<BoardEmbeddedArtifactReference?> SelectAsync(string applicationID, CancellationToken token = default) => throw new NotSupportedException();
        public Task<BoardEmbeddedArtifactReference?> CreateAsync(string applicationID, CancellationToken token = default) => throw new NotSupportedException();
        public void Dispose() => _paths.Dispose();
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) =>
            ValueTask.FromResult<AuthenticatedResourceActor?>(new("local:cycle8", "cycle8-test", null, null, "test-session"));
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "boards-cycle8-data-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
