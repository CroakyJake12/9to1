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
            DeviceId = Guid.NewGuid().ToString("D"),
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
            if (store.Manifest.SchemaVersion == 0) await store.MigrateSchemaZeroToOneAsync(cancellationToken);
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

    internal async Task<DenRecord?> ReadRevisionAsync(string namespaceId, string id, long revision,
        IDenAccessPolicy access, string principalId, CancellationToken cancellationToken = default)
    {
        ValidateSegment(namespaceId, "namespace"); ValidateSegment(id, "record ID");
        if (revision < 1) return null;
        if (!await access.IsAllowedAsync(principalId, namespaceId, id, DenPermission.Read, cancellationToken))
            throw new DenException(DenErrorCode.Forbidden, "The principal cannot read this Den object history.");
        var path = Path.Combine(_root, "history", namespaceId, id, revision.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + ".json");
        if (!File.Exists(path)) return null;
        DenRecord record;
        try
        {
            record = JsonSerializer.Deserialize<DenRecord>(await File.ReadAllBytesAsync(path, cancellationToken), DenJson.Options)
                ?? throw new DenException(DenErrorCode.InvalidRecord, "A historical revision is empty.", recoverable: true);
            ValidateRecord(record);
        }
        catch (JsonException ex) { throw new DenException(DenErrorCode.InvalidRecord, $"A historical revision is malformed: {ex.Message}", recoverable: true); }
        if (record.Id != id || record.NamespaceId != namespaceId || record.Revision != revision)
            throw new DenException(DenErrorCode.InvalidRecord, "A historical revision failed its identity check.", recoverable: true);
        return record;
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

    internal async Task<T> SaveAsync<T>(T proposed, long expectedRevision, string operationId,
        IDenAccessPolicy access, string principalId, CancellationToken cancellationToken = default,
        bool preserveOrigin = false) where T : DenRecord
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

            if (proposed is not StorageQuotaRecord)
                await EnsureRecordQuotasAsync(proposed, destination, previous, cancellationToken);

            var committed = (T)(proposed with
            {
                Revision = checked(expectedRevision + 1),
                CreatedAtUtc = previous?.CreatedAtUtc ?? proposed.CreatedAtUtc,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                OriginDeviceId = preserveOrigin ? proposed.OriginDeviceId : Manifest.DeviceId
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
        await EnterLockAsync(cancellationToken);
        try
        {
        var requested = namespaceIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var ns in requested) EnsureNamespace(ns);
        var records = new List<DenRecord>();
        foreach (var ns in requested) records.AddRange(await ListAllPermittedAsync(ns, access, principalId, cancellationToken));
        var selectedBlobs = (includeBlobIds ?? new HashSet<string>()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var blobs = new List<PortableBlob>();
        foreach (var blobId in selectedBlobs)
        {
            ValidateSegment(blobId, "blob ID");
            var references = records.OfType<BlobReferenceRecord>().Where(reference => !reference.Deleted && reference.Sha256 == blobId).ToArray();
            if (references.Length == 0) throw new DenException(DenErrorCode.Forbidden, "A selected attachment is not referenced by an authorised exported object.");
            var path = BlobPath(blobId);
            if (!File.Exists(path)) throw new DenException(DenErrorCode.InvalidRecord, "A selected content-addressed attachment is missing.", recoverable: true);
            var blobBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var first = references[0];
            if (blobBytes.LongLength != first.Length || Hash(blobBytes) != first.Sha256) throw new DenException(DenErrorCode.InvalidRecord, "A selected attachment failed length or hash validation.", recoverable: true);
            _ = DulcheDen.RejectSecrets(Encoding.Latin1.GetString(blobBytes));
            blobs.Add(new PortableBlob(blobId, first.Sha256, first.MediaType, blobBytes.LongLength, Convert.ToBase64String(blobBytes)));
        }
        var payload = new PortableDenArchive(1, Manifest with
        {
            Namespaces = Manifest.Namespaces.Where(n => requested.Contains(n.Id)).ToArray(),
            OperationReceipts = new Dictionary<string, string>()
        },
            records.OrderBy(r => r.NamespaceId, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray(),
            selectedBlobs, blobs, "");
        var digest = Hash(JsonSerializer.SerializeToUtf8Bytes(payload, DenJson.Options));
        payload = payload with { ArchiveSha256 = digest };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, DenJson.Options);
        if (bytes.Length > 64 * 1024 * 1024) throw new DenException(DenErrorCode.InvalidArchive, "The portable snapshot exceeds 64 MiB.");
        return bytes;
        }
        finally { ExitLock(); }
    }

    public async Task RestoreRecordAsync(DenRecord record, long expectedRevision, string operationId,
        IDenAccessPolicy access, string principalId, CancellationToken cancellationToken = default) =>
        _ = await SaveAsync(record, expectedRevision, operationId, access, principalId, cancellationToken, preserveOrigin: true);

    internal async Task<DenManifest> CreateNamespaceAsync(DenNamespace value, long expectedManifestRevision,
        string operationId, IDenAccessPolicy access, string principalId, CancellationToken cancellationToken)
    {
        ThrowIfUnavailableForWrite();
        ValidateNamespaces([value]); ValidateSegment(operationId, "operation ID");
        if (!await access.IsAllowedAsync(principalId, Manifest.DenId, Manifest.DenId, DenPermission.Administer, cancellationToken))
            throw new DenException(DenErrorCode.Forbidden, "Only a Den administrator can create a namespace.");
        await EnterLockAsync(cancellationToken);
        try
        {
            var fingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(new { value, expectedManifestRevision }, DenJson.Options));
            if (Manifest.OperationReceipts.TryGetValue(operationId, out var previousFingerprint))
            {
                if (previousFingerprint != fingerprint) throw new DenException(DenErrorCode.IdempotencyMismatch, "The operation ID was already used for a different mutation.");
                return Manifest;
            }
            if (Manifest.Revision != expectedManifestRevision)
                throw new DenException(DenErrorCode.Conflict, "The Den manifest changed before the namespace operation.", recoverable: true, retryable: true);
            if (Manifest.Namespaces.Any(ns => ns.Id == value.Id)) throw new DenException(DenErrorCode.Conflict, "The namespace ID already exists.", recoverable: true);
            var updated = Manifest with { Revision = checked(Manifest.Revision + 1), Namespaces = Manifest.Namespaces.Append(value).ToArray() };
            updated = updated with { OperationReceipts = WithReceipt(updated.OperationReceipts, operationId, fingerprint) };
            var nextPath = Path.Combine(_root, "den.json.next");
            await WriteAtomicAsync(nextPath, JsonSerializer.SerializeToUtf8Bytes(updated, DenJson.Options), cancellationToken);
            File.Move(nextPath, _manifestPath, overwrite: true);
            Manifest = updated;
            CreateDirectories();
            return updated;
        }
        catch (IOException ex) { throw new DenException(DenErrorCode.StorageFailure, ex.Message, recoverable: true, retryable: true); }
        finally { ExitLock(); }
    }

    internal async Task<DenManifest> SetNamespaceSharingAsync(string namespaceId, bool shared,
        long expectedManifestRevision, string operationId, IDenAccessPolicy access, string principalId,
        CancellationToken cancellationToken)
    {
        ThrowIfUnavailableForWrite();
        ValidateSegment(namespaceId, "namespace"); ValidateSegment(operationId, "operation ID");
        if (!await access.IsAllowedAsync(principalId, Manifest.DenId, namespaceId, DenPermission.Administer, cancellationToken))
            throw new DenException(DenErrorCode.Forbidden, "Only a Den administrator can change namespace sharing.");
        await EnterLockAsync(cancellationToken);
        try
        {
            var fingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(new { namespaceId, shared, expectedManifestRevision }, DenJson.Options));
            if (Manifest.OperationReceipts.TryGetValue(operationId, out var previousFingerprint))
            {
                if (previousFingerprint != fingerprint) throw new DenException(DenErrorCode.IdempotencyMismatch, "The operation ID was already used for a different mutation.");
                return Manifest;
            }
            if (Manifest.Revision != expectedManifestRevision) throw new DenException(DenErrorCode.Conflict, "The Den manifest changed before the sharing operation.", recoverable: true, retryable: true);
            var current = Manifest.Namespaces.FirstOrDefault(ns => ns.Id == namespaceId)
                ?? throw new DenException(DenErrorCode.NotFound, "The namespace does not exist.");
            if (current.Shared == shared) return Manifest;
            var updated = Manifest with
            {
                Revision = checked(Manifest.Revision + 1),
                Namespaces = Manifest.Namespaces.Select(ns => ns.Id == namespaceId ? ns with { Shared = shared } : ns).ToArray()
            };
            updated = updated with { OperationReceipts = WithReceipt(updated.OperationReceipts, operationId, fingerprint) };
            await WriteAtomicAsync(_manifestPath, JsonSerializer.SerializeToUtf8Bytes(updated, DenJson.Options), cancellationToken);
            Manifest = updated;
            return updated;
        }
        catch (IOException ex) { throw new DenException(DenErrorCode.StorageFailure, ex.Message, recoverable: true, retryable: true); }
        finally { ExitLock(); }
    }

    internal async Task PurgeAsync<T>(string namespaceId, string id, string operationId,
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
            }
            var history = Path.Combine(_root, "history", namespaceId, id);
            if (Directory.Exists(history)) Directory.Delete(history, recursive: true);
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

    internal async Task WriteBlobAsync(string id, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        ThrowIfUnavailableForWrite();
        if (bytes.Length is 0 or > 32 * 1024 * 1024) throw new DenException(DenErrorCode.InvalidRecord, "An attachment must be from 1 byte to 32 MiB.");
        ValidateSegment(id, "blob ID");
        var digest = Hash(bytes.Span);
        if (!string.Equals(id, digest, StringComparison.OrdinalIgnoreCase)) throw new DenException(DenErrorCode.InvalidRecord, "A blob ID must equal the content SHA-256.");
        _ = DulcheDen.RejectSecrets(Encoding.Latin1.GetString(bytes.Span));
        await EnterLockAsync(cancellationToken);
        try
        {
            var path = BlobPath(id);
            if (File.Exists(path))
            {
                var existing = await File.ReadAllBytesAsync(path, cancellationToken);
                if (Hash(existing) != digest) throw new DenException(DenErrorCode.InvalidRecord, "The content-addressed blob path contains invalid data.");
                return;
            }
            var currentBytes = Directory.EnumerateFiles(Path.Combine(_root, "blobs"), "*.blob").Sum(file => new FileInfo(file).Length);
            var globalQuota = Manifest.Namespaces.Sum(GetAttachmentQuota);
            if (currentBytes + bytes.Length > globalQuota)
                throw new DenException(DenErrorCode.RetentionBlocked, "The Den attachment storage quota would be exceeded.", recoverable: true);
            await WriteAtomicAsync(path, bytes.ToArray(), cancellationToken);
        }
        finally { ExitLock(); }
    }

    internal async Task<byte[]> ReadBlobAsync(string id, CancellationToken cancellationToken)
    {
        ValidateSegment(id, "blob ID");
        var path = BlobPath(id);
        if (!File.Exists(path)) throw new DenException(DenErrorCode.NotFound, "The attachment blob does not exist.");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (Hash(bytes) != id) throw new DenException(DenErrorCode.InvalidRecord, "The attachment blob failed its content hash check.", recoverable: true);
        return bytes;
    }

    internal async Task<IReadOnlyList<BlobReferenceRecord>> ListBlobReferencesAsync(string namespaceId,
        IDenAccessPolicy access, string principalId, CancellationToken cancellationToken) =>
        (await ListAsync<BlobReferenceRecord>(namespaceId, access, principalId, cancellationToken)).ToArray();

    internal async Task<long> GetBlobLengthAsync(string id, CancellationToken cancellationToken) =>
        (await ReadBlobAsync(id, cancellationToken)).LongLength;

    internal async Task<HashSet<string>> GetAllActiveBlobIdsAsync(CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var namespaceId in Manifest.Namespaces.Select(item => item.Id))
        {
            var directory = Path.Combine(_root, "records", namespaceId);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
            {
                var record = await ReadRecordAsync(file, cancellationToken);
                if (record is BlobReferenceRecord { Deleted: false } reference) result.Add(reference.Sha256);
            }
        }
        return result;
    }

    internal async Task<HashSet<string>> GetActiveBlobIdsAsync(string namespaceId, CancellationToken cancellationToken)
    {
        EnsureNamespace(namespaceId);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directory = Path.Combine(_root, "records", namespaceId);
        if (!Directory.Exists(directory)) return result;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
        {
            var record = await ReadRecordAsync(file, cancellationToken);
            if (record is BlobReferenceRecord { Deleted: false } reference) result.Add(reference.Sha256);
        }
        return result;
    }

    internal async Task<long> DeleteUnreferencedBlobsAsync(IReadOnlySet<string> requestedKeep,
        CancellationToken cancellationToken)
    {
        ThrowIfUnavailableForWrite();
        await EnterLockAsync(cancellationToken);
        try
        {
            var keep = await GetAllActiveBlobIdsAsync(cancellationToken);
            keep.UnionWith(requestedKeep);
            long deleted = 0;
            foreach (var path in Directory.EnumerateFiles(Path.Combine(_root, "blobs"), "*.blob"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = Path.GetFileNameWithoutExtension(path);
                if (keep.Contains(id)) continue;
                deleted = checked(deleted + new FileInfo(path).Length);
                File.Delete(path);
            }
            return deleted;
        }
        catch (IOException ex) { throw new DenException(DenErrorCode.StorageFailure, ex.Message, recoverable: true, retryable: true); }
        finally { ExitLock(); }
    }

    private string BlobPath(string id)
    {
        ValidateSegment(id, "blob ID");
        if (!Regex.IsMatch(id, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)) throw new DenException(DenErrorCode.InvalidRecord, "Blob ID must be a SHA-256 digest.");
        return Path.Combine(_root, "blobs", id.ToLowerInvariant() + ".blob");
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
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), record.Id, StringComparison.Ordinal) ||
                !string.Equals(new DirectoryInfo(Path.GetDirectoryName(path)!).Parent?.Name, record.NamespaceId, StringComparison.Ordinal))
                throw new DenException(DenErrorCode.InvalidRecord, "A record identity does not match its storage path.", recoverable: true);
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
            if (cancellationToken.IsCancellationRequested) { ProcessGate.Release(); cancellationToken.ThrowIfCancellationRequested(); }
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
        foreach (var name in new[] { "records", "journal", "transactions", "blobs", "history", "backups" }) Directory.CreateDirectory(Path.Combine(_root, name));
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
        }
        return result;
    }

    private static void ValidateManifest(DenManifest manifest, string root)
    {
        if (!Guid.TryParse(manifest.DenId, out _) || !Guid.TryParse(manifest.DeviceId, out _) || manifest.FormatVersion < 1 || manifest.SchemaVersion < 0)
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

    private async Task EnsureRecordQuotasAsync(DenRecord proposed, string destination, DenRecord? previous,
        CancellationToken cancellationToken)
    {
        var quotaPath = RecordPath(proposed.NamespaceId, "storage-quota");
        var quota = File.Exists(quotaPath)
            ? await ReadRecordAsync(quotaPath, cancellationToken) as StorageQuotaRecord
            : null;
        var metadataLimit = quota?.MetadataBytes ?? 64L * 1024 * 1024;
        var journalLimit = quota?.JournalBytes ?? 32L * 1024 * 1024;
        var recordsPath = Path.Combine(_root, "records", proposed.NamespaceId);
        long metadataUsed = Directory.Exists(recordsPath)
            ? Directory.EnumerateFiles(recordsPath, "*.json", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length)
            : 0;
        var historyPath = Path.Combine(_root, "history", proposed.NamespaceId);
        if (Directory.Exists(historyPath)) metadataUsed = checked(metadataUsed + Directory.EnumerateFiles(historyPath, "*.json", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length));
        metadataUsed = checked(metadataUsed - (previous is null ? 0 : new FileInfo(destination).Length) +
            JsonSerializer.SerializeToUtf8Bytes<DenRecord>(proposed, DenJson.Options).LongLength);
        if (metadataUsed > metadataLimit) throw new DenException(DenErrorCode.RetentionBlocked, "The Den metadata quota would be exceeded.", recoverable: true);
        var journalPath = Path.Combine(_root, "journal");
        var journalUsed = Directory.EnumerateFiles(journalPath, "*.json").Sum(path => new FileInfo(path).Length);
        if (journalUsed + 256 > journalLimit) throw new DenException(DenErrorCode.RetentionBlocked, "The Den journal quota would be exceeded.", recoverable: true);
    }

    private async Task MigrateSchemaZeroToOneAsync(CancellationToken cancellationToken)
    {
        await EnterLockAsync(cancellationToken);
        try
        {
            if (Manifest.SchemaVersion != 0) return;
            var backupDirectory = Path.Combine(_root, "backups", "pre-migration-0-to-1-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupDirectory);
            CopyDirectory(_root, backupDirectory, _root);
            var migrated = Manifest with { SchemaVersion = 1, DeviceId = Guid.TryParse(Manifest.DeviceId, out _) ? Manifest.DeviceId : Guid.NewGuid().ToString("D") };
            await WriteAtomicAsync(_manifestPath, JsonSerializer.SerializeToUtf8Bytes(migrated, DenJson.Options), cancellationToken);
            Manifest = migrated;
        }
        catch (IOException ex) { throw new DenException(DenErrorCode.StorageFailure, "Schema migration failed and the previous Den was preserved: " + ex.Message, recoverable: true, retryable: true); }
        finally { ExitLock(); }
    }

    private static void CopyDirectory(string source, string destination, string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            var name = info.Name;
            if (Path.GetFullPath(directory) == Path.GetFullPath(destination) || name is "backups" or ".den-write-lock") continue;
            var target = Path.Combine(destination, name);
            Directory.CreateDirectory(target);
            CopyDirectory(directory, target, root);
        }
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var info = new FileInfo(file);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(destination, info.Name);
            File.Copy(file, target, overwrite: false);
        }
        _ = root;
    }

    private long GetAttachmentQuota(DenNamespace ns)
    {
        var quotaPath = RecordPath(ns.Id, "storage-quota");
        if (!File.Exists(quotaPath)) return 128L * 1024 * 1024;
        try
        {
            var record = JsonSerializer.Deserialize<DenRecord>(File.ReadAllBytes(quotaPath), DenJson.Options);
            return record is StorageQuotaRecord quota ? quota.AttachmentBytes : 128L * 1024 * 1024;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { throw new DenException(DenErrorCode.InvalidRecord, "A storage quota record is invalid.", recoverable: true); }
    }

    private static string RecordKind(string id) => "objects";
    private static int CompareVersion(string left, string right)
    {
        if (!Version.TryParse(left, out var l) || !Version.TryParse(right, out var r)) throw new DenException(DenErrorCode.InvalidManifest, "A Den version string is invalid.");
        return l.CompareTo(r);
    }
    private static IReadOnlyDictionary<string, string> WithReceipt(IReadOnlyDictionary<string, string> current, string operationId, string fingerprint)
    {
        var copy = new Dictionary<string, string>(current, StringComparer.Ordinal) { [operationId] = fingerprint };
        return copy;
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

public sealed record PortableBlob(string Id, string Sha256, string MediaType, long Length, string ContentBase64);
public sealed record PortableDenArchive(int FormatVersion, DenManifest Manifest,
    IReadOnlyList<DenRecord> Records, IReadOnlyList<string> IncludedBlobIds, IReadOnlyList<PortableBlob> Blobs,
    string ArchiveSha256);
