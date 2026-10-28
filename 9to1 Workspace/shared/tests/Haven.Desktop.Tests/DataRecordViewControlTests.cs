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
