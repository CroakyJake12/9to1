using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

public sealed record DataRelationshipMutationResult(bool Committed, string Code, DataSaveResult? Saved, bool AuditRecorded);

public sealed record DataRelationshipMutationReceipt(int SchemaVersion, Guid OperationID, string PayloadSHA256,
    Guid RelationshipID, int ExpectedWorkbookVersion, Guid ExpectedWorkbookRevision, DataRelationshipMutationKind Kind,
    long? ExpectedRelationshipRevision, long? CommittedRelationshipRevision);

/// <summary>Consumes the one-use Home approval and carries actual root/actor admission into the same
/// canonical workbook repository. There is no relationship-only side store or generic UI authorization.</summary>
public sealed class DataHomeRelationshipUpdateOperation(IDataWorkbookRepository workbooks,
    IDataWorkbookCommitAuthority authority, HomeResourceOperationBroker home)
{
    private const string ReceiptKey = "data.relationship-mutations.v1";
    public async Task<DataRelationshipMutationResult> ExecuteAsync(DataRelationshipUpdateIntent intent,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        var actor = await home.ClaimExecutionAsync(capability, DataRelationshipUpdateIntent.TargetAppID,
            DataRelationshipUpdateIntent.ActionID, intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Home did not authorize this exact relationship edit.");
        DataSaveResult saved;
        try
        {
            if (workbooks is not IDataGuardedWorkbookRepository guarded) throw new NotSupportedException("Data guarded commit is unavailable.");
            var workbook = await workbooks.LoadAsync(intent.WorkbookID, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("WorkbookNotFound");
            if (workbook.Version != intent.Version || workbook.RevisionId != intent.RevisionID)
                throw new DataWorkbookRevisionConflictException(intent.WorkbookID, intent.Version, workbook.Version);
            var admission = await authority.CaptureAsync(intent.StoreID, intent.WorkbookID, intent.Version, intent.RevisionID,
                DataRelationshipUpdateIntent.ActionID, actor, cancellationToken).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Data relationship owner admission was revoked.");
            var receipts = ReadReceipts(workbook);
            if (receipts.Any(receipt => receipt.OperationID == intent.OperationID))
                throw new InvalidOperationException("DataRelationshipOperationAlreadyRecorded");
            if (receipts.Count >= 10000) throw new InvalidOperationException("DataRelationshipReceiptCapacityReached");
            var definition = intent.Relationship;
            var actualPreview = DataRelationshipUpdateIntent.Capture(intent.StoreID, workbook, intent.Kind,
                definition, intent.ExpectedRelationshipRevision, intent.OperationID);
            if (actualPreview.PayloadSHA256 != intent.PayloadSHA256)
                throw new InvalidOperationException("DataRelationshipPreviewConflict");
            var edited = intent.Kind == DataRelationshipMutationKind.Upsert
                ? DataTableDesign.SetRelationship(workbook, intent.Version, intent.RevisionID, intent.ExpectedRelationshipRevision, definition)
                : DataTableDesign.RemoveRelationship(workbook, intent.Version, intent.RevisionID, intent.RelationshipID, intent.ExpectedRelationshipRevision!.Value);
            if (!edited.Success) throw new InvalidDataException("RelationshipValidationFailed: " + edited.Issues[0].Code);
            workbook = edited.Workbook!;
            receipts.Add(new(1, intent.OperationID, intent.PayloadSHA256, intent.RelationshipID, intent.Version, intent.RevisionID,
                intent.Kind, intent.ExpectedRelationshipRevision, intent.Kind == DataRelationshipMutationKind.Upsert ? definition.Revision : null));
            workbook.Metadata[ReceiptKey] = JsonSerializer.Serialize(receipts);
            saved = await guarded.SaveAsync(workbook, "Home-approved canonical relationship edit", admission, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or NotSupportedException or KeyNotFoundException or OperationCanceledException or JsonException or OverflowException)
        {
            var code = error switch
            {
                OperationCanceledException => "Cancelled", UnauthorizedAccessException => "PermissionDenied",
                DataWorkbookRevisionConflictException => "RevisionConflict",
                InvalidDataException => "RelationshipValidationFailed",
                InvalidOperationException { Message: "DataRelationshipPreviewConflict" } => "DataRelationshipPreviewConflict",
                _ => "DataRelationshipUpdateFailed"
            };
            var audit = await CompleteAsync(capability, new(error is OperationCanceledException ? HomePermissionRequestState.Cancelled
                : HomePermissionRequestState.Failed, code, "The relationship edit did not commit.", [])).ConfigureAwait(false);
            return new(false, code, null, audit);
        }
        var recorded = await CompleteAsync(capability, new(HomePermissionRequestState.Succeeded, "DataRelationshipUpdated",
            $"Committed workbook revision {saved.Version}.", [new("data.relationship", intent.RelationshipID.ToString("D"))])).ConfigureAwait(false);
        return new(true, recorded ? "DataRelationshipUpdated" : "DataRelationshipCommittedAuditPending", saved, recorded);
    }

    public static DataRelationshipMutationReceipt? ReadReceipt(DataWorkbook workbook, DataRelationshipUpdateIntent intent)
    {
        var receipt = ReadReceipts(workbook).SingleOrDefault(receipt => receipt.OperationID == intent.OperationID);
        if (receipt is null) return null;
        if (receipt.RelationshipID != intent.RelationshipID || receipt.ExpectedWorkbookVersion != intent.Version
            || receipt.ExpectedWorkbookRevision != intent.RevisionID || receipt.PayloadSHA256 != intent.PayloadSHA256
            || receipt.Kind != intent.Kind || receipt.ExpectedRelationshipRevision != intent.ExpectedRelationshipRevision)
            throw new InvalidDataException("The retained relationship receipt differs from the reviewed operation.");
        return receipt;
    }
    private static List<DataRelationshipMutationReceipt> ReadReceipts(DataWorkbook workbook)
    {
        if (!workbook.Metadata.TryGetValue(ReceiptKey, out var json)) return [];
        var receipts = JsonSerializer.Deserialize<List<DataRelationshipMutationReceipt>>(json)
            ?? throw new InvalidDataException("The relationship receipt journal is missing.");
        var seen = new HashSet<Guid>();
        if (receipts.Count > 10000 || receipts.Any(receipt => receipt is null || receipt.SchemaVersion != 1
            || receipt.OperationID == Guid.Empty || !seen.Add(receipt.OperationID) || receipt.RelationshipID == Guid.Empty
            || receipt.ExpectedWorkbookVersion < 1 || receipt.ExpectedWorkbookRevision == Guid.Empty || !Enum.IsDefined(receipt.Kind)
            || receipt.ExpectedRelationshipRevision is < 1
            || receipt.Kind == DataRelationshipMutationKind.Upsert && receipt.CommittedRelationshipRevision is not > 0
            || receipt.Kind == DataRelationshipMutationKind.Remove && (receipt.ExpectedRelationshipRevision is null || receipt.CommittedRelationshipRevision is not null)
            || receipt.PayloadSHA256 is null || receipt.PayloadSHA256.Length != 64
            || receipt.PayloadSHA256.Any(character => !Uri.IsHexDigit(character))))
            throw new InvalidDataException("The relationship receipt journal is invalid.");
        return receipts;
    }
    private async Task<bool> CompleteAsync(HomeResourceExecutionCapability capability, HomeExecutionOutcome outcome)
    {
        try { return (await home.CompleteExecutionAsync(capability, outcome, CancellationToken.None).ConfigureAwait(false)).Succeeded; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        { return false; }
    }
}
