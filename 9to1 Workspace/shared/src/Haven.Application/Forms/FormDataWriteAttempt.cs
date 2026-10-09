using Haven.Core;

namespace Haven.Application;

public sealed record FormDataWriteAttempt(DataRecordUpdateSnapshot Operation, FormsDataWriteStatus Status,
    DataRecordMutationReceipt? Receipt, long Revision, DataRecordMutationTargetObservation? ObservedTarget = null);
public sealed record FormDataWriteJournalResult(bool Success, string? Code, FormDataWriteAttempt? Attempt);
public sealed record FormDataWriteJournalReadResult(bool Success, string? Code, IReadOnlyList<FormDataWriteAttempt> Attempts);

internal static class FormDataWriteAttemptValidation
{
    public static FormDataWriteAttempt Capture(FormDataWriteAttempt value, Guid formID, Guid responseID, Guid versionID, long responseRevision, Guid sourceStoreID)
    {
        if (value is null || value.Operation is null || value.Revision < 1
            || value.Status is not (FormsDataWriteStatus.Pending or FormsDataWriteStatus.Succeeded or FormsDataWriteStatus.Conflict))
            throw new InvalidDataException("Invalid Forms Data write attempt.");
        DataRecordUpdateIntent intent;
        try { intent = value.Operation.Restore(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        { throw new InvalidDataException("Invalid stored Data operation.", ex); }
        if (intent.Origin is not { } origin || origin.FormID != formID || origin.ResponseID != responseID
            || origin.FormVersionID != versionID || origin.ResponseRevision != responseRevision || origin.SourceStoreID != sourceStoreID)
            throw new InvalidDataException("Data write source differs from the retained response.");
        if (value.ObservedTarget is { } target && (target.StoreID != intent.StoreID || target.WorkbookID != intent.WorkbookID
            || target.Version < 1 || target.RevisionID == Guid.Empty)) throw new InvalidDataException("Invalid Data target observation.");
        if (value.Status == FormsDataWriteStatus.Conflict && (value.Receipt is not null || value.ObservedTarget is not { } conflict
            || conflict.Version == intent.Version && conflict.RevisionID == intent.RevisionID))
            throw new InvalidDataException("Data conflict lacks a changed target revision.");
        if (value.Status == FormsDataWriteStatus.Pending && (value.Receipt is not null
                || value.ObservedTarget is { } pending && (pending.Version != intent.Version || pending.RevisionID != intent.RevisionID))
            || value.Status == FormsDataWriteStatus.Succeeded && (value.Receipt is not { } receipt
                || receipt.OperationID != intent.OperationID || receipt.PayloadSHA256 != intent.PayloadSHA256
                || receipt.TableID != intent.TableID || receipt.RecordID != intent.RecordID || receipt.Origin != intent.Origin
                || receipt.BaseVersion != intent.Version || receipt.BaseRevisionID != intent.RevisionID
                || receipt.SourceAdmissionVersion != 1 || receipt.AppliedAt == default))
            throw new InvalidDataException("Data write state lacks matching committed evidence.");
        return value with { Operation = DataRecordUpdateSnapshot.Capture(intent) };
    }
}
