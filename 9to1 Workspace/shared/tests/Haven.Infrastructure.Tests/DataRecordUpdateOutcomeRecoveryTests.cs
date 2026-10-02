using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using System.Text.Json;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class DataRecordUpdateOutcomeRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_guarded_publication_lost_return_stays_unknown_until_exact_receipt_and_never_replays(bool cancelReturn)
    {
        using var paths = new Paths();
        var home = new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "home.json"));
        var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        var actor = Assert.IsType<AuthenticatedResourceActor>(await profiles.GetCurrentAsync(default));
        var physical = new DataWorkbookRepository(paths); var repository = new LostReturnRepository(physical, cancelReturn);
        var policies = new DataMutationActionPolicies(); var permissions = new HomePermissionTrustService(home, policies.TryGet);
        var ownership = new HomeLocalStoreOwnership(home, profiles,
            new HomeLocalStoreEvidenceRegistry([new DataLocalStoreEvidenceProvider(repository)]), permissions);
        var authority = new DataLocalStoreAuthority(repository, profiles, new HomeResourceStoreOwnershipAuthority(ownership, profiles));
        var identity = await repository.GetStoreIdentityAsync(default);
        await ownership.BindNewEmptyAsync("data", identity.StoreId.ToString("D"));
        var workbook = DataWorkbook.Create("Actual update recovery"); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Amount"); sheet.SetCell(1, 0, "1", kind: DataCellKind.Number);
        var table = new DataTableDefinition { SheetId = sheet.Id, HasHeaders = true, Range = new() { EndRow = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        var creation = await authority.CaptureAsync(identity.StoreId, workbook.Id, 0, Guid.Empty, "data.workbook.create", actor, default);
        Assert.NotNull(creation);
        await repository.SaveAsync(workbook, "Actual original table", creation, default);
        var field = table.Fields[0].FieldID; var record = table.Records[0].RecordID;
        var intent = DataRecordUpdateIntent.Capture(identity.StoreId, workbook.Id, workbook.Version, workbook.RevisionId,
            table.Id, record, new Dictionary<Guid, DataScalarRecordValue> { [field] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(42)) });
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(profiles,
            [new DataWorkbookMutationAccessResolver(repository, authority)]), permissions);
        var requested = await broker.AuthorizeForActorAsync(actor, "data", DataRecordUpdateIntent.ActionId,
            intent.Scopes, intent.Arguments, "Update original canonical record", null, "actual-update-recovery", default);
        Assert.True((await permissions.DecideAsync(requested.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var capability = Assert.IsType<HomeResourceExecutionCapability>(await broker.BeginExecutionCapabilityAsync(requested.RequestId, intent.Arguments));
        var recovery = new DataRecordMutationRecovery(repository, authority, profiles);
        var operation = new DataHomeRecordUpdateOperation(repository, authority, broker, recovery: recovery);
        repository.LoseNextReturn = true;
        var lost = await operation.ExecuteAsync(intent, capability);
        Assert.False(lost.OutcomeKnown); Assert.False(lost.Committed); Assert.False(lost.AuditRecorded);
        Assert.Equal("DataOutcomeUnconfirmed", lost.Code); Assert.Equal(1, repository.AttemptedMutationWrites);
        var published = Assert.IsType<DataSaveResult>(repository.Published);
        var bytes = await File.ReadAllBytesAsync(published.CurrentPath);
        // Exact inner physical publication happened; observation can still be unavailable.
        repository.HideReads = true;
        var hidden = await operation.FinishAsync(capability);
        Assert.False(hidden.OutcomeKnown); Assert.False(hidden.AuditRecorded);
        Assert.Equal(HomePermissionRequestState.Executing, (await permissions.ReadRequestObservationAsync(requested.RequestId))!.State);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(published.CurrentPath));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation.ExecuteAsync(intent, capability));
        Assert.Equal(1, repository.AttemptedMutationWrites);
        repository.HideReads = false;
        var observed = await operation.FinishAsync(capability);
        Assert.True(observed.OutcomeKnown); Assert.True(observed.Committed); Assert.True(observed.AuditRecorded);
        Assert.Null(observed.Saved); // Recovery does not fabricate paths/timestamps from a later read.
        var reopened = Assert.IsType<DataWorkbook>(await physical.LoadAsync(workbook.Id, default));
        Assert.Equal("42", DataTableIdentity.ReadCell(reopened, table.Id, record, field)!.Value);
        var receipt = Assert.Single(DataRecordMutationReceipts.Read(reopened));
        Assert.Equal(intent.OperationID, receipt.OperationID); Assert.Equal(intent.PayloadSHA256, receipt.PayloadSHA256);
        var terminalHome = await File.ReadAllBytesAsync(Path.Combine(paths.DataDirectory, "home.json"));
        Assert.True((await operation.FinishAsync(capability)).AuditRecorded);
        Assert.Equal(1, repository.AttemptedMutationWrites); Assert.Equal(bytes, await File.ReadAllBytesAsync(published.CurrentPath));
        Assert.Equal(terminalHome, await File.ReadAllBytesAsync(Path.Combine(paths.DataDirectory, "home.json")));
    }

    // Delegates ORIGINAL admission to the real atomic workbook publication, then loses ONLY its return.
    private sealed class LostReturnRepository(DataWorkbookRepository inner, bool cancelReturn) : IDataGuardedWorkbookRepository
    {
        public bool LoseNextReturn { get; set; }
        public bool HideReads { get; set; }
        public int AttemptedMutationWrites { get; private set; }
        public DataSaveResult? Published { get; private set; }
        public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken ct) => inner.GetStoreIdentityAsync(ct);
        public ValueTask<DataWorkbookStoreEvidence?> ReadStoreEvidenceAsync(CancellationToken ct) => inner.ReadStoreEvidenceAsync(ct);
        public Task<IReadOnlyList<DataWorkbookSummary>> ListAsync(CancellationToken ct) => inner.ListAsync(ct);
        public Task<DataWorkbook?> LoadAsync(Guid id, CancellationToken ct) => HideReads
            ? Task.FromException<DataWorkbook?>(new IOException("Actual receipt observation held unavailable.")) : inner.LoadAsync(id, ct);
        public Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, CancellationToken ct) => inner.SaveAsync(workbook, reason, ct);
        public Task DeleteAsync(Guid id, CancellationToken ct) => inner.DeleteAsync(id, ct);
        public async Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, IDataWorkbookCommitAdmission admission, CancellationToken ct)
        {
            var lose = LoseNextReturn; LoseNextReturn = false;
            if (lose || Published is not null) AttemptedMutationWrites++; // Count every subsequent attempted write, including forbidden replay.
            var result = await inner.SaveAsync(workbook, reason, admission, ct);
            if (!lose) return result;
            Published = result;
            if (cancelReturn) throw new OperationCanceledException("Return lost after actual publication.");
            throw new IOException("Return lost after actual publication.");
        }
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-update-recovery", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public Paths() => Directory.CreateDirectory(DataDirectory);
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
