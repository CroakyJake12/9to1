namespace Haven.Application;

/// <summary>Persists an exact pending operation before approval and reconciles only actual committed
/// Data evidence. These methods do not issue a Home capability or execute the target mutation.</summary>
public sealed class FormDataResponseWriteService(FormDataRecordPreparationService preparation,
    FormResponseSessionService responses, IDataRecordMutationReceiptSource receipts)
{
    public async Task<FormDataWriteJournalResult> PrepareAsync(Guid formID, Guid responseID, Guid workbookID,
        Guid tableID, Guid recordID, Guid operationID, CancellationToken cancellationToken = default)
    {
        var prepared = await preparation.PrepareAsync(formID, responseID, workbookID, tableID, recordID, operationID,
            cancellationToken).ConfigureAwait(false);
        if (!prepared.Success) return new(false, prepared.Code, null);
        return await responses.QueuePreparedDataWriteAsync(prepared.Plan!, cancellationToken).ConfigureAwait(false);
    }

    public Task<FormDataWriteJournalReadResult> ReadAsync(Guid formID, Guid responseID, CancellationToken cancellationToken = default) =>
        responses.ReadDataWritesAsync(formID, responseID, cancellationToken);

    public Task<FormDataWriteJournalResult> ReconcileAsync(Guid formID, Guid responseID, Guid operationID,
        CancellationToken cancellationToken = default) =>
        responses.ReconcileDataWriteAsync(formID, responseID, operationID, receipts, cancellationToken);
}
