using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

public sealed record DataJunctionTableMutationResult(bool Committed, string Code, DataSaveResult? Saved, bool AuditRecorded);

public sealed record DataJunctionTableMutationReceipt(int SchemaVersion, Guid OperationID, string PayloadSHA256,
    Guid TableID, Guid SheetID, Guid LeftRelationshipID, Guid RightRelationshipID,
    int ExpectedWorkbookVersion, Guid ExpectedWorkbookRevision);

/// <summary>Consumes the one-use Home approval and carries actual root/actor admission into the same
/// canonical workbook repository. There is no junction-only side store or generic UI authorization.</summary>
public sealed class DataHomeJunctionTableUpdateOperation(IDataWorkbookRepository workbooks,
    IDataWorkbookCommitAuthority authority, HomeResourceOperationBroker home)
{
    private const string ReceiptKey = "data.junction-mutations.v1";
    public async Task<DataJunctionTableMutationResult> ExecuteAsync(DataJunctionTableUpdateIntent intent,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        var actor = await home.ClaimExecutionAsync(capability, DataJunctionTableUpdateIntent.TargetAppID,
            DataJunctionTableUpdateIntent.ActionID, intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Home did not authorize this exact junction edit.");
        DataSaveResult saved;
        try
        {
            if (workbooks is not IDataGuardedWorkbookRepository guarded) throw new NotSupportedException("Data guarded commit is unavailable.");
            var workbook = await workbooks.LoadAsync(intent.WorkbookID, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("WorkbookNotFound");
            if (workbook.Version != intent.Version || workbook.RevisionId != intent.RevisionID)
                throw new DataWorkbookRevisionConflictException(intent.WorkbookID, intent.Version, workbook.Version);
            var admission = await authority.CaptureAsync(intent.StoreID, intent.WorkbookID, intent.Version, intent.RevisionID,
                DataJunctionTableUpdateIntent.ActionID, actor, cancellationToken).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Data junction owner admission was revoked.");
            var receipts = ReadReceipts(workbook);
            if (receipts.Any(receipt => receipt.OperationID == intent.OperationID))
                throw new InvalidOperationException("DataJunctionTableOperationAlreadyRecorded");
            if (receipts.Count >= 10000) throw new InvalidOperationException("DataJunctionTableReceiptCapacityReached");
            var definition = intent.Definition;
            var actualPreview = DataJunctionTableUpdateIntent.Capture(intent.StoreID, workbook, definition, intent.OperationID);
            if (actualPreview.PayloadSHA256 != intent.PayloadSHA256)
                throw new InvalidOperationException("DataJunctionTablePreviewConflict");
            var edited = DataJunctionTableDesign.Prepare(workbook, intent.Version, intent.RevisionID, definition);
            if (!edited.Success) throw new InvalidDataException("JunctionValidationFailed: " + edited.Issues[0].Code);
            workbook = edited.Workbook!;
            receipts.Add(new(1, intent.OperationID, intent.PayloadSHA256, definition.TableID, definition.SheetID,
                definition.LeftRelationshipID, definition.RightRelationshipID, intent.Version, intent.RevisionID));
            workbook.Metadata[ReceiptKey] = JsonSerializer.Serialize(receipts);
            saved = await guarded.SaveAsync(workbook, "Home-approved canonical junction edit", admission, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or NotSupportedException or KeyNotFoundException or OperationCanceledException or JsonException or OverflowException)
        {
            var code = error switch
            {
                OperationCanceledException => "Cancelled", UnauthorizedAccessException => "PermissionDenied",
                DataWorkbookRevisionConflictException => "RevisionConflict",
                InvalidDataException => "JunctionValidationFailed",
                InvalidOperationException { Message: "DataJunctionTablePreviewConflict" } => "DataJunctionTablePreviewConflict",
                _ => "DataJunctionTableUpdateFailed"
            };
            var audit = await CompleteAsync(capability, new(error is OperationCanceledException ? HomePermissionRequestState.Cancelled
                : HomePermissionRequestState.Failed, code, "The junction edit did not commit.", [])).ConfigureAwait(false);
            return new(false, code, null, audit);
        }
        var recorded = await CompleteAsync(capability, new(HomePermissionRequestState.Succeeded, "DataJunctionTableUpdated",
            $"Committed workbook revision {saved.Version}.", [new("data.table", intent.Definition.TableID.ToString("D"))])).ConfigureAwait(false);
        return new(true, recorded ? "DataJunctionTableUpdated" : "DataJunctionTableCommittedAuditPending", saved, recorded);
    }

    public static DataJunctionTableMutationReceipt? ReadReceipt(DataWorkbook workbook, DataJunctionTableUpdateIntent intent)
    {
        var receipt = ReadReceipts(workbook).SingleOrDefault(receipt => receipt.OperationID == intent.OperationID);
        if (receipt is null) return null;
        var definition = intent.Definition;
        if (receipt.TableID != definition.TableID || receipt.SheetID != definition.SheetID
            || receipt.LeftRelationshipID != definition.LeftRelationshipID || receipt.RightRelationshipID != definition.RightRelationshipID
            || receipt.ExpectedWorkbookVersion != intent.Version || receipt.ExpectedWorkbookRevision != intent.RevisionID
            || receipt.PayloadSHA256 != intent.PayloadSHA256)
            throw new InvalidDataException("The retained junction receipt differs from the reviewed operation.");
        return receipt;
    }
    private static List<DataJunctionTableMutationReceipt> ReadReceipts(DataWorkbook workbook)
    {
        if (!workbook.Metadata.TryGetValue(ReceiptKey, out var json)) return [];
        var receipts = JsonSerializer.Deserialize<List<DataJunctionTableMutationReceipt>>(json)
            ?? throw new InvalidDataException("The junction receipt journal is missing.");
        var seen = new HashSet<Guid>();
        if (receipts.Count > 10000 || receipts.Any(receipt => receipt is null || receipt.SchemaVersion != 1
            || receipt.OperationID == Guid.Empty || !seen.Add(receipt.OperationID)
            || new[] { receipt.TableID, receipt.SheetID, receipt.LeftRelationshipID, receipt.RightRelationshipID }.Any(id => id == Guid.Empty)
            || new[] { receipt.TableID, receipt.SheetID, receipt.LeftRelationshipID, receipt.RightRelationshipID }.Distinct().Count() != 4
            || receipt.ExpectedWorkbookVersion < 1 || receipt.ExpectedWorkbookRevision == Guid.Empty
            || receipt.PayloadSHA256 is null || receipt.PayloadSHA256.Length != 64
            || receipt.PayloadSHA256.Any(character => !Uri.IsHexDigit(character))))
            throw new InvalidDataException("The junction receipt journal is invalid.");
        return receipts;
    }
    private async Task<bool> CompleteAsync(HomeResourceExecutionCapability capability, HomeExecutionOutcome outcome)
    {
        try { return (await home.CompleteExecutionAsync(capability, outcome, CancellationToken.None).ConfigureAwait(false)).Succeeded; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        { return false; }
    }
}
