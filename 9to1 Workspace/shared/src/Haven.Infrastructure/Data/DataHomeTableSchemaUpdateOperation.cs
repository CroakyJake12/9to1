using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

public sealed record DataSchemaMutationResult(bool Committed, string Code, DataSaveResult? Saved, bool AuditRecorded);

public sealed record DataTableSchemaMutationReceipt(int SchemaVersion, Guid OperationID, string PayloadSHA256,
    Guid TableID, int ExpectedWorkbookVersion, Guid ExpectedWorkbookRevision, long SchemaRevision);

/// <summary>Consumes the one-use Home approval and carries actual root/actor admission into the same
/// canonical workbook repository. There is no schema-only side store or generic UI authorization.</summary>
public sealed class DataHomeTableSchemaUpdateOperation(IDataWorkbookRepository workbooks,
    IDataWorkbookCommitAuthority authority, HomeResourceOperationBroker home)
{
    private const string ReceiptKey = "data.table-schema-mutations.v1";
    public async Task<DataSchemaMutationResult> ExecuteAsync(DataTableSchemaUpdateIntent intent,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        var actor = await home.ClaimExecutionAsync(capability, DataTableSchemaUpdateIntent.TargetAppID,
            DataTableSchemaUpdateIntent.ActionID, intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Home did not authorize this exact schema edit.");
        DataSaveResult saved;
        try
        {
            if (workbooks is not IDataGuardedWorkbookRepository guarded) throw new NotSupportedException("Data guarded commit is unavailable.");
            var workbook = await workbooks.LoadAsync(intent.WorkbookID, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("WorkbookNotFound");
            if (workbook.Version != intent.Version || workbook.RevisionId != intent.RevisionID)
                throw new DataWorkbookRevisionConflictException(intent.WorkbookID, intent.Version, workbook.Version);
            var admission = await authority.CaptureAsync(intent.StoreID, intent.WorkbookID, intent.Version, intent.RevisionID,
                DataTableSchemaUpdateIntent.ActionID, actor, cancellationToken).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Data schema owner admission was revoked.");
            var receipts = ReadReceipts(workbook);
            if (receipts.Any(receipt => receipt.OperationID == intent.OperationID))
                throw new InvalidOperationException("DataSchemaOperationAlreadyRecorded");
            if (receipts.Count >= 10000) throw new InvalidOperationException("DataSchemaReceiptCapacityReached");
            var schema = intent.Schema;
            var actualPreview = DataTableSchemaUpdateIntent.Capture(intent.StoreID, workbook, intent.TableID,
                intent.ExpectedSchemaRevision, schema.Fields, schema.Keys, intent.OperationID);
            if (actualPreview.PayloadSHA256 != intent.PayloadSHA256)
                throw new InvalidOperationException("DataSchemaPreviewConflict");
            var edited = DataTableDesign.SetSchema(workbook, intent.TableID, intent.Version, intent.RevisionID,
                intent.ExpectedSchemaRevision, schema.Fields, schema.Keys);
            if (!edited.Success) throw new InvalidDataException("SchemaValidationFailed: " + edited.Issues[0].Code);
            workbook = edited.Workbook!;
            receipts.Add(new(1, intent.OperationID, intent.PayloadSHA256, intent.TableID, intent.Version, intent.RevisionID, schema.Revision));
            workbook.Metadata[ReceiptKey] = JsonSerializer.Serialize(receipts);
            saved = await guarded.SaveAsync(workbook, "Home-approved canonical table schema edit", admission, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or NotSupportedException or KeyNotFoundException or OperationCanceledException or JsonException or OverflowException)
        {
            var code = error switch
            {
                OperationCanceledException => "Cancelled", UnauthorizedAccessException => "PermissionDenied",
                DataWorkbookRevisionConflictException => "RevisionConflict",
                InvalidDataException => "SchemaValidationFailed",
                InvalidOperationException { Message: "DataSchemaPreviewConflict" } => "DataSchemaPreviewConflict",
                _ => "DataSchemaUpdateFailed"
            };
            var audit = await CompleteAsync(capability, new(error is OperationCanceledException ? HomePermissionRequestState.Cancelled
                : HomePermissionRequestState.Failed, code, "The table schema edit did not commit.", [])).ConfigureAwait(false);
            return new(false, code, null, audit);
        }
        var recorded = await CompleteAsync(capability, new(HomePermissionRequestState.Succeeded, "DataTableSchemaUpdated",
            $"Committed workbook revision {saved.Version}.", [new("data.table", intent.TableID.ToString("D"))])).ConfigureAwait(false);
        return new(true, recorded ? "DataTableSchemaUpdated" : "DataSchemaCommittedAuditPending", saved, recorded);
    }

    public static DataTableSchemaMutationReceipt? ReadReceipt(DataWorkbook workbook, DataTableSchemaUpdateIntent intent)
    {
        var receipt = ReadReceipts(workbook).SingleOrDefault(receipt => receipt.OperationID == intent.OperationID);
        if (receipt is null) return null;
        if (receipt.TableID != intent.TableID || receipt.ExpectedWorkbookVersion != intent.Version
            || receipt.ExpectedWorkbookRevision != intent.RevisionID || receipt.PayloadSHA256 != intent.PayloadSHA256)
            throw new InvalidDataException("The retained schema receipt differs from the reviewed operation.");
        return receipt;
    }
    private static List<DataTableSchemaMutationReceipt> ReadReceipts(DataWorkbook workbook)
    {
        if (!workbook.Metadata.TryGetValue(ReceiptKey, out var json)) return [];
        var receipts = JsonSerializer.Deserialize<List<DataTableSchemaMutationReceipt>>(json)
            ?? throw new InvalidDataException("The schema receipt journal is missing.");
        var seen = new HashSet<Guid>();
        if (receipts.Count > 10000 || receipts.Any(receipt => receipt is null || receipt.SchemaVersion != 1
            || receipt.OperationID == Guid.Empty || !seen.Add(receipt.OperationID) || receipt.TableID == Guid.Empty
            || receipt.ExpectedWorkbookVersion < 1 || receipt.ExpectedWorkbookRevision == Guid.Empty || receipt.SchemaRevision < 1
            || receipt.PayloadSHA256 is null || receipt.PayloadSHA256.Length != 64
            || receipt.PayloadSHA256.Any(character => !Uri.IsHexDigit(character))))
            throw new InvalidDataException("The schema receipt journal is invalid.");
        return receipts;
    }
    private async Task<bool> CompleteAsync(HomeResourceExecutionCapability capability, HomeExecutionOutcome outcome)
    {
        try { return (await home.CompleteExecutionAsync(capability, outcome, CancellationToken.None).ConfigureAwait(false)).Succeeded; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        { return false; }
    }
}
