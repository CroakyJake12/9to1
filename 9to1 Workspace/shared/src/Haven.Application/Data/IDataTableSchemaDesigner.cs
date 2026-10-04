using Haven.Core;

namespace Haven.Application;

public sealed record DataTableSchemaReview(string RequestID, DataTableSchemaUpdateIntent Intent);
public sealed record DataTableSchemaDesignerCommit(bool Committed, string Code, DataWorkbook? Workbook, bool AuditRecorded);

/// <summary>An owning actual-store review port. A draft and a pending request grant no write capability.</summary>
public interface IDataTableSchemaDesigner
{
    Task<DataTableSchemaReview> ReviewAsync(Guid workbookID, Guid tableID, int expectedVersion, Guid expectedRevision,
        long? expectedSchemaRevision, IReadOnlyList<DataFieldDefinition> fields, IReadOnlyList<DataKeyDefinition> keys,
        CancellationToken cancellationToken = default);
    Task<DataTableSchemaDesignerCommit> CommitAsync(DataTableSchemaReview review, CancellationToken cancellationToken = default);
}
