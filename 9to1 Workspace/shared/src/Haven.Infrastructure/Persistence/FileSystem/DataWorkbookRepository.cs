using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class DataWorkbookRepository : IDataGuardedWorkbookRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _root;
    public DataWorkbookRepository(IAppPaths paths) { ArgumentNullException.ThrowIfNull(paths); _root = Path.Combine(paths.DataDirectory, "Data", "Workbooks"); }

    public async Task<IReadOnlyList<DataWorkbookSummary>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); if (!Directory.Exists(_root)) return []; var summaries = new List<DataWorkbookSummary>();
        foreach (var directory in Directory.EnumerateDirectories(_root)) { cancellationToken.ThrowIfCancellationRequested(); if (!Guid.TryParse(Path.GetFileName(directory), out var id)) continue; var workbook = await LoadAsync(id, cancellationToken).ConfigureAwait(false); if (workbook is null) continue; summaries.Add(new(workbook.Id, workbook.Title, workbook.UpdatedAt, workbook.Version, workbook.Sheets.Count, workbook.Queries.Count, workbook.Recovery.RecoveredFromBackup)); }
        return summaries.OrderByDescending(item => item.UpdatedAt).ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<DataWorkbook?> LoadAsync(Guid workbookId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); var (current, backup) = Paths(workbookId); var workbook = await TryLoadAsync(current, workbookId, cancellationToken).ConfigureAwait(false); if (workbook is not null) return workbook; workbook = await TryLoadAsync(backup, workbookId, cancellationToken).ConfigureAwait(false); if (workbook is null) return null; workbook.Recovery.RecoveredFromBackup = true; workbook.Recovery.RecoveredAt = DateTimeOffset.UtcNow; workbook.Recovery.Message = "Recovered the previous valid workbook after the current file could not be read."; return workbook;
    }

    public Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, CancellationToken cancellationToken) =>
        SaveGuardedCoreAsync(workbook, reason, null, cancellationToken);

    public async Task DeleteAsync(Guid workbookId, CancellationToken cancellationToken)
    {
        await WithRootLeaseAsync(async () =>
        {
            var current = await LoadAsync(workbookId, cancellationToken).ConfigureAwait(false);
            if (current?.RecordViews.Count > 0 || current?.Metadata.ContainsKey("data.recordSession.v1") == true)
                throw new UnauthorizedAccessException("Record databases require an explicit Data-owned delete review.");
            var directory = WorkbookDirectory(workbookId); DemandOrdinaryDirectory(directory);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DataWorkbook?> TryLoadAsync(string path, Guid expectedId, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try { await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan); var workbook = await JsonSerializer.DeserializeAsync<DataWorkbook>(stream, JsonOptions, cancellationToken).ConfigureAwait(false); if (workbook is null || workbook.Id != expectedId || workbook.SchemaVersion <= 0 || workbook.SchemaVersion > DataWorkbook.CurrentSchemaVersion) return null; workbook.Normalize(); return workbook; } catch (JsonException) { return null; } catch (InvalidDataException) { return null; }
    }

    private static void ValidateForSave(DataWorkbook workbook) { if (workbook.Id == Guid.Empty) throw new InvalidDataException("A workbook must have a stable identifier."); if (workbook.SchemaVersion <= 0 || workbook.SchemaVersion > DataWorkbook.CurrentSchemaVersion) throw new InvalidDataException("This workbook schema version is not supported by this 9to1 build."); if (workbook.Sheets is null || workbook.Queries is null) throw new InvalidDataException("A workbook must contain sheet and query collections."); }
    private (string CurrentPath, string BackupPath) Paths(Guid id) { var directory = WorkbookDirectory(id); return (Path.Combine(directory, "current.json"), Path.Combine(directory, "previous.json")); }
    private string WorkbookDirectory(Guid id) => Path.Combine(_root, id.ToString("D"));
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}
