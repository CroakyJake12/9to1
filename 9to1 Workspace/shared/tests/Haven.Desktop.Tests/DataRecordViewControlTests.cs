using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Data;
using HavenOS.Apps.Data.RecordsUI;

namespace Haven.Desktop.Tests;

public sealed class DataRecordViewControlTests
{
    [AvaloniaFact]
    public async Task Authored_view_tabs_render_the_same_saved_record_in_Table_Board_and_Timeline()
    {
        using var paths = new ViewPaths(); var actors = new ViewActors();
        var repository = new Haven.Infrastructure.DataWorkbookRepository(paths);
        var factory = new Haven.Infrastructure.DataLocalRecordSessionFactory(repository, actors);
        await using var session = await factory.CreateAsync("profile-a", "View fixture");
        var table = session.Snapshot.Tables.Single(); var name = table.RelationalSchema!.Fields.Single(f => f.Name == "Name").FieldID;
        await session.CreateRecordAsync(table.Id, new Dictionary<Guid, DataScalarRecordValue>
        { [name] = new(DataCellKind.Text, System.Text.Json.JsonSerializer.SerializeToElement("Retained record")) });
        var record = session.Snapshot.Tables.Single().Records.Single().RecordID;
        var view = new DataRecordViewControl(session);
        var window = new Avalonia.Controls.Window { Content = view, Width = 1100, Height = 700 };
        try
        {
            window.Show(); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            foreach (var definition in session.Snapshot.RecordViews)
            {
                var tab = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.Button>()
                    .Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == definition.Name);
                tab.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await view.WhenIdleAsync(); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(definition.ViewID, view.SelectedViewID);
                Assert.Equal(record, Assert.Single(session.Project(definition.ViewID).Rows).RecordID);
                Assert.Contains(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.TextBox>(),
                    input => input.Text == "Retained record");
            }
        }
        finally { await view.DisposeAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task Explicit_discard_restores_saved_values_without_mutating_records()
    {
        using var paths = new ViewPaths(); var actors = new ViewActors();
        var factory = new Haven.Infrastructure.DataLocalRecordSessionFactory(new Haven.Infrastructure.DataWorkbookRepository(paths), actors);
        await using var session = await factory.CreateAsync("profile-a", "Discard fixture");
        var table = session.Snapshot.Tables.Single();
        var field = table.RelationalSchema!.Fields.Single(value => value.Name == "Name").FieldID;
        await session.CreateRecordAsync(table.Id, new Dictionary<Guid, DataScalarRecordValue>
        { [field] = new(DataCellKind.Text, System.Text.Json.JsonSerializer.SerializeToElement("Saved value")) });
        var view = new DataRecordViewControl(session);
        var window = new Avalonia.Controls.Window { Content = view, Width = 1100, Height = 700 };
        try
        {
            window.Show(); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var input = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.TextBox>()
                .Single(box => box.Text == "Saved value");
            input.Text = "Unsaved change";
            Assert.True(view.HasUnsavedDrafts);
            var discard = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.Button>()
                .Single(button => button.Content?.ToString() == "Discard field changes");
            discard.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await view.WhenIdleAsync();
            Assert.False(view.HasUnsavedDrafts);
            Assert.Equal("Saved value", DataTableIdentity.ReadCell(session.Snapshot, table.Id,
                session.Snapshot.Tables.Single().Records.Single().RecordID, field)!.Value);
        }
        finally { await view.DisposeAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Native_record_headers_and_inputs_follow_their_own_theme_scope()
    {
        using var paths = new ViewPaths(); var actors = new ViewActors();
        var factory = new Haven.Infrastructure.DataLocalRecordSessionFactory(new Haven.Infrastructure.DataWorkbookRepository(paths), actors);
        await using var session = await factory.CreateAsync("profile-a", "Theme fixture");
        var table = session.Snapshot.Tables.Single();
        var field = table.RelationalSchema!.Fields.Single(value => value.Name == "Name").FieldID;
        await session.CreateRecordAsync(table.Id, new Dictionary<Guid, DataScalarRecordValue>
        { [field] = new(DataCellKind.Text, System.Text.Json.JsonSerializer.SerializeToElement("Theme record")) });
        foreach (var appearance in new[] { CakeOS.Cui.Themes.CuiAppearance.Dark, CakeOS.Cui.Themes.CuiAppearance.Bright })
        {
            using var theme = new CakeOS.Cui.Themes.CuiThemePreferenceContext(new ContrastThemeSource(appearance));
            var view = new DataRecordViewControl(session, theme: theme);
            var window = new Avalonia.Controls.Window { Content = view, Width = 1100, Height = 700 };
            try
            {
                window.Show(); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var header = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.TextBlock>()
                    .Single(label => label.Text == "Name" && label.FontWeight == Avalonia.Media.FontWeight.Bold);
                var input = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.TextBox>()
                    .Single(box => box.Text == "Theme record");
                var expected = CakeOS.Cui.Themes.CuiSurfacePaletteCatalog.For("data", appearance,
                    CakeOS.Cui.Themes.CuiTheme.Professional).Text;
                Assert.Equal(expected, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(header.Foreground).Color);
                Assert.Equal(expected, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(input.Foreground).Color);
            }
            finally { await view.DisposeAsync(); window.Close(); }
        }
    }
    private sealed class ContrastThemeSource(CakeOS.Cui.Themes.CuiAppearance appearance) : CakeOS.Cui.Themes.ICuiThemePreferenceSource
    {
        public CakeOS.Cui.Themes.CuiThemePreferenceSnapshot Current { get; } = new("Professional", 1, "data-theme-fixture", false,
            CakeOS.Cui.Themes.CuiAccessibilitySettings.Default, appearance);
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class ViewActors : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) =>
            ValueTask.FromResult<AuthenticatedResourceActor?>(new("local:owner", "profile-a", null, null, "session"));
    }
    private sealed class ViewPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "data-view-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }

    [AvaloniaFact]
    public async Task View_only_records_keep_tabs_available_but_do_not_admit_mutations()
    {
        using var paths = new ViewPaths(); var actors = new ViewActors();
        var factory = new Haven.Infrastructure.DataLocalRecordSessionFactory(new Haven.Infrastructure.DataWorkbookRepository(paths), actors);
        await using var session = await factory.CreateAsync("profile-a", "Read only");
        var view = new DataRecordViewControl(session) { IsReadOnly = true };
        var window = new Avalonia.Controls.Window { Content = view, Width = 1000, Height = 700 };
        try
        {
            window.Show(); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(view.IsEnabled);
            var timeline = session.Snapshot.RecordViews.Single(v => v.Kind == DataRecordViewKind.Timeline);
            view.SelectView(timeline.ViewID); Assert.Equal(timeline.ViewID, view.SelectedViewID);
            var before = session.Reference;
            await view.DispatchAsync("AddRecord", null);
            Assert.Equal(before, session.Reference);
            Assert.Empty(session.Snapshot.Tables.Single().Records);
        }
        finally { await view.DisposeAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Terminal_failed_database_open_can_retry_with_original_diagnostic_retained()
    {
        await using var board = new CakeOS.Apps.Boards.App.InMemoryRichBoardSession();
        await board.OpenAsync("memory://retry-database");
        var model = new CakeOS.Apps.Boards.App.BoardsViewModel(board);
        var source = new FailingDatabaseSource();
        await using var blocks = new CakeOS.Apps.Boards.App.BoardsDatabaseBlocks(model, source);
        var reference = new CakeOS.Apps.Boards.Contract.HavenBoardDatabaseReference(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var block = new CakeOS.Apps.Boards.App.RichBoardBlock { Id = "database-retry", Kind = "database", Database = reference };
        board.Document.Sections[0].Pages[0].Blocks.Add(block);
        var mounted = blocks.Mount(block); var window = new Avalonia.Controls.Window { Content = mounted, Width = 700, Height = 500 };
        try
        {
            window.Show(); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); await blocks.WhenIdleAsync();
            Assert.Equal(1, source.Calls);
            var retry = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(mounted).OfType<Avalonia.Controls.Button>()
                .Single(button => Equals(button.Content, "Retry opening database"));
            retry.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await blocks.WhenIdleAsync(); Assert.Equal(2, source.Calls);
            Assert.Contains(blocks.OpenDiagnostics, error => error.ToString().Contains("first read", StringComparison.Ordinal));
        }
        finally { window.Close(); model.Dispose(); }
    }
    private sealed class FailingDatabaseSource : CakeOS.Apps.Boards.App.IBoardsDatabaseSource
    {
        internal int Calls;
        public Task<DataRecordSession> CreateDatabaseAsync(string title, CancellationToken token = default) => throw new NotSupportedException();
        public Task<DataRecordSession> OpenDatabaseAsync(Guid workbook, CancellationToken token = default)
        { Calls++; return Task.FromException<DataRecordSession>(new IOException(Calls == 1 ? "first read" : "second read")); }
    }

    [AvaloniaFact]
    public async Task Pending_created_database_link_vetoes_close_without_losing_the_reference()
    {
        await using var board = new CakeOS.Apps.Boards.App.InMemoryRichBoardSession();
        await board.OpenAsync("memory://pending-database-link");
        var model = new CakeOS.Apps.Boards.App.BoardsViewModel(board);
        var reference = new CakeOS.Apps.Boards.Contract.HavenBoardDatabaseReference(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var field = typeof(CakeOS.Apps.Boards.App.BoardsViewModel).GetField("_pendingDatabase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        field.SetValue(model, reference);
        var task = (Task)typeof(CakeOS.Apps.Boards.App.BoardsViewModel).GetMethod("PrepareDatabaseCloseAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(model, [CancellationToken.None])!;
        await task;
        Assert.Null(task.GetType().GetProperty("Result")!.GetValue(task));
        Assert.Same(reference, field.GetValue(model));
        Assert.Contains("Link created database", model.Get("StatusText")?.ToString() ?? string.Empty, StringComparison.Ordinal);
        model.Dispose();
    }

    [Fact]
    public void Spreadsheet_undo_clone_preserves_record_field_and_schema_identities()
    {
        var workbook = DataRecordSession.NewRecordDatabase("Identity");
        var table = workbook.Tables.Single();
        var clone = (DataTableDefinition)typeof(DataPage).GetMethod("CloneTableDefinition",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [table])!;
        Assert.Equal(table.RecordIdentityVersion, clone.RecordIdentityVersion);
        Assert.Equal(table.Fields, clone.Fields);
        Assert.Equal(table.Records, clone.Records);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(table.RelationalSchema),
            System.Text.Json.JsonSerializer.Serialize(clone.RelationalSchema));
        Assert.NotSame(table.Fields, clone.Fields);
    }
}
