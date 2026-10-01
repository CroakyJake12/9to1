using System.Text.Json;
using System.Text.Json.Nodes;
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

public sealed class DataRecordDocumentWorkspaceHostTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Production_Data_factory_retains_display_identity_before_record_review_and_persists_only_original_approved_default_and_formula(int replacement)
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-data-record-host-" + Guid.NewGuid().ToString("N"));
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
            Assert.IsType<DataHomeRecordCreator>(graph.GetRequiredService<IDataRecordCreator>());
            Assert.Same(graph.GetRequiredService<DataHomeRecordCreator>(), graph.GetRequiredService<IDataRecordCreator>());
            var workbook = DataWorkbook.Create("Canonical record host workbook");
            var sheet = workbook.Sheets[0]; sheet.SetCell(0, 0, "Amount");
            var table = new DataTableDefinition { Name = "Amounts", SheetId = sheet.Id, Range = new() { EndRow = 0 } };
            DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table); workbook.Normalize();
            var fieldID = table.Fields[0].FieldID;
            var key = new DataKeyDefinition(Guid.NewGuid(), "Canonical amount", DataKeyKind.Primary, [fieldID]);
            workbook = DataTableDesign.SetSchema(workbook, table.Id, 0, Guid.Empty, null,
                [new(fieldID, "Amount", DataFieldType.Integer, Nullable: false, DefaultValue: "7")], [key]).Workbook!;
            sheet = workbook.Sheets[0]; sheet.SetCell(0, 2, "old cache", "=A2*2", DataCellKind.Formula);
            var initialCalculation = new DataFormulaEngine().Recalculate(workbook);
            Assert.Empty(initialCalculation.Issues);
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
                HavenButton Button(string id) => Assert.IsType<HavenButton>(page.SceneRoot.DescendantsAndSelf().Single(item => item.Name == id));
                string Status() => Assert.IsType<Text>(page.SceneRoot.DescendantsAndSelf().Single(item => item.Name == "Data.RecordCreate.Status")).Content;
                var permissions = graph.GetRequiredService<HomePermissionTrustService>();
                Assert.False(page.IsDirty); // Persisted formula cache was prepared by the actual engine before display.
                Assert.Equal(workbook.Id, page.Workbook!.Id);
                Assert.Equal(originalVersion, page.Workbook.Version);
                Assert.Equal(workbook.RevisionId, page.Workbook.RevisionId);
                if (replacement != 0)
                {
                    byte[]? foreignSettings = null;
                    var currentStoreID = identity.StoreId;
                    var settingsPath = Path.Combine(root, "settings.json");
                    if (replacement == 1)
                    {
                        // Keep every displayed workbook/table/field/version identity colliding; replace only actual persisted store UUID.
                        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath, token))!.AsObject();
                        var persistedIdentity = envelope["storeIdentity"]!.AsObject();
                        Assert.Equal(identity.StoreId, persistedIdentity["storeId"]!.GetValue<Guid>());
                        currentStoreID = Guid.NewGuid();
                        persistedIdentity["storeId"] = currentStoreID;
                        envelope["opaqueDisplayReplacement"] = "preserve foreign supplied bytes exactly";
                        foreignSettings = JsonSerializer.SerializeToUtf8Bytes(envelope);
                        await File.WriteAllBytesAsync(settingsPath, foreignSettings, token);
                    }
                    else
                    {
                        var home = graph.GetRequiredService<IHomeCoreStateStore>();
                        var profile = Assert.Single((await home.ReadAsync(token)).State!.Records, item => item.RecordType == "home.local-profile");
                        var foreignProfile = profile with { Revision = profile.Revision + 1,
                            Payload = JsonSerializer.SerializeToElement(profile.Payload.Deserialize<HomeLocalProfile>()! with { ProfileId = Guid.NewGuid() }) };
                        Assert.True((await home.WriteAsync(foreignProfile, profile.Revision, token)).IsSuccess);
                    }
                    if (replacement == 1)
                    {
                        // Existing repository explicitly refuses the first changed-root refresh; preserve that real boundary.
                        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetStoreIdentityAsync(token).AsTask());
                        Assert.True(Enumerable.SequenceEqual(foreignSettings!, await File.ReadAllBytesAsync(settingsPath, token)));
                    }
                    // Make the replacement context independently legitimate through actual Home review/import.
                    // The stale displayed selection must still deny, even though a freshly displayed selection could be admitted.
                    var currentActor = (await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token))!;
                    var ownership = graph.GetRequiredService<HomeLocalStoreOwnership>();
                    var import = await ownership.RequestImportAsync(currentActor, "data", currentStoreID.ToString("D"), "actual-native-replacement-import", token);
                    Assert.Equal(HomePermissionRequestState.PendingApproval, import.State);
                    Assert.True((await permissions.DecideAsync(import.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
                    await ownership.CompleteImportAsync(import.RequestId, token);
                    Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
                    var freshAdmission = await authority.CaptureAsync(currentStoreID, workbook.Id, originalVersion, workbook.RevisionId,
                        DataRecordCreateIntent.ActionID, currentActor, token);
                    Assert.NotNull(freshAdmission);
                    Assert.True(await freshAdmission.CheckAsync(new(currentStoreID, workbook.Id, originalVersion, workbook.RevisionId,
                        DataWorkbookCommitPhase.Publication), token));
                    Press(Button("Data.RecordCreate.Review"));
                    await UntilAsync(() => Task.FromResult(Status().StartsWith("Review failed;", StringComparison.Ordinal)), token);
                    Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
                    Assert.Equal(originalBytes, await File.ReadAllBytesAsync(saved.CurrentPath, token));
                    if (foreignSettings is not null) Assert.True(Enumerable.SequenceEqual(foreignSettings, await File.ReadAllBytesAsync(settingsPath, token)));
                    Assert.Equal(originalVersion, page.Workbook!.Version);
                    Assert.Equal(workbook.RevisionId, page.Workbook.RevisionId);
                    Assert.Empty(Assert.Single(page.Workbook.Tables).Records);
                    Assert.False(page.IsDirty);
                    return;
                }
                Press(Button("Data.RecordCreate.Review"));
                await UntilAsync(async () => (await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests.Count == 1, token);
                var request = Assert.Single((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
                Assert.Equal(actor.ActorId, request.Caller.CallerId);
                Assert.Equal(actor.ProfileId, request.Caller.Origin);
                Assert.Equal(DataRecordCreateIntent.ActionID, request.Scope.ActionName);
                var reviewedObject = Assert.Single(request.Scope.Objects);
                Assert.Equal(DataRecordUpdateIntent.ResourceKind, reviewedObject.ObjectType);
                Assert.Equal($"{identity.StoreId:D}/{workbook.Id:D}", reviewedObject.ObjectId);
                Assert.False(string.IsNullOrWhiteSpace(request.Impact.ArgumentsDigest));
                Press(Button("Data.RecordCreate.Apply"));
                await UntilAsync(() => Task.FromResult(Status().Contains("ApprovalRequired", StringComparison.Ordinal)), token);
                Assert.Equal(originalBytes, await File.ReadAllBytesAsync(saved.CurrentPath, token));
                Assert.False(page.IsDirty);
                Assert.True((await permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
                Press(Button("Data.RecordCreate.Apply"));
                await UntilAsync(() => Task.FromResult(page.Workbook!.Version > originalVersion), token);
                var current = (await repository.LoadAsync(workbook.Id, token))!;
                var persistedTable = Assert.Single(current.Tables);
                var record = Assert.Single(persistedTable.Records);
                Assert.Equal(table.Id, persistedTable.Id); Assert.Equal(fieldID, Assert.Single(persistedTable.Fields).FieldID);
                Assert.Equal("7", DataTableIdentity.ReadCell(current, table.Id, record.RecordID, fieldID)!.Value);
                Assert.Equal("14", current.Sheets[0].GetCell(0, 2)!.Value);
                Assert.Equal("=A2*2", current.Sheets[0].GetCell(0, 2)!.Formula);
                Assert.Equal(key.KeyID, Assert.Single(persistedTable.RelationalSchema!.Keys).KeyID);
                var committedBytes = await File.ReadAllBytesAsync(saved.CurrentPath, token);
                Assert.False(Enumerable.SequenceEqual(originalBytes, committedBytes));
                var actualHome = await permissions.GetAuthorizationAsync(request.RequestId, token);
                Assert.Equal(HomePermissionRequestState.Succeeded, actualHome.State);
                Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
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
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("The actual native record creation owner operation did not complete.");
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
