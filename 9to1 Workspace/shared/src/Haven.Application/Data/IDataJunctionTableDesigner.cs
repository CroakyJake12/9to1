using Haven.Core;

namespace Haven.Application;

public sealed record DataJunctionTableReview(string RequestID, DataJunctionTableUpdateIntent Intent);
public sealed record DataJunctionTableDesignerCommit(bool Committed, string Code, DataWorkbook? Workbook, bool AuditRecorded);
public interface IDataJunctionTableDesigner
{
    Task<DataJunctionTableReview> ReviewAsync(Guid workbookID, int expectedVersion, Guid expectedRevision,
        DataJunctionTableDefinition definition, CancellationToken cancellationToken = default);
    Task<DataJunctionTableDesignerCommit> CommitAsync(DataJunctionTableReview review, CancellationToken cancellationToken = default);
}
