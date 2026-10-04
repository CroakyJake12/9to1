using Haven.Core;

namespace Haven.Application;

public enum DataWorkbookCommitPhase { Admission, Publication }
public sealed record DataWorkbookCommitContext(Guid StoreID, Guid WorkbookID, int ExpectedVersion,
    Guid ExpectedRevisionID, DataWorkbookCommitPhase Phase);

/// <summary>Trusted admission captured for one exact Data mutation. Implementations must not
/// re-enter the workbook repository while its durable lease is held.</summary>
public interface IDataWorkbookCommitAdmission
{
    ValueTask<bool> CheckAsync(DataWorkbookCommitContext context, CancellationToken cancellationToken);
}

/// <summary>The actual workbook repository's canonical root identity, coupled to its captured
/// data directory. Reading this may initialize the existing canonical settings root UUID.</summary>
public interface IDataWorkbookStoreIdentitySource
{
    ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken cancellationToken);
}

/// <summary>Sensitive cross-app mutations require this port; no unguarded Save fallback.</summary>
public sealed record DataWorkbookStoreEvidence(ResourceStoreIdentity Identity, string Fingerprint, bool IsEmpty);
public interface IDataWorkbookStoreEvidenceSource : IDataWorkbookStoreIdentitySource
{
    ValueTask<DataWorkbookStoreEvidence?> ReadStoreEvidenceAsync(CancellationToken cancellationToken);
}

public interface IDataWorkbookCommitAuthority
{
    ValueTask<IDataWorkbookCommitAdmission?> CaptureAsync(Guid storeID, Guid workbookID, int expectedVersion,
        Guid expectedRevisionID, string actionID, AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken);
}

public interface IDataGuardedWorkbookRepository : IDataWorkbookRepository, IDataWorkbookStoreEvidenceSource
{
    Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, IDataWorkbookCommitAdmission admission,
        CancellationToken cancellationToken);
}
