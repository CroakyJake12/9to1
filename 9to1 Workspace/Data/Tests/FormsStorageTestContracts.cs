using Haven.Core;

namespace Haven.Application;

// This focused runtime project links the production storage source directly so its
// behavior remains verifiable while unrelated application projects are unavailable.
public sealed record DataWorkbookSummary(
    Guid Id,
    string Title,
    DateTimeOffset UpdatedAt,
    int Version,
    int SheetCount,
    int QueryCount,
    bool RecoveredFromBackup);

public sealed record DataSaveResult(
    Guid WorkbookId,
    int Version,
    DateTimeOffset SavedAt,
    string CurrentPath,
    string BackupPath);

public static class DataWorkbookAppLinks
{
    public static Guid FormsResponses { get; } = Guid.Parse("f1000000-0000-4000-8000-000000000002");
}

public interface IDataWorkbookRepository
{
    Task<IReadOnlyList<DataWorkbookSummary>> ListAsync(CancellationToken cancellationToken);
    Task<DataWorkbook?> LoadAsync(Guid workbookId, CancellationToken cancellationToken);
    Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, CancellationToken cancellationToken);
    Task DeleteAsync(Guid workbookId, CancellationToken cancellationToken);
}

public interface IAppPaths
{
    string DataDirectory { get; }
}
