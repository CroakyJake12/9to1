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
                    byte[]? foreignHome = null;
                    var currentStoreID = identity.StoreId;
                    var settingsPath = Path.Combine(root, "settings.json");
                    if (replacement == 1)
                    {
                        // Keep every displayed workbook/table/field/version identity colliding; replace only actual persisted store UUID.
                        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath, token))!.AsObject();
                        var persistedIdentity = envelope[nameof(SettingsExportManifest.StoreIdentity)]!.AsObject();
                        Assert.Equal(identity.StoreId, persistedIdentity[nameof(SettingsStoreIdentity.StoreId)]!.GetValue<Guid>());
                        currentStoreID = Guid.NewGuid();
                        persistedIdentity[nameof(SettingsStoreIdentity.StoreId)] = currentStoreID;
                        envelope["opaqueDisplayReplacement"] = "preserve foreign supplied bytes exactly";
                        foreignSettings = JsonSerializer.SerializeToUtf8Bytes(envelope);
                        await File.WriteAllBytesAsync(settingsPath, foreignSettings, token);
                    }
                    else
                    {
                        // A same-store binding cannot legitimately be transferred merely by changing a profile.
                        // Prepare a distinct real Home context, import through its actual approval workflow,
                        // then replace the supplied Home file with that independently legitimate context.
                        var foreignHomePath = Path.Combine(root, "independently-owned-home.json");
                        IServiceCollection foreignRegistrations = new ServiceCollection();
                        foreach (var descriptor in registrations) foreignRegistrations.Add(descriptor);
                        foreignRegistrations.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(foreignHomePath));
                        await using var foreignGraph = foreignRegistrations.BuildServiceProvider();
                        var foreignActor = (await foreignGraph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token))!;
                        Assert.NotEqual(actor.ProfileId, foreignActor.ProfileId);
                        var foreignOwnership = foreignGraph.GetRequiredService<HomeLocalStoreOwnership>();
                        var foreignPermissions = foreignGraph.GetRequiredService<HomePermissionTrustService>();
                        var imported = await foreignOwnership.RequestImportAsync(foreignActor, "data", currentStoreID.ToString("D"),
                            "independent-native-profile-import", token);
                        Assert.Equal(HomePermissionRequestState.PendingApproval, imported.State);
                        Assert.True((await foreignPermissions.DecideAsync(imported.RequestId, HomeApprovalChoice.Accept,
                            cancellationToken: token)).Succeeded);
                        await foreignOwnership.CompleteImportAsync(imported.RequestId, token);
                        Assert.NotNull(await foreignOwnership.GetVerifiedAsync("data", currentStoreID.ToString("D"), token));
                        Assert.Empty((await foreignPermissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
                        foreignHome = await File.ReadAllBytesAsync(foreignHomePath, token);
                        await File.WriteAllBytesAsync(Path.Combine(root, "home.json"), foreignHome, token);
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
                    if (replacement == 1)
                    {
                        var import = await ownership.RequestImportAsync(currentActor, "data", currentStoreID.ToString("D"), "actual-native-replacement-import", token);
                        Assert.Equal(HomePermissionRequestState.PendingApproval, import.State);
                        Assert.True((await permissions.DecideAsync(import.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
                        await ownership.CompleteImportAsync(import.RequestId, token);
                    }
                    Assert.NotNull(await ownership.GetVerifiedAsync("data", currentStoreID.ToString("D"), token));
                    await AssertNoPersistedPendingAsync(graph.GetRequiredService<IHomeCoreStateStore>(), token);
                    var freshAdmission = await authority.CaptureAsync(currentStoreID, workbook.Id, originalVersion, workbook.RevisionId,
                        DataRecordCreateIntent.ActionID, currentActor, token);
                    Assert.NotNull(freshAdmission);
                    Assert.True(await freshAdmission.CheckAsync(new(currentStoreID, workbook.Id, originalVersion, workbook.RevisionId,
                        DataWorkbookCommitPhase.Publication), token));
                    Press(Button("Data.RecordCreate.Review"));
                    await UntilAsync(() => Task.FromResult(Status().StartsWith("Review failed;", StringComparison.Ordinal)), token);
                    await AssertNoPersistedPendingAsync(graph.GetRequiredService<IHomeCoreStateStore>(), token);
                    Assert.Equal(originalBytes, await File.ReadAllBytesAsync(saved.CurrentPath, token));
                    if (foreignSettings is not null) Assert.True(Enumerable.SequenceEqual(foreignSettings, await File.ReadAllBytesAsync(settingsPath, token)));
                    if (foreignHome is not null) Assert.True(Enumerable.SequenceEqual(foreignHome, await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token)));
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

    private static async Task AssertNoPersistedPendingAsync(IHomeCoreStateStore home, CancellationToken token)
    {
        // GetSnapshotAsync expires grants and always persists a new Home revision. Read the actual
        // durable permission requests without allowing the observer to change supplied foreign bytes.
        var read = await home.ReadAsync(token);
        Assert.True(read.IsSuccess);
        var record = Assert.Single(read.State!.Records, item => item.RecordId == "home.permissions-trust");
        Assert.Equal("home.permissions-trust", record.RecordType);
        Assert.Equal(1, record.SchemaVersion);
        var requests = record.Payload.GetProperty("Requests").Deserialize<HomePermissionRequest[]>()
            ?? throw new InvalidDataException("Actual persisted permission requests are required.");
        Assert.Empty(requests.Where(request => request.State == HomePermissionRequestState.PendingApproval));
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
