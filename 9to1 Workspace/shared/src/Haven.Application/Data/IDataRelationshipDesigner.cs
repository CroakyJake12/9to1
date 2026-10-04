using Haven.Core;

namespace Haven.Application;

public sealed record DataRelationshipReview(string RequestID, DataRelationshipUpdateIntent Intent);
public sealed record DataRelationshipDesignerCommit(bool Committed, string Code, DataWorkbook? Workbook, bool AuditRecorded);

/// <summary>Actual owning review and persistence for canonical relationships; drawing a connection grants no capability.</summary>
public interface IDataRelationshipDesigner
{
    Task<DataRelationshipReview> ReviewAsync(Guid workbookID, int expectedVersion, Guid expectedRevision,
        DataRelationshipMutationKind kind, DataRelationshipDefinition relationship, long? expectedRelationshipRevision,
        CancellationToken cancellationToken = default);
    Task<DataRelationshipDesignerCommit> CommitAsync(DataRelationshipReview review, CancellationToken cancellationToken = default);
}
