using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

public sealed record DataRecordCreateResult(bool Committed, string Code, DataSaveResult? Saved, bool AuditRecorded);

public sealed record DataRecordCreateReceipt(int SchemaVersion, Guid OperationID, string PayloadSHA256,
    Guid TableID, Guid RecordID, int ExpectedWorkbookVersion, Guid ExpectedWorkbookRevision);

/// <summary>Consumes the one-use Home approval and carries actual root/actor admission into the same
/// canonical workbook repository. There is no record creation-only side store or generic UI authorization.</summary>
public sealed class DataHomeRecordCreateOperation(IDataWorkbookRepository workbooks,
    IDataWorkbookCommitAuthority authority, HomeResourceOperationBroker home)
{
    private const string ReceiptKey = "data.record-create-mutations.v1";
    public async Task<DataRecordCreateResult> ExecuteAsync(DataRecordCreateIntent intent,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        var actor = await home.ClaimExecutionAsync(capability, DataRecordCreateIntent.TargetAppID,
            DataRecordCreateIntent.ActionID, intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Home did not authorize this exact record creation edit.");
        DataSaveResult saved;
        try
        {
            if (workbooks is not IDataGuardedWorkbookRepository guarded) throw new NotSupportedException("Data guarded commit is unavailable.");
            var workbook = await workbooks.LoadAsync(intent.WorkbookID, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("WorkbookNotFound");
            if (workbook.Version != intent.Version || workbook.RevisionId != intent.RevisionID)
                throw new DataWorkbookRevisionConflictException(intent.WorkbookID, intent.Version, workbook.Version);
            var admission = await authority.CaptureAsync(intent.StoreID, intent.WorkbookID, intent.Version, intent.RevisionID,
                DataRecordCreateIntent.ActionID, actor, cancellationToken).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Data record creation owner admission was revoked.");
            var receipts = ReadReceipts(workbook);
            if (receipts.Any(receipt => receipt.OperationID == intent.OperationID))
                throw new InvalidOperationException("DataRecordCreateOperationAlreadyRecorded");
            if (receipts.Count >= 10000) throw new InvalidOperationException("DataRecordCreateReceiptCapacityReached");
            var actualPreview = intent.Revalidate(workbook);
            if (actualPreview.PayloadSHA256 != intent.PayloadSHA256)
                throw new InvalidOperationException("DataRecordCreatePreviewConflict");
            var edited = DataRecordCreationProjection.Prepare(workbook, intent.TableID, intent.RecordID, intent.Version, intent.RevisionID, intent.Values, intent.CalculationAt);
            if (!edited.Success) throw new InvalidDataException("RecordCreationValidationFailed: " + edited.Issues[0].Code);
            workbook = edited.Workbook!;
            receipts.Add(new(1, intent.OperationID, intent.PayloadSHA256, intent.TableID, intent.RecordID, intent.Version, intent.RevisionID));
            workbook.Metadata[ReceiptKey] = JsonSerializer.Serialize(receipts);
            saved = await guarded.SaveAsync(workbook, "Home-approved canonical record creation", admission, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or NotSupportedException or KeyNotFoundException or OperationCanceledException or JsonException or OverflowException)
        {
            var code = error switch
            {
                OperationCanceledException => "Cancelled", UnauthorizedAccessException => "PermissionDenied",
                DataWorkbookRevisionConflictException => "RevisionConflict",
                InvalidDataException => "RecordCreationValidationFailed",
                InvalidOperationException { Message: "DataRecordCreatePreviewConflict" } => "DataRecordCreatePreviewConflict",
                _ => "DataRecordCreateUpdateFailed"
            };
            var audit = await CompleteAsync(capability, new(error is OperationCanceledException ? HomePermissionRequestState.Cancelled
                : HomePermissionRequestState.Failed, code, "The record was not created.", [])).ConfigureAwait(false);
            return new(false, code, null, audit);
        }
        var recorded = await CompleteAsync(capability, new(HomePermissionRequestState.Succeeded, "DataRecordCreated",
            $"Committed workbook revision {saved.Version}.", [new("data.record", intent.RecordID.ToString("D"))])).ConfigureAwait(false);
        return new(true, recorded ? "DataRecordCreated" : "DataRecordCreatedAuditPending", saved, recorded);
    }

    public static DataRecordCreateReceipt? ReadReceipt(DataWorkbook workbook, DataRecordCreateIntent intent)
    {
        var receipt = ReadReceipts(workbook).SingleOrDefault(receipt => receipt.OperationID == intent.OperationID);
        if (receipt is null) return null;
        if (receipt.TableID != intent.TableID || receipt.RecordID != intent.RecordID
            || receipt.ExpectedWorkbookVersion != intent.Version || receipt.ExpectedWorkbookRevision != intent.RevisionID
            || receipt.PayloadSHA256 != intent.PayloadSHA256)
            throw new InvalidDataException("The retained record creation receipt differs from the reviewed operation.");
        return receipt;
    }
    private static List<DataRecordCreateReceipt> ReadReceipts(DataWorkbook workbook)
    {
        if (!workbook.Metadata.TryGetValue(ReceiptKey, out var json)) return [];
        var receipts = JsonSerializer.Deserialize<List<DataRecordCreateReceipt>>(json)
            ?? throw new InvalidDataException("The record creation receipt journal is missing.");
        var seen = new HashSet<Guid>();
        if (receipts.Count > 10000 || receipts.Any(receipt => receipt is null || receipt.SchemaVersion != 1
            || receipt.OperationID == Guid.Empty || !seen.Add(receipt.OperationID)
            || new[] { receipt.TableID, receipt.RecordID }.Any(id => id == Guid.Empty)
            || new[] { receipt.TableID, receipt.RecordID }.Distinct().Count() != 2
            || receipt.ExpectedWorkbookVersion < 1 || receipt.ExpectedWorkbookRevision == Guid.Empty
            || receipt.PayloadSHA256 is null || receipt.PayloadSHA256.Length != 64
            || receipt.PayloadSHA256.Any(character => !Uri.IsHexDigit(character))))
            throw new InvalidDataException("The record creation receipt journal is invalid.");
        return receipts;
    }
    private async Task<bool> CompleteAsync(HomeResourceExecutionCapability capability, HomeExecutionOutcome outcome)
    {
        try { return (await home.CompleteExecutionAsync(capability, outcome, CancellationToken.None).ConfigureAwait(false)).Succeeded; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        { return false; }
    }
}
