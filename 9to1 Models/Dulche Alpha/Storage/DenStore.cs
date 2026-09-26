using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NineToOne.Dulche.Den;

public sealed class DenStore : IAsyncDisposable
{
    private const string CurrentVersion = "1.0";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    private static readonly Regex SafeSegment = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly SemaphoreSlim ProcessGate = new(1, 1);
    private readonly string _root;
    private readonly string _manifestPath;
    private readonly string _lockPath;
    private readonly bool _readOnly;
    private bool _disposed;

    private DenStore(string root, DenManifest manifest, bool readOnly)
    {
        _root = Path.GetFullPath(root);
        _manifestPath = Path.Combine(_root, "den.json");
        _lockPath = Path.Combine(_root, ".den-write-lock");
        Manifest = manifest;
        _readOnly = readOnly;
    }

    public DenManifest Manifest { get; private set; }
    public bool IsReadOnly => _readOnly;
    public string RootPath => _root;

    public static async Task<DenStore> CreateAsync(string root, IEnumerable<DenNamespace> namespaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fullRoot = Path.GetFullPath(root);
        Directory.CreateDirectory(fullRoot);
        var manifestPath = Path.Combine(fullRoot, "den.json");
        if (File.Exists(manifestPath)) throw new DenException(DenErrorCode.Conflict, "A Den already exists at this location.", recoverable: true);
        var manifest = new DenManifest
        {
            DenId = Guid.NewGuid().ToString("D"),
            Namespaces = ValidateNamespaces(namespaces).ToArray(),
            StorageLocations = new Dictionary<string, string>
            {
                ["records"] = "records", ["journal"] = "journal", ["transactions"] = "transactions", ["blobs"] = "blobs", ["history"] = "history"
            }
        };
        var store = new DenStore(fullRoot, manifest, false);
        store.CreateDirectories();
        await WriteAtomicAsync(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, DenJson.Options), cancellationToken);
        return store;
    }

    public static async Task<DenStore> OpenAsync(string root, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fullRoot = Path.GetFullPath(root);
        var manifestPath = Path.Combine(fullRoot, "den.json");
        if (!File.Exists(manifestPath)) throw new DenException(DenErrorCode.InvalidManifest, "den.json is missing.", recoverable: true);
        DenManifest manifest;
        try
        {
            var bytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
            if (bytes.Length > 2 * 1024 * 1024) throw new DenException(DenErrorCode.InvalidManifest, "The Den manifest exceeds the supported size.");
            manifest = JsonSerializer.Deserialize<DenManifest>(bytes, DenJson.Options)
                ?? throw new DenException(DenErrorCode.InvalidManifest, "The Den manifest is empty.");
        }
        catch (JsonException ex) { throw new DenException(DenErrorCode.InvalidManifest, $"The Den manifest is invalid: {ex.Message}", recoverable: true); }

        ValidateManifest(manifest, fullRoot);
        var newer = manifest.SchemaVersion > 1 || CompareVersion(manifest.MinimumReaderVersion, CurrentVersion) > 0;
        var store = new DenStore(fullRoot, manifest, newer);
        if (!newer)
        {
            store.CreateDirectories();
            await store.RecoverTransactionsAsync(cancellationToken);
        }
        return store;
    }

    public async Task<T?> GetAsync<T>(string namespaceId, string id, IDenAccessPolicy access,
        string principalId, CancellationToken cancellationToken = default) where T : DenRecord
    {
        ValidateSegment(namespaceId, "namespace");
        ValidateSegment(id, "record ID");
        if (!await access.IsAllowedAsync(principalId, namespaceId, id, DenPermission.Read, cancellationToken))
            throw new DenException(DenErrorCode.Forbidden, "The principal cannot read this Den object.");
        var path = RecordPath(namespaceId, id);
        if (!File.Exists(path)) return null;
        var record = await ReadRecordAsync(path, cancellationToken);
        if (record is not T typed) throw new DenException(DenErrorCode.InvalidRecord, "The record has a different type than requested.");
        return typed;
    }

    public async Task<IReadOnlyList<T>> ListAsync<T>(string namespaceId, IDenAccessPolicy access,
        string principalId, CancellationToken cancellationToken = default) where T : DenRecord
    {
        ValidateSegment(namespaceId, "namespace");
        var allowed = Manifest.Namespaces.Any(ns => ns.Id == namespaceId);
        if (!allowed) throw new DenException(DenErrorCode.NotFound, "The namespace does not exist.");
        var directory = Path.Combine(_root, "records", namespaceId);
        if (!Directory.Exists(directory)) return [];
        var results = new List<T>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await ReadRecordAsync(file, cancellationToken);
            if (record is T typed && await access.IsAllowedAsync(principalId, namespaceId, typed.Id, DenPermission.Read, cancellationToken))
                results.Add(typed);
        }
        return results;
    }

    public async Task<T> SaveAsync<T>(T proposed, long expectedRevision, string operationId,
        IDenAccessPolicy access, string principalId, CancellationToken cancellationToken = default) where T : DenRecord
    {
        ThrowIfUnavailableForWrite();
        ValidateRecord(proposed);
        ValidateSegment(operationId, "operation ID");
        if (!await access.IsAllowedAsync(principalId, proposed.NamespaceId, proposed.Id, DenPermission.Write, cancellationToken))
            throw new DenException(DenErrorCode.Forbidden, "The principal cannot modify this Den object.");
        EnsureNamespace(proposed.NamespaceId);
        var fingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(new { proposed, expectedRevision }, DenJson.Options));
        await EnterLockAsync(cancellationToken);
        try
        {
            var idempotencyPath = Path.Combine(_root, "journal", operationId + ".json");
            if (File.Exists(idempotencyPath))
            {
                var prior = JsonSerializer.Deserialize<OperationReceipt>(await File.ReadAllBytesAsync(idempotencyPath, cancellationToken), DenJson.Options)!;
                if (prior.Fingerprint != fingerprint) throw new DenException(DenErrorCode.IdempotencyMismatch, "The operation ID was already used for different content.");
                var replayPath = RecordPath(proposed.NamespaceId, proposed.Id);
                return (T)await ReadRecordAsync(replayPath, cancellationToken);
            }

            var destination = RecordPath(proposed.NamespaceId, proposed.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var exists = File.Exists(destination);
            DenRecord? previous = exists ? await ReadRecordAsync(destination, cancellationToken) : null;
            if ((!exists && expectedRevision != 0) || (exists && previous!.Revision != expectedRevision))
                throw new DenException(DenErrorCode.Conflict, "The record changed since it was read.", recoverable: true, retryable: true);
            if (exists && (previous!.GetType() != proposed.GetType() || previous.NamespaceId != proposed.NamespaceId))
                throw new DenException(DenErrorCode.Conflict, "A stable record ID cannot change type or namespace.", recoverable: true);

            var committed = (T)(proposed with
            {
                Revision = checked(expectedRevision + 1),
                CreatedAtUtc = previous?.CreatedAtUtc ?? proposed.CreatedAtUtc,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            ValidateRecord(committed);
            var transactionId = Guid.NewGuid().ToString("N");
            var transactionDir = Path.Combine(_root, "transactions", transactionId);
            Directory.CreateDirectory(transactionDir);
            var metadata = new TransactionMetadata(operationId, fingerprint, proposed.NamespaceId, proposed.Id, exists);
            await WriteAtomicAsync(Path.Combine(transactionDir, "prepared.json"), JsonSerializer.SerializeToUtf8Bytes(metadata, DenJson.Options), cancellationToken);
            if (exists) await CopyDurableAsync(destination, Path.Combine(transactionDir, "previous.json"), cancellationToken);
            var staged = Path.Combine(transactionDir, "next.json");
            await WriteAtomicAsync(staged, JsonSerializer.SerializeToUtf8Bytes<DenRecord>(committed, DenJson.Options), cancellationToken);
            if (previous is not null)
            {
                var history = Path.Combine(_root, "history", proposed.NamespaceId, proposed.Id, previous.Revision.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + ".json");
                if (!File.Exists(history)) await CopyDurableAsync(destination, history, cancellationToken);
            }
            File.Move(staged, destination, overwrite: true);
            await WriteAtomicAsync(Path.Combine(transactionDir, "committed.json"), "{}"u8.ToArray(), cancellationToken);
            await WriteAtomicAsync(idempotencyPath, JsonSerializer.SerializeToUtf8Bytes(new OperationReceipt(fingerprint), DenJson.Options), cancellationToken);
            Directory.Delete(transactionDir, recursive: true);
            return committed;
        }
        catch (IOException ex) { throw new DenException(DenErrorCode.StorageFailure, ex.Message, recoverable: true, retryable: true); }
        finally { ExitLock(); }
    }

    public async Task<byte[]> ReadPortableSnapshotAsync(IEnumerable<string> namespaceIds, IDenAccessPolicy access,
        string principalId, IReadOnlySet<string>? includeBlobIds = null, CancellationToken cancellationToken = default)
    {
        var requested = namespaceIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var ns in requested) EnsureNamespace(ns);
        var records = new List<DenRecord>();
        foreach (var ns in requested) records.AddRange(await ListAllPermittedAsync(ns, access, principalId, cancellationToken));
        var payload = new PortableDenArchive(1, Manifest with { Namespaces = Manifest.Namespaces.Where(n => requested.Contains(n.Id)).ToArray() },
            records.OrderBy(r => r.NamespaceId, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray(),
            (includeBlobIds ?? new HashSet<string>()).Order(StringComparer.Ordinal).ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, DenJson.Options);
        if (bytes.Length > 64 * 1024 * 1024) throw new DenException(DenErrorCode.InvalidArchive, "The portable snapshot exceeds 64 MiB.");
        return bytes;
    }

    public async Task RestoreRecordAsync(DenRecord record, long expectedRevision, string operationId,
        IDenAccessPolicy access, string principalId, CancellationToken cancellationToken = default) =>
        _ = await SaveAsync(record, expectedRevision, operationId, access, principalId, cancellationToken);

    public async Task PurgeAsync<T>(string namespaceId, string id, string operationId,
        CancellationToken cancellationToken = default) where T : DenRecord
    {
        ThrowIfUnavailableForWrite();
        ValidateSegment(namespaceId, "namespace"); ValidateSegment(id, "record ID"); ValidateSegment(operationId, "operation ID");
        await EnterLockAsync(cancellationToken);
        try
        {
            var receiptPath = Path.Combine(_root, "journal", operationId + ".json");
            var fingerprint = Hash(Encoding.UTF8.GetBytes("purge|" + namespaceId + "|" + id));
            if (File.Exists(receiptPath))
            {
                var receipt = JsonSerializer.Deserialize<OperationReceipt>(await File.ReadAllBytesAsync(receiptPath, cancellationToken), DenJson.Options)!;
                if (receipt.Fingerprint != fingerprint) throw new DenException(DenErrorCode.IdempotencyMismatch, "The operation ID was already used for a different mutation.");
                return;
            }
            var path = RecordPath(namespaceId, id);
            if (File.Exists(path))
            {
                var record = await ReadRecordAsync(path, cancellationToken);
                if (record is not T) throw new DenException(DenErrorCode.InvalidRecord, "The purge target has an unexpected record type.");
                if (record is MemoryEntry memory && memory.Retention is not (MemoryRetentionState.SoftDeleted or MemoryRetentionState.Expired))
                    throw new DenException(DenErrorCode.RetentionBlocked, "Only a soft-deleted or expired memory can be permanently purged.");
                File.Delete(path);
                var history = Path.Combine(_root, "history", namespaceId, id);
                if (Directory.Exists(history)) Directory.Delete(history, recursive: true);
            }
            await WriteAtomicAsync(receiptPath, JsonSerializer.SerializeToUtf8Bytes(new OperationReceipt(fingerprint), DenJson.Options), cancellationToken);
        }
        catch (IOException ex) { throw new DenException(DenErrorCode.StorageFailure, ex.Message, recoverable: true, retryable: true); }
        finally { ExitLock(); }
    }

    public async Task<IReadOnlyList<DenRecord>> ListAllPermittedAsync(string namespaceId, IDenAccessPolicy access,
        string principalId, CancellationToken cancellationToken = default)
    {
        ValidateSegment(namespaceId, "namespace");
        var directory = Path.Combine(_root, "records", namespaceId);
        if (!Directory.Exists(directory)) return [];
        var result = new List<DenRecord>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await ReadRecordAsync(file, cancellationToken);
            if (await access.IsAllowedAsync(principalId, namespaceId, record.Id, DenPermission.Read, cancellationToken)) result.Add(record);
        }
        return result;
    }

    internal string RecordPath(string namespaceId, string id)
    {
        ValidateSegment(namespaceId, "namespace"); ValidateSegment(id, "record ID");
        var path = Path.GetFullPath(Path.Combine(_root, "records", namespaceId, RecordKind(id), id + ".json"));
        var prefix = Path.GetFullPath(Path.Combine(_root, "records", namespaceId)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new DenException(DenErrorCode.InvalidRecord, "The record path escapes its namespace.");
        return path;
    }

    internal static void ValidateSegment(string value, string label)
    {
        if (!SafeSegment.IsMatch(value) || value is "." or "..") throw new DenException(DenErrorCode.InvalidRecord, $"The {label} contains unsupported characters.");
    }

    private async Task<DenRecord> ReadRecordAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length > 8 * 1024 * 1024) throw new DenException(DenErrorCode.InvalidRecord, "A Den record exceeds 8 MiB.", recoverable: true);
        try
        {
            var record = JsonSerializer.Deserialize<DenRecord>(await File.ReadAllBytesAsync(path, cancellationToken), DenJson.Options)
                ?? throw new DenException(DenErrorCode.InvalidRecord, "A Den record is empty.", recoverable: true);
            ValidateRecord(record);
            return record;
        }
        catch (JsonException ex) { throw new DenException(DenErrorCode.InvalidRecord, $"A Den record is malformed: {ex.Message}", recoverable: true); }
    }

    private async Task RecoverTransactionsAsync(CancellationToken cancellationToken)
    {
        await EnterLockAsync(cancellationToken);
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(Path.Combine(_root, "transactions")))
            {
                var prepared = Path.Combine(dir, "prepared.json");
                if (!File.Exists(prepared)) { Directory.Delete(dir, true); continue; }
                var metadata = JsonSerializer.Deserialize<TransactionMetadata>(await File.ReadAllBytesAsync(prepared, cancellationToken), DenJson.Options)
                    ?? throw new DenException(DenErrorCode.StorageFailure, "A transaction journal entry is unreadable.", recoverable: true);
                var destination = RecordPath(metadata.NamespaceId, metadata.RecordId);
                var committed = File.Exists(Path.Combine(dir, "committed.json"));
                if (committed)
                {
                    var receipt = Path.Combine(_root, "journal", metadata.OperationId + ".json");
                    if (!File.Exists(receipt)) await WriteAtomicAsync(receipt, JsonSerializer.SerializeToUtf8Bytes(new OperationReceipt(metadata.Fingerprint), DenJson.Options), cancellationToken);
                }
                else if (metadata.HadPrevious && File.Exists(Path.Combine(dir, "previous.json")))
                    await CopyDurableAsync(Path.Combine(dir, "previous.json"), destination, cancellationToken);
                else if (!metadata.HadPrevious && File.Exists(destination)) File.Delete(destination);
                Directory.Delete(dir, recursive: true);
            }
        }
        finally { ExitLock(); }
    }

    private async Task EnterLockAsync(CancellationToken cancellationToken)
    {
        await ProcessGate.WaitAsync(cancellationToken);
        var until = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { _heldFileLock = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); return; }
            catch (IOException) when (DateTime.UtcNow < until) { await Task.Delay(40, cancellationToken); }
            catch (IOException) { ProcessGate.Release(); throw new DenException(DenErrorCode.StorageFailure, "Timed out waiting for the Den writer lock.", recoverable: true, retryable: true); }
        }
    }

    private FileStream? _heldFileLock;
    private void ExitLock() { _heldFileLock?.Dispose(); _heldFileLock = null; ProcessGate.Release(); }

    private void ThrowIfUnavailableForWrite()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_readOnly) throw new DenException(DenErrorCode.UnsupportedSchema, "This Den is newer than this reader and is open read-only.", recoverable: true);
    }

    private void CreateDirectories()
    {
        foreach (var name in new[] { "records", "journal", "transactions", "blobs", "history" }) Directory.CreateDirectory(Path.Combine(_root, name));
    }

    private void EnsureNamespace(string namespaceId)
    {
        ValidateSegment(namespaceId, "namespace");
        if (!Manifest.Namespaces.Any(n => n.Id == namespaceId)) throw new DenException(DenErrorCode.NotFound, "The namespace does not exist.");
    }

    private static IReadOnlyList<DenNamespace> ValidateNamespaces(IEnumerable<DenNamespace> namespaces)
    {
        var result = namespaces?.ToArray() ?? throw new ArgumentNullException(nameof(namespaces));
        if (result.Length == 0 || result.Select(n => n.Id).Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw new DenException(DenErrorCode.InvalidManifest, "A Den requires unique namespaces.");
        foreach (var ns in result)
        {
            ValidateSegment(ns.Id, "namespace");
            if (ns.Kind is not ("personal" or "team" or "organisation" or "app")) throw new DenException(DenErrorCode.InvalidManifest, "Namespace kind is not supported.");
            if (ns.Shared && ns.Kind == "personal") throw new DenException(DenErrorCode.InvalidManifest, "Personal namespaces cannot be shared implicitly.");
        }
        return result;
    }

    private static void ValidateManifest(DenManifest manifest, string root)
    {
        if (!Guid.TryParse(manifest.DenId, out _) || manifest.FormatVersion < 1 || manifest.SchemaVersion < 1)
            throw new DenException(DenErrorCode.InvalidManifest, "The Den identity or version fields are invalid.");
        if (manifest.FormatVersion > 1) throw new DenException(DenErrorCode.UnsupportedSchema, "The Den format version is newer than this reader supports.", recoverable: true);
        var namespaces = ValidateNamespaces(manifest.Namespaces);
        foreach (var (key, relative) in manifest.StorageLocations)
        {
            ValidateSegment(key, "storage key");
            if (Path.IsPathRooted(relative) || relative.Contains(':')) throw new DenException(DenErrorCode.InvalidManifest, "Storage locations must be relative to the Den root.");
            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new DenException(DenErrorCode.InvalidManifest, "A storage location escapes the Den root.");
        }
        if (manifest.RequiredCapabilities.Any(capability => capability is not ("json-records-v1" or "journaled-writes-v1")))
            throw new DenException(DenErrorCode.CapabilityUnavailable, "The Den requires an unsupported capability.", recoverable: true);
        _ = namespaces;
    }

    private static void ValidateRecord(DenRecord record)
    {
        ValidateSegment(record.Id, "record ID"); ValidateSegment(record.NamespaceId, "namespace");
        if (record.Revision < 1 || record.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5) || record.UpdatedAtUtc < record.CreatedAtUtc)
            throw new DenException(DenErrorCode.InvalidRecord, "Record timestamps or revision are invalid.");
        if (record is MemoryEntry memory && (string.IsNullOrWhiteSpace(memory.Content) || string.IsNullOrWhiteSpace(memory.Category) || memory.Confidence is < 0 or > 1))
            throw new DenException(DenErrorCode.InvalidRecord, "Memory content, category or confidence is invalid.");
        if (record is ModelRecord model)
        {
            if (string.IsNullOrWhiteSpace(model.DisplayName) || string.IsNullOrWhiteSpace(model.ArtifactRevision)) throw new DenException(DenErrorCode.InvalidRecord, "Model name and artifact revision are required.");
            if (model.ArtifactSha256 is not null && !Regex.IsMatch(model.ArtifactSha256, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)) throw new DenException(DenErrorCode.InvalidRecord, "Model artifact hash must be SHA-256.");
        }
    }

    private static string RecordKind(string id) => "objects";
    private static int CompareVersion(string left, string right)
    {
        if (!Version.TryParse(left, out var l) || !Version.TryParse(right, out var r)) throw new DenException(DenErrorCode.InvalidManifest, "A Den version string is invalid.");
        return l.CompareTo(r);
    }
    private static string Hash(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value));

    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
        {
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }

    private static async Task CopyDurableAsync(string source, string destination, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
        await WriteAtomicAsync(destination, bytes, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private sealed record OperationReceipt(string Fingerprint);
    private sealed record TransactionMetadata(string OperationId, string Fingerprint, string NamespaceId, string RecordId, bool HadPrevious);
}

public sealed record PortableDenArchive(int FormatVersion, DenManifest Manifest,
    IReadOnlyList<DenRecord> Records, IReadOnlyList<string> IncludedBlobIds);
