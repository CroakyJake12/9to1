namespace Haven.Application;

/// <summary>Durable actual owner observation only, never permission or execution authority.</summary>
public sealed record MapsOwnedMutationReceipt(int SchemaVersion, Guid OperationID, Guid StoreID,
    long ExpectedLibraryRevision, string PayloadSHA256);
