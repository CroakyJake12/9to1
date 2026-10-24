namespace HavenOS.Apps.Stacks;

/// <summary>Opaque receipt issued by the same actual store. It is neither a Home grant nor a persisted identity.</summary>
public interface IStackOriginalSourceRevision { }

public sealed record StackOriginalSourceLoad(StackManifest Manifest, IStackOriginalSourceRevision Revision);

/// <summary>Optional original-source CAS and metadata-projection path. Existing non-files test/provider stores keep their existing contract.</summary>
public interface IStackOriginalSourceProjectStore : IStackProjectStore
{
    Task<StackOriginalSourceLoad> LoadOriginalAsync(CancellationToken cancellationToken = default);
    Task ValidateOriginalRevisionAsync(IStackOriginalSourceRevision originalRevision, CancellationToken cancellationToken = default);
    Task<StackLineagePage> ReadLineagePageOriginalAsync(IStackOriginalSourceRevision? originalRevision,
        int pageIndex, int pageSize, Guid? requestedDomainId = null, bool locateRequestedDomain = false,
        CancellationToken cancellationToken = default);
    Task<StackProjectSummary> ReadProjectSummaryOriginalAsync(IStackOriginalSourceRevision? originalRevision,
        CancellationToken cancellationToken = default);
    Task<StackConflictProposalPage> ReadConflictProposalsPageOriginalAsync(IStackOriginalSourceRevision? originalRevision,
        Guid domainId, int pageIndex, int pageSize, Guid? requestedProposalId = null,
        bool locateRequestedProposal = false, CancellationToken cancellationToken = default);
    Task<IStackOriginalSourceRevision> SaveOriginalAsync(StackManifest manifest,
        IStackOriginalSourceRevision originalRevision, CancellationToken cancellationToken = default);
}
