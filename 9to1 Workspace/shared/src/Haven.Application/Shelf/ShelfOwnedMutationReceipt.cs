namespace Haven.Application.Shelf;

/// <summary>Durable actual owner observation only, never permission or execution authority.</summary>
public sealed record ShelfOwnedMutationReceipt(int SchemaVersion, Guid OperationID, Guid StoreID,
    long ExpectedLibraryRevision, string PayloadSHA256);
