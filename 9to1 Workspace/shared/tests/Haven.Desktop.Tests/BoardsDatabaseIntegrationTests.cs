using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.App;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Data.RecordsUI;

namespace Haven.Desktop.Tests;

public sealed class BoardsDatabaseIntegrationTests
{
    [AvaloniaFact]
    public async Task Board_database_tabs_share_records_and_saved_reference_and_delete_undo_keeps_the_Data_artifact()
    {
        using var paths = new Paths();
        var factory = new DataLocalRecordSessionFactory(new DataWorkbookRepository(paths), new Actors());
        var source = new Source(factory);
        using var store = new JsonFileHavenBoardStore(Path.Combine(paths.DataDirectory, "Boards"));
        var boardPath = Path.Combine(paths.DataDirectory, "board.9to1board");
        await using (var seed = await RichBoardSession.CreateNewAsync(store, "Board")) await seed.SaveAsAsync(boardPath);
        await using var board = await ContractSessionAdapter.OpenAsync(store, boardPath);
        var vm = new BoardsViewModel(board, source);
        await vm.InsertKindAsync("database");
        Assert.Equal(1, source.CreateCount);
        var block = Assert.Single(vm.PageBlocks);
        var reference = Assert.IsType<HavenBoardDatabaseReference>(block.Database);
        var data = Assert.IsType<DataRecordSession>(source.Created);
        var table = data.Snapshot.Tables.Single();
        var field = table.RelationalSchema!.Fields.Single(item => item.Name == "Name").FieldID;
        await data.CreateRecordAsync(table.Id, new Dictionary<Guid, DataScalarRecordValue>
        { [field] = new(DataCellKind.Text, JsonSerializer.SerializeToElement("Only in canonical Data")) });
        var rowID = data.Snapshot.Tables.Single().Records.Single().RecordID;
        var definitions = JsonSerializer.Serialize(data.Snapshot.RecordViews);
        var host = new StackPanel();
        BlockRenderer.Rebuild(host, vm, []);
        var window = new Window { Content = host, Width = 1100, Height = 700 };
        try
        {
            window.Show(); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await vm.DatabaseBlocks.WhenIdleAsync();
            var view = Assert.Single(window.GetVisualDescendants().OfType<DataRecordViewControl>());
            foreach (var definition in data.Snapshot.RecordViews)
            {
                var tab = view.GetVisualDescendants().OfType<Button>()
                    .Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == definition.Name);
                tab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await view.WhenIdleAsync();
                Assert.Equal(definition.ViewID, view.SelectedViewID);
                Assert.Equal(definition.ViewID, Assert.Single(vm.PageBlocks).Database!.ViewID);
                Assert.Equal(rowID, Assert.Single(data.Project(definition.ViewID).Rows).RecordID);
            }
            Assert.Equal(definitions, JsonSerializer.Serialize(data.Snapshot.RecordViews));
            await board.SaveAsync();
            var savedReference = Assert.Single(vm.PageBlocks).Database!;
            await using (var cold = await ContractSessionAdapter.OpenAsync(store, boardPath))
                Assert.Equal(savedReference, Assert.Single(cold.Document.Sections[0].Pages[0].Blocks).Database);
            Assert.DoesNotContain("Only in canonical Data", await File.ReadAllTextAsync(boardPath));
            vm.FocusBlock(block.Id);
            await vm.ConvertSelectedKindAsync("paragraph");
            Assert.Equal("database", Assert.Single(vm.PageBlocks).Kind);
            await vm.DeleteBlockAsync(block.Id);
            Assert.Empty(vm.PageBlocks);
            Assert.True(await board.UndoAsync());
            Assert.Equal(savedReference, Assert.Single(board.Document.Sections[0].Pages[0].Blocks).Database);
            Assert.Equal(rowID, Assert.Single(data.Snapshot.Tables.Single().Records).RecordID);
        }
        finally { await vm.DatabaseBlocks.DisposeAsync(); window.Close(); }
    }

    private sealed class Source(DataLocalRecordSessionFactory factory) : IBoardsEmbeddedArtifactHost, IBoardsDatabaseSource
    {
        internal int CreateCount;
        internal DataRecordSession? Created;
        public async Task<DataRecordSession> CreateDatabaseAsync(string title, CancellationToken token = default)
        { CreateCount++; return Created = await factory.CreateAsync("profile", title, token); }
        public Task<DataRecordSession> OpenDatabaseAsync(Guid workbookID, CancellationToken token = default) => factory.OpenAsync("profile", workbookID, token);
        public void BindOwningControl(Control control) { }
        public Task<IBoardsEmbeddedArtifactSession> OpenAsync(BoardEmbeddedArtifactReference reference, BoardEmbeddedArtifactMode mode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BoardEmbeddedArtifactReference?> SelectAsync(string applicationID, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BoardEmbeddedArtifactReference?> CreateAsync(string applicationID, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) =>
            ValueTask.FromResult<AuthenticatedResourceActor?>(new("local:owner", "profile", null, null, "session"));
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "boards-database-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
