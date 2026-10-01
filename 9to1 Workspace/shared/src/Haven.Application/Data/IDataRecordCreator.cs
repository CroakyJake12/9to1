using Haven.Core;

namespace Haven.Application;

/// <summary>Opaque owner-issued display origin, never an authorization grant or JSON transport.</summary>
public interface IDataRecordDisplaySelection { }
public sealed record DataRecordDisplaySnapshot(DataWorkbook Workbook, [property: System.Text.Json.Serialization.JsonIgnore] IDataRecordDisplaySelection Selection);
public sealed record DataRecordCreateReview(string RequestID, DataRecordCreateIntent Intent, [property: System.Text.Json.Serialization.JsonIgnore] IDataRecordDisplaySelection? DisplaySelection = null);
public sealed record DataRecordCreatorCommit(bool Committed, string Code, DataWorkbook? Workbook, bool AuditRecorded, DataFormulaRecalculationReport? Calculation = null);
public interface IDataRecordCreator
{
    Task<DataRecordDisplaySnapshot> LoadForDisplayAsync(Guid workbookID, CancellationToken cancellationToken = default)
        => Task.FromException<DataRecordDisplaySnapshot>(new UnauthorizedAccessException("Trusted Data display identity is unavailable."));
    Task<DataRecordCreateReview> ReviewAsync(IDataRecordDisplaySelection selection, Guid workbookID, Guid tableID,
        Guid recordID, int expectedVersion, Guid expectedRevision, IReadOnlyDictionary<Guid, DataScalarRecordValue> values,
        CancellationToken cancellationToken = default)
        => Task.FromException<DataRecordCreateReview>(new UnauthorizedAccessException("An owning Data display selection is required."));
    Task<DataRecordCreateReview> ReviewAsync(Guid workbookID, Guid tableID, Guid recordID, int expectedVersion,
        Guid expectedRevision, IReadOnlyDictionary<Guid, DataScalarRecordValue> values, CancellationToken cancellationToken = default);
    Task<DataRecordCreatorCommit> CommitAsync(DataRecordCreateReview review, CancellationToken cancellationToken = default);
}
