using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class DataWorkbookRepository : IDataWorkbookRepository
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

    public async Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateForSave(workbook);
        workbook.Normalize();
        // Capture before waiting for the cross-process lease. Failed persistence must not publish a
        // new revision into the caller's working document or overwrite a competing writer's content.
        var captured = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook, JsonOptions), JsonOptions)
            ?? throw new InvalidDataException("The workbook snapshot is missing.");
        captured.Normalize();
        var expected = captured.Version;
        if (expected < 0) throw new InvalidDataException("Workbook revision must be nonnegative.");
        var directory = WorkbookDirectory(captured.Id);
        await using var lease = await AcquireWriteLeaseAsync(captured.Id, cancellationToken).ConfigureAwait(false);
        var (current, backup) = Paths(captured.Id);
        var existing = await LoadAsync(captured.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null && (File.Exists(current) || File.Exists(backup)))
            throw new InvalidDataException("Stored workbook is unreadable; preserve it for explicit recovery.");
        if ((existing?.Version ?? 0) != expected || (existing?.RevisionId ?? Guid.Empty) != captured.RevisionId) throw new DataWorkbookRevisionConflictException(captured.Id, expected, existing?.Version);
        if (existing?.Recovery.RecoveredFromBackup == true && !captured.Recovery.RecoveredFromBackup)
            throw new InvalidDataException("Reload the recovered workbook before replacing its unreadable current file.");
        Directory.CreateDirectory(directory);
        captured.UpdatedAt = DateTimeOffset.UtcNow;
        captured.Version = checked(expected + 1);
        captured.RevisionId = Guid.NewGuid();
        captured.Metadata["lastSaveReason"] = reason ?? string.Empty;
        captured.Recovery.RecoveredFromBackup = false; captured.Recovery.RecoveredAt = null; captured.Recovery.Message = string.Empty;
        var temporary = Path.Combine(directory, $"current-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, captured, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            _ = await TryLoadAsync(temporary, captured.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The workbook did not pass its persistence verification read.");
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(current))
            {
                if (existing?.Recovery.RecoveredFromBackup == true)
                    File.Move(current, Path.Combine(directory, $"unreadable-current-{Guid.NewGuid():N}.json"), overwrite: false);
                else File.Copy(current, backup, overwrite: true);
            }
            File.Move(temporary, current, overwrite: true);
            // Publish only persistence metadata. Edits made by the caller during the wait remain
            // in its buffer for a subsequent revision rather than silently being discarded.
            workbook.Version = captured.Version; workbook.RevisionId = captured.RevisionId; workbook.UpdatedAt = captured.UpdatedAt;
            workbook.Metadata["lastSaveReason"] = reason ?? string.Empty;
            workbook.Recovery.RecoveredFromBackup = false; workbook.Recovery.RecoveredAt = null; workbook.Recovery.Message = string.Empty;
            return new(captured.Id, captured.Version, captured.UpdatedAt, current, backup);
        }
        finally { TryDelete(temporary); }
    }

    private async Task<FileStream> AcquireWriteLeaseAsync(Guid workbookId, CancellationToken token)
    {
        var locks = Path.Combine(_root, ".locks");
        Directory.CreateDirectory(locks);
        var path = Path.Combine(locks, workbookId.ToString("D") + ".lock");
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None); }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline) { await Task.Delay(20, token).ConfigureAwait(false); }
        }
    }

    public async Task DeleteAsync(Guid workbookId, CancellationToken cancellationToken)
    {
        await using var lease = await AcquireWriteLeaseAsync(workbookId, cancellationToken).ConfigureAwait(false);
        var directory = WorkbookDirectory(workbookId);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private async Task<DataWorkbook?> TryLoadAsync(string path, Guid expectedId, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try { await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan); var workbook = await JsonSerializer.DeserializeAsync<DataWorkbook>(stream, JsonOptions, cancellationToken).ConfigureAwait(false); if (workbook is null || workbook.Id != expectedId || workbook.SchemaVersion <= 0 || workbook.SchemaVersion > DataWorkbook.CurrentSchemaVersion || workbook.SchemaVersion >= 4 && workbook.Version > 0 && workbook.RevisionId == Guid.Empty) return null; workbook.Normalize(); return workbook; } catch (JsonException) { return null; } catch (InvalidDataException) { return null; }
    }

    private static void ValidateForSave(DataWorkbook workbook) { if (workbook.Id == Guid.Empty) throw new InvalidDataException("A workbook must have a stable identifier."); if (workbook.SchemaVersion <= 0 || workbook.SchemaVersion > DataWorkbook.CurrentSchemaVersion) throw new InvalidDataException("This workbook schema version is not supported by this Haven build."); if (workbook.Sheets is null || workbook.Queries is null) throw new InvalidDataException("A workbook must contain sheet and query collections."); }
    private (string CurrentPath, string BackupPath) Paths(Guid id) { var directory = WorkbookDirectory(id); return (Path.Combine(directory, "current.json"), Path.Combine(directory, "previous.json")); }
    private string WorkbookDirectory(Guid id) => Path.Combine(_root, id.ToString("D"));
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}

public sealed class DataWorkbookRevisionConflictException(Guid workbookId, int expectedVersion, int? currentVersion)
    : InvalidOperationException("DataWorkbookRevisionConflict: reload the current workbook before saving.")
{
    public string Code => "RevisionConflict";
    public Guid WorkbookId { get; } = workbookId;
    public int ExpectedVersion { get; } = expectedVersion;
    public int? CurrentVersion { get; } = currentVersion;
}
