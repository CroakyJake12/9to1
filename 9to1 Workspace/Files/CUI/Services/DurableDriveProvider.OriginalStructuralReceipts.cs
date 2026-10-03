namespace HavenOS.Files;

/// <summary>Read-only journal observation. A copied receipt is never dispatch authority.</summary>
public sealed record FilesOriginalStructuralReceipt(Guid StoreId, FilesOperation Operation, FilesChangeEvent Change);

public sealed partial class DurableDriveProvider
{
    /// <summary>Observes the original existing owning store without adoption or rewriting. This is
    /// also usable by the trusted owner auditing an already-admitted operation after execution retires.</summary>
    public async Task<FilesOriginalStructuralReceipt?> GetOriginalStructuralReceiptAsync(Guid expectedStoreId,
        FilesOperationId originalOperationId, CancellationToken cancellationToken = default)
    {
        if (originalOperationId.Value == Guid.Empty) throw new ArgumentException("Select the original operation.", nameof(originalOperationId));
        var state = await _store.ReadExistingAsync(cancellationToken).ConfigureAwait(false);
        RequireOriginalReadStore(state, expectedStoreId);
        if (state.Operations is null || state.Events is null)
            throw new InvalidDataException("The original Files journal is incomplete.");
        var operation = state.Operations.SingleOrDefault(item => item.Id == originalOperationId);
        var changes = state.Events.Where(item => item.OperationId == originalOperationId).ToArray();
        if (operation is null)
        {
            if (changes.Length != 0) throw new InvalidDataException("A Files change has no matching original operation.");
            return null;
        }
        if (operation.State != FilesOperationState.Committed || operation.ResultRevisionId is null || changes.Length != 1)
            throw new InvalidDataException("The original Files operation has no unique committed change.");
        var change = changes[0];
        if (operation.ActorId != _owner || change.ActorId != operation.ActorId || change.ItemId != operation.ItemId ||
            change.Kind != operation.Operation || change.BaseRevisionId != operation.BaseRevisionId ||
            change.ResultRevisionId != operation.ResultRevisionId || change.Cursor.Revision != operation.Sequence ||
            change.Metadata is not { } metadata || metadata.Id != operation.ItemId ||
            metadata.CurrentRevisionId != operation.ResultRevisionId || metadata.LocationId != Location.Id ||
            metadata.OwnerPrincipalId != _owner)
            throw new InvalidDataException("The original Files operation and change disagree.");
        RequireOriginalReadItem(metadata);
        return new(expectedStoreId, operation, change);
    }
}
