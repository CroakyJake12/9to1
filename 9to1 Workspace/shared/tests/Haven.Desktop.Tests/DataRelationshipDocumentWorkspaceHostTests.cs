using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Desktop.Events;
using Haven.Core;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;
using Haven.UI;
using Haven.UI.Components;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Tests;

public sealed class DataRelationshipDocumentWorkspaceHostTests
{
    [AvaloniaFact]
    public async Task Production_Data_factory_mounts_actual_relationship_owner_review_and_commits_legal_self_reference_after_exact_Home_approval()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-data-relationship-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var registrations = new ServiceCollection().AddHavenInfrastructure();
            registrations.AddSingleton<IAppPaths>(new Paths(root));
            registrations.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            registrations.AddSingleton<GenUiLiveActivityTracker>(); registrations.AddSingleton<GenUiInstanceStore>();
            await using var graph = registrations.BuildServiceProvider();
            var repository = Assert.IsType<DataWorkbookRepository>(graph.GetRequiredService<IDataWorkbookRepository>());
            Assert.IsType<DataHomeTableSchemaDesigner>(graph.GetRequiredService<IDataTableSchemaDesigner>());
            var actor = (await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token))!;
            var identity = await repository.GetStoreIdentityAsync(token);
            var authority = graph.GetRequiredService<IDataWorkbookCommitAuthority>();
            var workbook = DataWorkbook.Create("Canonical host workbook");
            var sheet = workbook.Sheets[0]; sheet.SetCell(0, 0, "Amount"); sheet.SetCell(1, 0, "1", kind: DataCellKind.Number);
            var table = new DataTableDefinition { SheetId = sheet.Id, Range = new() { EndRow = 1 } };
            DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table); workbook.Normalize();
            var key = new DataKeyDefinition(Guid.NewGuid(), "Canonical ID", DataKeyKind.Primary, [table.Fields[0].FieldID]);
            workbook = DataTableDesign.SetSchema(workbook, table.Id, 0, Guid.Empty, null,
                [new(table.Fields[0].FieldID, "ID", DataFieldType.Integer, false)], [key]).Workbook!;
            Assert.NotNull(workbook);
            Assert.Null(await authority.CaptureAsync(identity.StoreId, workbook.Id, 0, Guid.Empty, "data.workbook.create", actor, token));
            await graph.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync("data", identity.StoreId.ToString("D"), token);
            var admission = await authority.CaptureAsync(identity.StoreId, workbook.Id, 0, Guid.Empty, "data.workbook.create", actor, token);
            var saved = await repository.SaveAsync(workbook, "Create owned native host fixture", admission!, token);
            var originalBytes = await File.ReadAllBytesAsync(saved.CurrentPath, token);
            var originalVersion = workbook.Version;
            using var page = MainView.CreateDataDocumentWorkspace(new HavenEventBus(), graph);
            var window = new Window { Content = page, Width = 1300, Height = 850 };
            try
            {
                window.Show(); await page.InitializeAsync(token); window.UpdateLayout();
                var name = Assert.IsType<Input>(page.SceneRoot.DescendantsAndSelf()
                    .Single(item => item.Name == "Data.Relationship.Name"));
                name.Text = "Self reference";
                HavenButton Button(string id) => Assert.IsType<HavenButton>(page.SceneRoot.DescendantsAndSelf().Single(item => item.Name == id));
                string Status() => Assert.IsType<Text>(page.SceneRoot.DescendantsAndSelf().Single(item => item.Name == "Data.Relationship.Status")).Content;
                var permissions = graph.GetRequiredService<HomePermissionTrustService>();
                Press(Button("Data.Relationship.Review"));
                await UntilAsync(async () => (await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests.Count == 1, token);
                var request = Assert.Single((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
                Assert.Equal(actor.ActorId, request.Caller.CallerId);
                Assert.Equal(actor.ProfileId, request.Caller.Origin);
                Assert.Equal(DataRelationshipUpdateIntent.ActionID, request.Scope.ActionName);
                Press(Button("Data.Relationship.Apply"));
                await UntilAsync(() => Task.FromResult(Status().Contains("ApprovalRequired", StringComparison.Ordinal)), token);
                Assert.Equal(originalBytes, await File.ReadAllBytesAsync(saved.CurrentPath, token));
                Assert.False(page.IsDirty);
                Assert.True((await permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
                Press(Button("Data.Relationship.Apply"));
                await UntilAsync(() => Task.FromResult(page.Workbook!.Version > originalVersion), token);
                var current = (await repository.LoadAsync(workbook.Id, token))!;
                var relation = Assert.Single(current.Relationships);
                Assert.Equal("Self reference", relation.Name);
                Assert.Equal(table.Id, relation.SourceTableID); Assert.Equal(table.Id, relation.TargetTableID);
                Assert.Equal(key.KeyID, relation.TargetKeyID); Assert.Equal(table.Fields[0].FieldID, Assert.Single(relation.SourceFieldIDs));
                Assert.Equal("ID", Assert.Single(current.Tables[0].RelationalSchema!.Fields).Name);
                Assert.Equal(table.Fields[0].FieldID, current.Tables[0].RelationalSchema!.Fields[0].FieldID);
                Assert.Equal("1", DataTableIdentity.ReadCell(current, table.Id, table.Records[0].RecordID, table.Fields[0].FieldID)!.Value);
                Assert.False(page.IsDirty);
            }
            finally { window.Content = null; window.Close(); }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    private static void Press(HavenButton button)
    {
        Assert.True(button.KeyDown(new(HavenKey.Enter, HavenKeyModifiers.None)));
        Assert.True(button.KeyUp(new(HavenKey.Enter, HavenKeyModifiers.None)));
    }
    private static async Task UntilAsync(Func<Task<bool>> condition, CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!await condition())
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("The actual native Data owner operation did not complete.");
            await Task.Delay(20, token);
        }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "app.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
