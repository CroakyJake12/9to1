using Haven.Core;

namespace Haven.Application;

public sealed record DataWorkbookSummary(Guid Id, string Title, DateTimeOffset UpdatedAt, int Version, int SheetCount, int QueryCount, bool RecoveredFromBackup);
public sealed record DataSaveResult(Guid WorkbookId, int Version, DateTimeOffset SavedAt, string CurrentPath, string BackupPath);
public sealed record DataQueryResult(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, bool Truncated, string SourceDescription);

/// <summary>Stable Data workbooks that are owned by other Haven apps and open in the Data app.</summary>
public static class DataWorkbookAppLinks
{
    public static Guid MapsSavedPlaces { get; } = Guid.Parse("f1000000-0000-4000-8000-000000000001");
    public static Guid FormsResponses { get; } = Guid.Parse("f1000000-0000-4000-8000-000000000002");
}

public interface IDataWorkbookRepository
{
    Task<IReadOnlyList<DataWorkbookSummary>> ListAsync(CancellationToken cancellationToken);
    Task<DataWorkbook?> LoadAsync(Guid workbookId, CancellationToken cancellationToken);
    Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, CancellationToken cancellationToken);
    Task DeleteAsync(Guid workbookId, CancellationToken cancellationToken);
}

public interface IDataWorkbookQueryService
{
    Task<DataQueryResult> ExecuteReadOnlyAsync(DataWorkbook workbook, string sql, int maxRows, CancellationToken cancellationToken);
}

public interface IDataWorkbookFormatService
{
    IReadOnlyList<string> ImportExtensions { get; }
    IReadOnlyList<string> ExportExtensions { get; }
    Task<DataWorkbook> ImportAsync(string sourcePath, CancellationToken cancellationToken);
    Task<string> ExportAsync(DataWorkbook workbook, string destinationPath, CancellationToken cancellationToken);
}
