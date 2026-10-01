using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Core.Forms;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure.Tests;

public sealed class DataLocalStoreAuthorityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Exact_Home_record_operation_commits_once_or_reports_validation_failure(bool invalid, bool fromForm)
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(CancellationToken.None);
        var workbook = fixture.Workbook; var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Amount"); sheet.SetCell(1, 0, "1", kind: DataCellKind.Number);
        var table = new DataTableDefinition { SheetId = sheet.Id, HasHeaders = true, Range = new() { EndRow = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        if (invalid) workbook.Validations.Add(new() { SheetId = sheet.Id, Range = new() { StartRow = 1, EndRow = 1 },
            Kind = DataValidationKind.WholeNumber, Minimum = "0", Maximum = "10" });
        await fixture.Repository.SaveAsync(workbook, "Canonical table", (await fixture.CaptureAsync(CancellationToken.None))!, CancellationToken.None);
        var fieldID = table.Fields[0].FieldID; var recordID = table.Records[0].RecordID;
        using var source = JsonDocument.Parse("42");
        var values = new Dictionary<Guid, DataScalarRecordValue> { [fieldID] = new(DataCellKind.Number, source.RootElement) };
        var intent = DataRecordUpdateIntent.Capture(fixture.StoreID, workbook.Id, workbook.Version, workbook.RevisionId, table.Id, recordID, values);
        if (fromForm)
        {
            var now = DateTimeOffset.UtcNow; var form = FormProjectEditor.Create("Bound response", FormModeKind.Form, now);
            var field = new FormField(Guid.NewGuid(), FormFieldKind.Number, "Amount", null, JsonSerializer.SerializeToElement(new { }), true, new());
            form = FormProjectEditor.AddField(form, form.Revision, form.Pages[0].PageID, field, now);
            form = FormProjectEditor.BindData(form, form.Revision, new(Guid.NewGuid(), field.FieldID, workbook.Id,
                table.Id, fieldID, FormDataBindingKind.UpdateRecord), now);
            var runtime = new FormResponseRuntime(form, Guid.NewGuid());
            var answered = runtime.Answer(1, field.FieldID, JsonSerializer.SerializeToElement(42));
            var submitted = runtime.Submit(answered.Response.Revision); Assert.True(submitted.Success);
            var plan = FormDataRecordUpdateProjection.Prepare(form, submitted.Response, fixture.StoreID, workbook, table.Id, recordID);
            Assert.Equal(submitted.Response.ResponseID, plan.ResponseID);
            intent = plan.Intent;
        }
        source.Dispose(); values[fieldID] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(999));
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(fixture.Actor!,
            [new DataWorkbookMutationAccessResolver(fixture.Repository, fixture.Authority!)]), fixture.Permissions!);
        var pending = await broker.AuthorizeAsync("data", DataRecordUpdateIntent.ActionId, intent.Scopes, intent.Arguments,
            "Update canonical amount", null, "actual-data-session");
        Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
        Assert.Null(await broker.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments));
        Assert.True((await fixture.Permissions!.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var capability = (await broker.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments))!;
        Assert.NotNull(capability);
        var operation = new DataHomeRecordUpdateOperation(fixture.Repository, fixture.Authority!, broker);
        var changed = DataRecordUpdateIntent.Capture(fixture.StoreID, workbook.Id, workbook.Version, workbook.RevisionId, table.Id, recordID, values);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation.ExecuteAsync(changed, capability));
        var result = await operation.ExecuteAsync(intent, capability);
        var committed = !invalid && !fromForm;
        Assert.Equal(committed, result.Committed); Assert.True(result.AuditRecorded);
        Assert.Equal(fromForm ? "SourceAuthorityUnavailable" : invalid ? "DataValidationFailed" : "DataRecordUpdated", result.Code);
        var reopened = (await fixture.Repository.LoadAsync(workbook.Id, CancellationToken.None))!;
        Assert.Equal(committed ? 3 : 2, reopened.Version);
        Assert.Equal(committed ? "42" : "1", DataTableIdentity.ReadCell(reopened, table.Id, recordID, fieldID)!.Value);
        Assert.Equal(DataCellKind.Number, DataTableIdentity.ReadCell(reopened, table.Id, recordID, fieldID)!.Kind);
        var recovery = new DataRecordMutationRecovery(new DataWorkbookRepository(fixture.Paths), fixture.Authority!, fixture.Actor!);
        var receipt = await recovery.ReadAsync(intent);
        if (!committed) Assert.Null(receipt);
        else
        {
            Assert.NotNull(receipt); Assert.Equal(intent.OperationID, receipt!.OperationID);
            Assert.Equal(intent.PayloadSHA256, receipt.PayloadSHA256); Assert.Equal(intent.Origin, receipt.Origin);
            Assert.Equal(fromForm, receipt.Origin is not null);
            var receiptConflict = DataRecordUpdateIntent.Capture(fixture.StoreID, workbook.Id, intent.Version,
                intent.RevisionID, table.Id, recordID, values, intent.OperationID, intent.Origin);
            await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.ReadAsync(receiptConflict));
            Assert.Equal(3, (await fixture.Repository.LoadAsync(workbook.Id, CancellationToken.None))!.Version);
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation.ExecuteAsync(intent, capability));
        Assert.Equal(committed ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
            (await fixture.Permissions.GetAuthorizationAsync(pending.RequestId)).State);
        Assert.Single((await fixture.Permissions.GetSnapshotAsync()).RecentAuditEvents,
            item => item.RequestId == pending.RequestId && item.Kind == HomePermissionAuditKind.ExecutionCompleted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Held_workbook_lease_rechecks_actor_binding_and_actual_root(int change)
    {
        using var fixture = new Fixture(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token; await fixture.InitializeAsync(token);
        var workbook = fixture.Workbook; var revision = workbook.RevisionId;
        var current = fixture.Saved!.CurrentPath; var before = await File.ReadAllBytesAsync(current, token);
        var admission = await fixture.CaptureAsync(token); Assert.NotNull(admission);
        var lockPath = Path.Combine(fixture.Paths.DataDirectory, "Data", "Workbooks", ".locks", workbook.Id.ToString("D") + ".lock");
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        workbook.Title = "Denied pending write";
        var pending = fixture.Repository.SaveAsync(workbook, "Must be denied", admission!, token);
        Assert.False(pending.IsCompleted);
        if (change == 0) fixture.Actor!.Current = fixture.Actor.Current with { AuthenticationRevision = "changed" };
        else if (change == 1) fixture.Actor!.Current = fixture.Actor.Current with { ActorId = "other-principal" };
        else if (change == 2)
        {
            var record = Assert.Single((await fixture.Home.ReadAsync()).State!.Records, item => item.RecordType == "home.local-store-ownership");
            var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await fixture.Home.WriteAsync(record with { Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "revoked" }) }, record.Revision)).IsSuccess);
        }
        else
        {
            using var foreign = new Paths(); var settings = new VersionedAtomicSettingsStore(foreign);
            var identity = await settings.GetStoreIdentityAsync(token); Assert.NotEqual(fixture.StoreID, identity.StoreId);
            File.Copy(Path.Combine(foreign.DataDirectory, "settings.json"), Path.Combine(fixture.Paths.DataDirectory, "settings.json"), true);
        }
        lease.Dispose();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Equal(before, await File.ReadAllBytesAsync(current, token));
        Assert.Equal(1, workbook.Version); Assert.Equal(revision, workbook.RevisionId);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(current)!, "*.tmp"));
    }

    [Fact]
    public async Task Final_publication_denial_preserves_current_and_backup_after_temporary_preparation()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(CancellationToken.None);
        var second = await fixture.Repository.SaveAsync(fixture.Workbook, "Second", (await fixture.CaptureAsync(CancellationToken.None))!, CancellationToken.None);
        var current = File.ReadAllBytes(second.CurrentPath); var backup = File.ReadAllBytes(second.BackupPath);
        var admission = (await fixture.CaptureAsync(CancellationToken.None))!;
        var phases = new List<DataWorkbookCommitPhase>();
        var switcher = new SwitchAtPublication(admission, fixture.Actor!, phases);
        fixture.Workbook.Title = "Denied after flush";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Repository.SaveAsync(fixture.Workbook, "Denied", switcher, CancellationToken.None));
        Assert.Equal(new[] { DataWorkbookCommitPhase.Admission, DataWorkbookCommitPhase.Publication }, phases);
        Assert.Equal(current, File.ReadAllBytes(second.CurrentPath)); Assert.Equal(backup, File.ReadAllBytes(second.BackupPath));
        Assert.Equal(2, fixture.Workbook.Version);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(second.CurrentPath)!, "*.tmp"));
    }

    [Fact]
    public async Task Real_Home_receipt_allows_exact_commit_without_reentering_Data_evidence()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(CancellationToken.None);
        var admission = (await fixture.CaptureAsync(CancellationToken.None))!;
        fixture.Evidence!.RejectReads = true;
        fixture.Workbook.Title = "Authorized";
        var saved = await fixture.Repository.SaveAsync(fixture.Workbook, "Exact owner commit", admission, CancellationToken.None);
        Assert.Equal(2, saved.Version);
        Assert.Equal("Authorized", (await new DataWorkbookRepository(fixture.Paths).LoadAsync(fixture.Workbook.Id, CancellationToken.None))!.Title);
    }

    [Fact]
    public async Task Existing_unreadable_Data_is_not_reported_as_empty_for_binding()
    {
        using var paths = new Paths(); var directory = Path.Combine(paths.DataDirectory, "Data", "Workbooks", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "current.json"), "unreadable");
        var repository = new DataWorkbookRepository(paths); var identity = await repository.GetStoreIdentityAsync(CancellationToken.None);
        Assert.Null(await new DataLocalStoreEvidenceProvider(repository).ReadAsync(identity.StoreId.ToString("D"), CancellationToken.None));
    }

    private sealed class SwitchAtPublication(IDataWorkbookCommitAdmission inner, MutableActor actor,
        List<DataWorkbookCommitPhase> phases) : IDataWorkbookCommitAdmission
    {
        public ValueTask<bool> CheckAsync(DataWorkbookCommitContext context, CancellationToken cancellationToken)
        {
            phases.Add(context.Phase);
            if (context.Phase == DataWorkbookCommitPhase.Publication) actor.Current = actor.Current with { AuthenticationRevision = "changed-after-flush" };
            return inner.CheckAsync(context, cancellationToken);
        }
    }
    private sealed class MutableActor(AuthenticatedResourceActor actor) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current { get; set; } = actor;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class Evidence(DataLocalStoreEvidenceProvider inner) : IHomeLocalStoreEvidenceProvider
    {
        public string ResourceKind => "data";
        public bool RejectReads { get; set; }
        public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken cancellationToken) => RejectReads
            ? throw new InvalidOperationException("Data evidence was reentered under the workbook lease.") : inner.ReadAsync(storeId, cancellationToken);
    }
    private sealed class Fixture : IDisposable
    {
        public Paths Paths { get; } = new();
        public FileHomeCoreStateStore Home { get; }
        public DataWorkbookRepository Repository { get; }
        public DataWorkbook Workbook { get; } = DataWorkbook.Create("Owned");
        public MutableActor? Actor { get; private set; }
        public Evidence? Evidence { get; private set; }
        public DataLocalStoreAuthority? Authority { get; private set; }
        public HomePermissionTrustService? Permissions { get; private set; }
        public Guid StoreID { get; private set; }
        public DataSaveResult? Saved { get; private set; }
        public Fixture() { Home = new(Path.Combine(Paths.DataDirectory, "home.json")); Repository = new(Paths); }
        public async Task InitializeAsync(CancellationToken token)
        {
            var profiles = new HomeLocalProfileIdentity(Home, new OperatingSystemPrincipalSource());
            Actor = new((await profiles.GetCurrentAsync(token))!);
            Evidence = new(new DataLocalStoreEvidenceProvider(Repository));
            var policies = new DataMutationActionPolicies(); Permissions = new(Home, policies.TryGet);
            var ownership = new HomeLocalStoreOwnership(Home, profiles, new HomeLocalStoreEvidenceRegistry([Evidence]), Permissions);
            Authority = new(Repository, Actor, new HomeResourceStoreOwnershipAuthority(ownership, Actor));
            StoreID = (await Repository.GetStoreIdentityAsync(token)).StoreId;
            Assert.Null(await Authority.CaptureAsync(StoreID, Workbook.Id, 0, Guid.Empty, "data.workbook.create", Actor.Current, token));
            await ownership.BindNewEmptyAsync("data", StoreID.ToString("D"));
            var admission = await Authority.CaptureAsync(StoreID, Workbook.Id, 0, Guid.Empty, "data.workbook.create", Actor.Current, token);
            Assert.NotNull(admission);
            Saved = await Repository.SaveAsync(Workbook, "Create owned", admission!, token);
        }
        public ValueTask<IDataWorkbookCommitAdmission?> CaptureAsync(CancellationToken token) =>
            Authority!.CaptureAsync(StoreID, Workbook.Id, Workbook.Version, Workbook.RevisionId, "data.workbook.save", Actor!.Current, token);
        public void Dispose() => Paths.Dispose();
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-data-home", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public Paths() => Directory.CreateDirectory(DataDirectory);
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
