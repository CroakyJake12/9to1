namespace Haven.Application;

/// <summary>Trusted source admission for declared cross-app provenance. Origin metadata alone never
/// proves access to a retained Forms response or authorizes its use by a Data mutation.</summary>
public interface IDataRecordMutationOriginAuthority
{
    ValueTask<IDataWorkbookCommitAdmission?> CaptureAsync(DataRecordUpdateIntent intent,
        AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken);
}
