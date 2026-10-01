using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using Haven.Core.Forms;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure.Tests;

public sealed class FormDataPreparationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Actual_bound_stores_prepare_retained_response_then_require_exact_Home_approval_and_preserve_receipt(int sourceChange)
    {
        using var paths = new Paths(); var token = CancellationToken.None;
        var home = new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "home.json"));
        var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        var actors = new Actor((await profiles.GetCurrentAsync(token))!);
        var settings = new VersionedAtomicSettingsStore(paths); var workbooks = new DataWorkbookRepository(paths);
        var policies = new DataMutationActionPolicies();
        var permissions = new HomePermissionTrustService(home, (app, action) => policies.TryGet(app, action)
            ?? (app == "9to1.home.local-profile" && action == "home.profile.importStore"
                ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null));
        var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry(
            [new DataLocalStoreEvidenceProvider(workbooks), new FormLocalStoreEvidenceProvider(settings, settings)]), permissions);
        var receiptAuthority = new HomeResourceStoreOwnershipAuthority(ownership, actors);
        var dataAuthority = new DataLocalStoreAuthority(workbooks, actors, receiptAuthority);
        var formAuthority = new FormLocalStoreAuthority(settings, settings, actors, receiptAuthority);
        var storeID = (await workbooks.GetStoreIdentityAsync(token)).StoreId;
        await ownership.BindNewEmptyAsync("data", storeID.ToString("D"));
        // Data initialized the shared settings identity; the Forms domain must explicitly import it.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ownership.BindNewEmptyAsync("forms", storeID.ToString("D")));
        var import = await ownership.RequestImportAsync("forms", storeID.ToString("D"), "form-data-setup");
        Assert.Equal(HomePermissionRequestState.PendingApproval, import.State);
        Assert.True((await permissions.DecideAsync(import.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        await ownership.CompleteImportAsync(import.RequestId);
        var workbook = DataWorkbook.Create("Bound amounts"); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Amount"); sheet.SetCell(1, 0, "1", kind: DataCellKind.Number);
        var table = new DataTableDefinition { SheetId = sheet.Id, HasHeaders = true, Range = new() { EndRow = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        var create = await dataAuthority.CaptureAsync(storeID, workbook.Id, 0, Guid.Empty, "data.workbook.create", actors.Current, token);
        await workbooks.SaveAsync(workbook, "Create bound target", create!, token);
        var now = DateTimeOffset.UtcNow; var form = FormProjectEditor.Create("Amount", FormModeKind.Form, now);
        var field = new FormField(Guid.NewGuid(), FormFieldKind.Number, "Published amount", null, JsonSerializer.SerializeToElement(new { }), true, new());
        form = FormProjectEditor.AddField(form, form.Revision, form.Pages[0].PageID, field, now);
        form = FormProjectEditor.BindData(form, form.Revision, new(Guid.NewGuid(), field.FieldID, workbook.Id, table.Id,
            table.Fields[0].FieldID, FormDataBindingKind.UpdateRecord), now);
        // This focused fixture admits this known scalar binding; production publication still requires its real capability validator.
        var publications = new FormPublicationService(settings, settings, formAuthority, new ScalarFixtureValidator());
        var created = await publications.CreateAsync(form.FormID, FormProjectEditor.Project(form)); Assert.True(created.Success);
        var published = (await publications.PublishAsync(form.FormID, created.Publication!.Revision)).Publication!;
        var responses = new FormResponseSessionService(publications, settings, settings, formAuthority, actors);
        var started = await responses.StartAsync(form.FormID, published.Revision); Assert.True(started.Success);
        var responseID = started.Response!.ResponseID; var operationID = Guid.NewGuid();
        var preparation = new FormDataRecordPreparationService(responses, workbooks, dataAuthority, actors);
        Assert.Equal("ResponseNotSubmitted", (await preparation.PrepareAsync(form.FormID, responseID, workbook.Id,
            table.Id, table.Records[0].RecordID, operationID)).Code);
        var answered = await responses.AnswerAsync(form.FormID, responseID, 1, field.FieldID, JsonSerializer.SerializeToElement(42));
        var submitted = await responses.SubmitAsync(form.FormID, responseID, answered.Response!.Revision); Assert.True(submitted.Success);
        var draft = await new FormAuthoringService(publications).UpdateFieldAsync(form.FormID, published.Revision,
            form.Fields[0] with { Label = "Later draft", Kind = FormFieldKind.ShortText });
        Assert.True(draft.Success);
        var prepared = await preparation.PrepareAsync(form.FormID, responseID, workbook.Id, table.Id, table.Records[0].RecordID, operationID);
        Assert.True(prepared.Success); var intent = prepared.Plan!.Intent;
        Assert.Equal(operationID, intent.OperationID); Assert.Equal(submitted.Response!.FormVersionID, intent.Origin!.FormVersionID);
        Assert.Equal(DataCellKind.Number, Assert.Single(intent.Values).Value.Kind);
        Assert.Equal(1, (await workbooks.LoadAsync(workbook.Id, token))!.Version);
        Assert.Equal(storeID, intent.Origin!.SourceStoreID);
        var journal = new FormDataResponseWriteService(preparation, responses,
            new DataRecordMutationRecovery(workbooks, dataAuthority, actors));
        // Actual legacy envelope: reads preserve bytes; the next admitted mutation migrates in place.
        var responseKey = "forms.response-sessions.v1." + form.FormID.ToString("N");
        var legacy = JsonNode.Parse((await settings.ExportAsync(token)).Settings[responseKey]!)!.AsObject();
        legacy["SchemaVersion"] = 1;
        foreach (var entry in legacy["Responses"]!.AsArray()) entry!.AsObject().Remove("DataWrites");
        await settings.SetAsync(responseKey, legacy, token);
        var legacyJson = (await settings.ExportAsync(token)).Settings[responseKey];
        Assert.Empty((await journal.ReadAsync(form.FormID, responseID)).Attempts);
        Assert.Equal(legacyJson, (await settings.ExportAsync(token)).Settings[responseKey]);
        var queued = await journal.PrepareAsync(form.FormID, responseID, workbook.Id, table.Id, table.Records[0].RecordID, operationID);
        Assert.True(queued.Success, queued.Code); Assert.Equal(FormsDataWriteStatus.Pending, queued.Attempt!.Status);
        Assert.Equal(intent.PayloadSHA256, queued.Attempt.Operation.Restore().PayloadSHA256);
        var repeated = await journal.PrepareAsync(form.FormID, responseID, workbook.Id, table.Id, table.Records[0].RecordID, operationID);
        Assert.True(repeated.Success); Assert.Equal(1, repeated.Attempt!.Revision);
        var migrated = JsonNode.Parse((await settings.ExportAsync(token)).Settings[responseKey]!)!;
        Assert.Equal(2, migrated["SchemaVersion"]!.GetValue<int>());
        Assert.Equal(legacy["Responses"]![0]!["Checkpoint"]!.ToJsonString(), migrated["Responses"]![0]!["Checkpoint"]!.ToJsonString());
        Assert.Equal("DataWritePending", (await journal.ReconcileAsync(form.FormID, responseID, operationID)).Code);
        if (sourceChange == 2) intent = DataRecordUpdateIntent.Capture(intent.StoreID, intent.WorkbookID, intent.Version,
            intent.RevisionID, intent.TableID, intent.RecordID, intent.Values, intent.OperationID,
            intent.Origin with { ResponseID = Guid.NewGuid() });
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(actors,
            [new DataWorkbookMutationAccessResolver(workbooks, dataAuthority)]), permissions);
        var request = await broker.AuthorizeAsync("data", DataRecordUpdateIntent.ActionId, intent.Scopes, intent.Arguments, "Apply submitted amount", null, "form-data-session");
        Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
        Assert.Null(await broker.BeginExecutionCapabilityAsync(request.RequestId, intent.Arguments));
        Assert.True((await permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var capability = (await broker.BeginExecutionCapabilityAsync(request.RequestId, intent.Arguments))!;
        var sourceAuthority = new SignallingSource(new FormDataMutationSourceAuthority(preparation, responses, receiptAuthority, actors));
        var operation = new DataHomeRecordUpdateOperation(workbooks, dataAuthority, broker, sourceAuthority);
        async Task RevokeSourceAsync()
        {
            var bindingRecord = Assert.Single((await home.ReadAsync()).State!.Records,
                item => item.RecordType == "home.local-store-ownership" && item.Payload.Deserialize<HomeLocalStoreBinding>()!.ResourceKind == "forms");
            var binding = bindingRecord.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await home.WriteAsync(bindingRecord with { Revision = bindingRecord.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "revoked" }) }, bindingRecord.Revision)).IsSuccess);
        }
        if (sourceChange == 1) await RevokeSourceAsync();
        DataRecordMutationResult result;
        if (sourceChange == 3)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var held = new FileStream(Path.Combine(paths.DataDirectory, "Data", "Workbooks", ".locks", workbook.Id.ToString("D") + ".lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var running = operation.ExecuteAsync(intent, capability, timeout.Token);
            await Task.WhenAny(sourceAuthority.Captured.Task, running);
            Assert.True(sourceAuthority.Captured.Task.IsCompletedSuccessfully);
            Assert.False(running.IsCompleted);
            await RevokeSourceAsync(); held.Dispose();
            result = await running;
        }
        else result = await operation.ExecuteAsync(intent, capability);
        Assert.Equal(sourceChange == 0, result.Committed); Assert.True(result.AuditRecorded);
        if (sourceChange != 0) Assert.Equal("PermissionDenied", result.Code);
        var recovery = new DataRecordMutationRecovery(new DataWorkbookRepository(paths), dataAuthority, actors);
        var receipt = await recovery.ReadAsync(intent);
        if (sourceChange == 0)
        {
            Assert.Equal(responseID, receipt!.Origin!.ResponseID); Assert.Equal(1, receipt.SourceAdmissionVersion);
            Assert.Equal(storeID, receipt.Origin.SourceStoreID);
            // Simulated process interruption: target committed while Forms still records Pending.
            Assert.Equal(FormsDataWriteStatus.Pending, Assert.Single((await journal.ReadAsync(form.FormID, responseID)).Attempts).Status);
            var reopenedSettings = new VersionedAtomicSettingsStore(paths);
            var reopenedPublications = new FormPublicationService(reopenedSettings, reopenedSettings, formAuthority, new ScalarFixtureValidator());
            var reopenedResponses = new FormResponseSessionService(reopenedPublications, reopenedSettings, reopenedSettings, formAuthority, actors);
            var reopenedJournal = new FormDataResponseWriteService(
                new FormDataRecordPreparationService(reopenedResponses, workbooks, dataAuthority, actors), reopenedResponses, recovery);
            var reconciled = await reopenedJournal.ReconcileAsync(form.FormID, responseID, operationID);
            Assert.True(reconciled.Success, reconciled.Code); Assert.Equal(FormsDataWriteStatus.Succeeded, reconciled.Attempt!.Status);
            Assert.Equal(2, reconciled.Attempt.Revision); Assert.Equal(receipt, reconciled.Attempt.Receipt);
            Assert.Equal(2, (await reopenedJournal.ReconcileAsync(form.FormID, responseID, operationID)).Attempt!.Revision);
            Assert.Equal(submitted.Response.Revision, (await reopenedResponses.ReadSubmittedAsync(form.FormID, responseID)).Response!.Revision);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => recovery.ReadAsync(intent,
                actors.Current with { AuthenticationRevision = "other" }, token));
        }
        else
        {
            Assert.Null(receipt);
            var pending = await journal.ReadAsync(form.FormID, responseID);
            if (sourceChange == 2)
            {
                Assert.True(pending.Success); Assert.Equal(FormsDataWriteStatus.Pending, Assert.Single(pending.Attempts).Status);
                Assert.Equal("DataWritePending", (await journal.ReconcileAsync(form.FormID, responseID, operationID)).Code);
            }
            else Assert.False(pending.Success);
        }
        var owner = actors.Current; actors.Current = owner with { ActorId = "other", ProfileId = "other" };
        Assert.False((await preparation.PrepareAsync(form.FormID, responseID, workbook.Id, table.Id, table.Records[0].RecordID, operationID)).Success);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => recovery.ReadAsync(intent));
        Assert.Equal(sourceChange == 0 ? 2 : 1, (await workbooks.LoadAsync(workbook.Id, token))!.Version);
    }

    private sealed class SignallingSource(IDataRecordMutationOriginAuthority inner) : IDataRecordMutationOriginAuthority
    {
        public TaskCompletionSource<bool> Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<IDataWorkbookCommitAdmission?> CaptureAsync(DataRecordUpdateIntent intent, AuthenticatedResourceActor actor,
            CancellationToken cancellationToken)
        {
            var admission = await inner.CaptureAsync(intent, actor, cancellationToken);
            if (admission is not null) Captured.TrySetResult(true);
            return admission;
        }
    }
    private sealed class ScalarFixtureValidator : IFormProjectPublicationValidator
    {
        public void Validate(Guid id, JsonElement element)
        {
            var project = FormProjectCodec.Decode(Encoding.UTF8.GetBytes(element.GetRawText())); Assert.Equal(id, project.FormID);
            Assert.All(project.DataBindings, binding => Assert.Equal(FormDataBindingKind.UpdateRecord, binding.Kind));
        }
    }
    private sealed class Actor(AuthenticatedResourceActor actor) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current { get; set; } = actor;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("astra-form-data-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
