using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class DataWorkbookRepository
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RootGates = new(StringComparer.Ordinal);
    private const int MaximumWorkbookBytes = 64 * 1024 * 1024;
    internal string CanonicalRoot => Path.GetFullPath(_root);

    public Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, IDataWorkbookCommitAdmission admission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admission);
        return SaveGuardedCoreAsync(workbook, reason, admission, cancellationToken);
    }

    internal async Task<T> WithRootLeaseAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        var gate = RootGates.GetOrAdd(CanonicalRoot, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_root); DemandOrdinaryDirectory(_root);
            await using var lease = await AcquireFileLeaseAsync(Path.Combine(_root, ".write-lease"), token).ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    private static async Task<FileStream> AcquireFileLeaseAsync(string path, CancellationToken token)
    {
        var deadline = Environment.TickCount64 + 10000;
        while (true)
        {
            token.ThrowIfCancellationRequested(); DemandOrdinaryFile(path);
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (Environment.TickCount64 < deadline) { await Task.Delay(20, token).ConfigureAwait(false); }
        }
    }
    public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) => new(WithRootLeaseAsync(async () =>
    {
        var path = Path.Combine(_root, ".store.json"); DemandOrdinaryFile(path);
        if (File.Exists(path))
        {
            var existing = JsonSerializer.Deserialize<ResourceStoreIdentity>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
            if (existing is not { SchemaVersion: 1 } || existing.StoreId == Guid.Empty)
                throw new InvalidDataException("The canonical Data store identity is invalid.");
            return existing with { NewlyCreated = false };
        }
        var created = new ResourceStoreIdentity(1, Guid.NewGuid(), DateTimeOffset.UtcNow, false);
        await WriteNewJsonAsync(path, created, token).ConfigureAwait(false);
        return created with { NewlyCreated = true };
    }, token));

    public async ValueTask<DataWorkbookStoreEvidence?> ReadStoreEvidenceAsync(CancellationToken token)
    {
        var identity = await GetStoreIdentityAsync(token).ConfigureAwait(false);
        return await WithRootLeaseAsync(async () =>
        {
            var entries = new List<string>();
            foreach (var directory in Directory.EnumerateDirectories(_root).Order(StringComparer.Ordinal))
            {
                DemandOrdinaryDirectory(directory);
                if (!Guid.TryParse(Path.GetFileName(directory), out _)) continue;
                var path = Path.Combine(directory, "current.json");
                entries.Add(Path.GetFileName(directory) + ":" + (await FingerprintAsync(path, token).ConfigureAwait(false) ?? "missing"));
            }
            return new DataWorkbookStoreEvidence(identity, Convert.ToHexString(SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(string.Join("\n", entries)))), entries.Count == 0);
        }, token).ConfigureAwait(false);
    }

    internal async Task<string?> FingerprintAsync(Guid workbookID, CancellationToken token)
    {
        DemandOrdinaryDirectory(_root); DemandOrdinaryDirectory(WorkbookDirectory(workbookID));
        var (current, backup) = Paths(workbookID);
        var currentHash = await FingerprintAsync(current, token).ConfigureAwait(false);
        var backupHash = await FingerprintAsync(backup, token).ConfigureAwait(false);
        if (currentHash is null && backupHash is null) return null;
        // A backup can be the actual source of a recovered read. Bind both heads:
        // pinning only a corrupt/missing primary would miss replacement of that source.
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            "current:" + (currentHash ?? "missing") + "\nbackup:" + (backupHash ?? "missing"))));
    }
    private static async Task<string?> FingerprintAsync(string path, CancellationToken token)
    {
        DemandOrdinaryFile(path);
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (stream.Length > MaximumWorkbookBytes) throw new InvalidDataException("Data workbook exceeds 64 MiB.");
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }
    private async Task<DataSaveResult> SaveGuardedCoreAsync(DataWorkbook workbook, string reason,
        IDataWorkbookCommitAdmission? admission, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(workbook); token.ThrowIfCancellationRequested();
        var detached = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook, JsonOptions), JsonOptions)
            ?? throw new InvalidDataException("The Data workbook snapshot is unavailable.");
        ValidateForSave(detached); detached.Normalize();
        if (admission is null && detached.RecordViews.Count != 0)
            throw new UnauthorizedAccessException("Record-view workbooks require the Data-owned guarded session.");
        var identity = await GetStoreIdentityAsync(token).ConfigureAwait(false);
        return await WithRootLeaseAsync(async () =>
        {
            var directory = WorkbookDirectory(detached.Id); Directory.CreateDirectory(directory); DemandOrdinaryDirectory(directory);
            var (current, backup) = Paths(detached.Id); DemandOrdinaryFile(current); DemandOrdinaryFile(backup);
            var beforeHash = await FingerprintAsync(current, token).ConfigureAwait(false);
            var existing = await TryLoadAsync(current, detached.Id, token).ConfigureAwait(false);
            if (existing is null && detached.Recovery.RecoveredFromBackup)
                existing = await TryLoadAsync(backup, detached.Id, token).ConfigureAwait(false);
            if (existing is null ? detached.Version != 0 || beforeHash is not null && !detached.Recovery.RecoveredFromBackup :
                existing.Version != detached.Version || existing.RevisionId != detached.RevisionId)
                throw new InvalidOperationException("DataRevisionConflict: reopen the current workbook; no values were overwritten.");
            if (admission is null && (existing?.RecordViews.Count > 0 || existing?.Metadata.ContainsKey("data.recordSession.v1") == true))
                throw new UnauthorizedAccessException("Record-view workbooks require the Data-owned guarded session.");
            var context = new DataWorkbookCommitContext(identity.StoreId, detached.Id, detached.Version, detached.RevisionId, DataWorkbookCommitPhase.Admission);
            if (admission is not null && !await admission.CheckAsync(context, token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The original Data write admission is no longer current.");
            detached.Version = checked(detached.Version + 1); detached.RevisionId = Guid.NewGuid(); detached.UpdatedAt = DateTimeOffset.UtcNow;
            detached.Metadata["lastSaveReason"] = reason ?? "";
            detached.Recovery.RecoveredFromBackup = false; detached.Recovery.Message = ""; detached.Recovery.RecoveredAt = null;
            var temporary = Path.Combine(directory, "current-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, detached, JsonOptions, token).ConfigureAwait(false);
                    if (stream.Length > MaximumWorkbookBytes) throw new InvalidDataException("Data workbook exceeds 64 MiB.");
                    await stream.FlushAsync(token).ConfigureAwait(false); stream.Flush(true);
                }
                if (await TryLoadAsync(temporary, detached.Id, token).ConfigureAwait(false) is null) throw new InvalidDataException("Data verification read failed.");
                if (admission is not null && !await admission.CheckAsync(context with { Phase = DataWorkbookCommitPhase.Publication }, token).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("Data permission changed before publication.");
                if (beforeHash != await FingerprintAsync(current, token).ConfigureAwait(false))
                    throw new InvalidOperationException("DataRevisionConflict: workbook bytes changed before publication.");
                if (File.Exists(current))
                {
                    if (workbook.Recovery.RecoveredFromBackup) File.Move(current, Path.Combine(directory, "unreadable-current-" + Guid.NewGuid().ToString("N") + ".json"));
                    else File.Copy(current, backup, true);
                }
                File.Move(temporary, current, true);
                workbook.Version = detached.Version; workbook.RevisionId = detached.RevisionId; workbook.UpdatedAt = detached.UpdatedAt;
                workbook.Recovery = detached.Recovery; workbook.Metadata["lastSaveReason"] = detached.Metadata["lastSaveReason"];
                return new DataSaveResult(workbook.Id, workbook.Version, workbook.UpdatedAt, current, backup);
            }
            finally { TryDelete(temporary); }
        }, token).ConfigureAwait(false);
    }
    internal static void DemandOrdinaryDirectory(string path)
    {
        for (DirectoryInfo? directory = new DirectoryInfo(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Data root cannot traverse a linked directory.");
    }
    internal static void DemandOrdinaryFile(string path)
    { if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Data files cannot be links."); }
    internal static async Task WriteNewJsonAsync<T>(string path, T value, CancellationToken token)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value), token).ConfigureAwait(false); File.Move(temp, path, false); }
        finally { TryDelete(temp); }
    }
}
