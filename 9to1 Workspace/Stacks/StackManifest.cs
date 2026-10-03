using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HavenOS.Apps.Stacks;

public sealed class StackManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public StackStorageMode StorageMode { get; set; }
    public StackAuthorityMode AuthorityMode { get; set; }
    public bool RequireTwoFactorForPrune { get; set; } = true;
    public bool RequireTwoFactorForPurge { get; set; } = true;
    public long DeletedRetentionTicks { get; set; } = TimeSpan.FromDays(30).Ticks;
    public Guid MainDomainId { get; set; }
    public Guid? ActiveDomainId { get; set; }
    public long RevisionSequence { get; set; }
    public List<StackDomainRecord> Domains { get; set; } = [];
    public List<StackRevision> Revisions { get; set; } = [];
    public List<StackConflict> Conflicts { get; set; } = [];
    public List<StackConflictProposal> ConflictProposals { get; set; } = [];
    public List<StackRoot> Roots { get; set; } = [];
    public List<StackSubroot> Subroots { get; set; } = [];
    public List<StackFreeze> Freezes { get; set; } = [];
    public List<StackAuditEntry> Audit { get; set; } = [];
    public List<StackSyncCheckpoint> SyncCheckpoints { get; set; } = [];
    public List<string> RetainedObjectIds { get; set; } = [];
    public string? ManagedDependencyCommitId { get; set; }
}

public sealed class StackDomainRecord
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public StackDomainKind Kind { get; set; }
    public Guid? ParentId { get; set; }
    public Guid BaseRevisionId { get; set; }
    public Guid? HeadRevisionId { get; set; }
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Dictionary<string, StackResource?> BaseTree { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, StackMutation> LocalChanges { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, StackMutation> WorkingChanges { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Guid> ChildDomainIds { get; set; } = [];
}

public sealed record StackSyncCheckpoint(
    Guid Id,
    DateTimeOffset Timestamp,
    StackAuthorityMode Authority,
    string? FilesRevision,
    string? GitHubRevision,
    bool Verified,
    bool HasConflict,
    string? ErrorCode);

public interface IStackProjectStore
{
    Task CreateAsync(StackManifest manifest, CancellationToken cancellationToken = default);
    Task<StackManifest> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(StackManifest manifest, CancellationToken cancellationToken = default);
    string ProjectDirectory { get; }
}

/// <summary>
/// A durable Files-backed Stack manifest store. Metadata is written atomically and the managed layout is validated on every open.
/// </summary>
public sealed class JsonFileStackProjectStore : IStackProjectStore
{
    public const string ManifestRelativePath = ".branches/stack.manifest.json";
    public const string RootsRelativePath = ".roots/roots.json";
    public const string CompanionRef = "stack/twigs-and-leaves-dependency";
    public const string PendingSaveRecoveryRelativePath = ".branches/save.recovery.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _expectedManifestSha256;
    private readonly Func<CancellationToken, Task>? _validateCurrentBinding;

    public JsonFileStackProjectStore(string projectDirectory, Func<CancellationToken, Task>? validateCurrentBinding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ProjectDirectory = Path.GetFullPath(projectDirectory);
        _validateCurrentBinding = validateCurrentBinding;
    }

    public string ProjectDirectory { get; }

    public async Task CreateAsync(StackManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(ProjectDirectory) || File.Exists(ProjectDirectory))
            {
                throw new StackFailureException(StackFailureCode.DuplicateIdentity,
                    "The selected project folder already exists and cannot be initialized as a new Stack project. Existing data has been preserved.", ProjectDirectory, recoverable: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ProjectDirectory)!);
            string stagingDirectory = ProjectDirectory + ".stack-initialize-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(stagingDirectory);
                Directory.CreateDirectory(Path.Combine(stagingDirectory, ".branches", "domains"));
                Directory.CreateDirectory(Path.Combine(stagingDirectory, ".source"));
                Directory.CreateDirectory(Path.Combine(stagingDirectory, ".roots"));
                await WriteAtomicallyAsync(Path.Combine(stagingDirectory, ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar)), Serialize(manifest), cancellationToken).ConfigureAwait(false);
                await WriteAtomicallyAsync(Path.Combine(stagingDirectory, RootsRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    JsonSerializer.SerializeToUtf8Bytes(new StackRootsDocument { SchemaVersion = StackManifest.CurrentSchemaVersion }, JsonOptions), cancellationToken).ConfigureAwait(false);
                await WriteAtomicallyAsync(Path.Combine(stagingDirectory, ".branches", "README.md"),
                    "# Stack-managed storage\n\nThis folder contains versioned Stack metadata. Do not edit or delete it through ordinary project editing.\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(Path.Combine(stagingDirectory, ".branches", "storage.lock"), Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
                Directory.Move(stagingDirectory, ProjectDirectory);
                _expectedManifestSha256 = SHA256.HashData(await File.ReadAllBytesAsync(ManifestPath, cancellationToken).ConfigureAwait(false));
            }
            finally
            {
                if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
            }
        }
        catch (StackFailureException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StackFailureException(StackFailureCode.MaterialisationFailed,
                "The Stack project folder could not be initialized. Existing data has been preserved.", ProjectDirectory, recoverable: true, retryable: true, exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackManifest> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ValidateManagedLayout();
            await using var storageLock = await AcquireStorageLockAsync(createIfMissing: false, cancellationToken).ConfigureAwait(false);
            DemandNoPendingSave();
            byte[] bytes = await File.ReadAllBytesAsync(ManifestPath, cancellationToken).ConfigureAwait(false);
            StackManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<StackManifest>(bytes, JsonOptions)
                    ?? throw new JsonException("Manifest content was empty.");
            }
            catch (JsonException exception)
            {
                throw new StackFailureException(StackFailureCode.SourceCorrupt,
                    "The Stack manifest is not valid JSON. The source and managed metadata have been left untouched.", ManifestRelativePath, recoverable: true, innerException: exception);
            }

            if (manifest.SchemaVersion != StackManifest.CurrentSchemaVersion)
            {
                throw new StackFailureException(StackFailureCode.SchemaVersionUnsupported,
                    $"Stack manifest schema {manifest.SchemaVersion} is not supported by this build; no migration was attempted.", ManifestRelativePath, recoverable: true);
            }

            ValidateManifest(manifest);
            await ValidateRootsDocumentAsync(manifest, cancellationToken).ConfigureAwait(false);
            // Legacy projects need no metadata migration merely to open them. A concurrent
            // writer that introduced a lock while this reader opened the legacy layout is
            // detected by a second canonical manifest read; an unstable view is unavailable.
            var observedSha = SHA256.HashData(bytes);
            var finalSha = SHA256.HashData(await File.ReadAllBytesAsync(ManifestPath, cancellationToken).ConfigureAwait(false));
            if (!CryptographicOperations.FixedTimeEquals(observedSha, finalSha))
                throw new StackFailureException(StackFailureCode.RevisionConflict,
                    "The canonical Stack changed while it was being read. Reopen before using this view.",
                    ManifestRelativePath, recoverable: true, retryable: true);
            _expectedManifestSha256 = observedSha;
            return manifest;
        }
        catch (StackFailureException)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            throw new StackFailureException(StackFailureCode.ManagedMetadataMissing,
                "The required Stack manifest is missing. This project is in recovery mode and has not been treated as ordinary source.", ManifestRelativePath, recoverable: true, innerException: exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(StackManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateManifest(manifest);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ValidateManagedLayout();
            // Reject a stale legacy writer before introducing its first coordination file.
            if (_validateCurrentBinding is not null) await _validateCurrentBinding(cancellationToken).ConfigureAwait(false);
            DemandExpectedRevision(SHA256.HashData(await File.ReadAllBytesAsync(ManifestPath, cancellationToken).ConfigureAwait(false)));
            await using var storageLock = await AcquireStorageLockAsync(createIfMissing: true, cancellationToken).ConfigureAwait(false);
            if (_validateCurrentBinding is not null) await _validateCurrentBinding(cancellationToken).ConfigureAwait(false);
            DemandNoPendingSave();
            var previousManifest = await File.ReadAllBytesAsync(ManifestPath, cancellationToken).ConfigureAwait(false);
            var currentSha = SHA256.HashData(previousManifest);
            DemandExpectedRevision(currentSha);
            var previousRoots = await File.ReadAllBytesAsync(RootsPath, cancellationToken).ConfigureAwait(false);
            var serialized = Serialize(manifest);
            var roots = new StackRootsDocument
            {
                SchemaVersion = StackManifest.CurrentSchemaVersion,
                Roots = manifest.Roots.ToList(),
            };
            var nextRoots = JsonSerializer.SerializeToUtf8Bytes(roots, JsonOptions);
            // Preserve BOTH sides before publishing either independently atomic metadata file.
            // This record is recovery evidence, never a replay grant. An interrupted save
            // requires explicit owner inspection instead of treating mixed roots as valid.
            var recovery = new StackPendingSaveRecovery(1, manifest.ProjectId,
                previousManifest, previousRoots, serialized, nextRoots,
                Hash(previousManifest), Hash(previousRoots), Hash(serialized), Hash(nextRoots));
            await WriteAtomicallyAsync(PendingSaveRecoveryPath,
                JsonSerializer.SerializeToUtf8Bytes(recovery, JsonOptions), cancellationToken).ConfigureAwait(false);
            await WriteAtomicallyAsync(RootsPath, nextRoots, cancellationToken).ConfigureAwait(false);
            if (_validateCurrentBinding is not null) await _validateCurrentBinding(cancellationToken).ConfigureAwait(false);
            await WriteAtomicallyAsync(ManifestPath, serialized, cancellationToken).ConfigureAwait(false);
            File.Delete(PendingSaveRecoveryPath);
            _expectedManifestSha256 = SHA256.HashData(serialized);
        }
        catch (StackFailureException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StackFailureException(StackFailureCode.MaterialisationFailed,
                "The Stack save did not acknowledge completion. Inspect preserved recovery evidence before any further edit; do not repeat the operation.", ManifestRelativePath, recoverable: true, retryable: true, exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Token of this store's last successfully loaded/committed canonical manifest; not an action grant.</summary>
    public string? LoadedRevisionToken => _expectedManifestSha256 is { } sha ? Convert.ToHexString(sha).ToLowerInvariant() : null;

    private void DemandExpectedRevision(byte[] currentSha)
    {
        if (_expectedManifestSha256 is null || !CryptographicOperations.FixedTimeEquals(currentSha, _expectedManifestSha256))
            throw new StackFailureException(StackFailureCode.RevisionConflict,
                "The canonical Stack project changed after this engine opened it. Reload before proposing another edit.",
                ManifestRelativePath, recoverable: true, retryable: true);
    }

    private async Task<FileStream?> AcquireStorageLockAsync(bool createIfMissing, CancellationToken cancellationToken)
    {
        var path = Path.Combine(ProjectDirectory, ".branches", "storage.lock");
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (new FileInfo(path).LinkTarget is not null || (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
                throw new StackFailureException(StackFailureCode.SourceCorrupt, "The Stack storage lock cannot be a symbolic link.", path, recoverable: true);
            try { return new FileStream(path, createIfMissing ? FileMode.OpenOrCreate : FileMode.Open,
                createIfMissing ? FileAccess.ReadWrite : FileAccess.Read, FileShare.None); }
            catch (FileNotFoundException) when (!createIfMissing) { return null; }
            catch (IOException) when (attempt < 100) { await Task.Delay(20, cancellationToken).ConfigureAwait(false); }
        }
    }

    private string ManifestPath => Path.Combine(ProjectDirectory, ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
    private string RootsPath => Path.Combine(ProjectDirectory, RootsRelativePath.Replace('/', Path.DirectorySeparatorChar));
    private string PendingSaveRecoveryPath => Path.Combine(ProjectDirectory, PendingSaveRecoveryRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record StackPendingSaveRecovery(int SchemaVersion, Guid ProjectId,
        byte[] PreviousManifest, byte[] PreviousRoots, byte[] NextManifest, byte[] NextRoots,
        string PreviousManifestSha256, string PreviousRootsSha256,
        string NextManifestSha256, string NextRootsSha256);

    /// <summary>Read-only observed metadata evidence. This record never authorizes repair or replay.</summary>
    public Task<StackPendingSaveInspection?> InspectPendingSaveAsync(CancellationToken cancellationToken = default) =>
        InspectPendingSaveCoreAsync(null, cancellationToken);

    /// <summary>Read-only evidence for the caller's original canonical project. Identity matching grants no permission.</summary>
    public Task<StackPendingSaveInspection?> InspectPendingSaveAsync(Guid expectedProjectId, CancellationToken cancellationToken = default)
    {
        if (expectedProjectId == Guid.Empty) throw new ArgumentException("An original canonical project identity is required.", nameof(expectedProjectId));
        return InspectPendingSaveCoreAsync(expectedProjectId, cancellationToken);
    }

    private async Task<StackPendingSaveInspection?> InspectPendingSaveCoreAsync(Guid? expectedProjectId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ValidateManagedLayout();
            await using var storageLock = await AcquireStorageLockAsync(createIfMissing: false, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(PendingSaveRecoveryPath)) return null;
            byte[] journalBytes = await File.ReadAllBytesAsync(PendingSaveRecoveryPath, cancellationToken).ConfigureAwait(false);
            StackPendingSaveRecovery recovery;
            try
            {
                recovery = JsonSerializer.Deserialize<StackPendingSaveRecovery>(journalBytes, JsonOptions)
                    ?? throw new JsonException("Recovery evidence was empty.");
            }
            catch (JsonException error)
            {
                throw new StackFailureException(StackFailureCode.SourceCorrupt,
                    "Stack recovery evidence is not valid JSON. No repair was attempted.", PendingSaveRecoveryRelativePath,
                    recoverable: true, innerException: error);
            }
            if (recovery.SchemaVersion != 1 || recovery.ProjectId == Guid.Empty ||
                recovery.PreviousManifest is null || recovery.PreviousRoots is null || recovery.NextManifest is null || recovery.NextRoots is null ||
                Hash(recovery.PreviousManifest) != recovery.PreviousManifestSha256 || Hash(recovery.PreviousRoots) != recovery.PreviousRootsSha256 ||
                Hash(recovery.NextManifest) != recovery.NextManifestSha256 || Hash(recovery.NextRoots) != recovery.NextRootsSha256)
                throw new StackFailureException(StackFailureCode.SourceCorrupt,
                    "Stack recovery evidence has invalid identities or content hashes. No repair was attempted.",
                    PendingSaveRecoveryRelativePath, recoverable: true);
            if (expectedProjectId is Guid originalProject && originalProject != recovery.ProjectId)
                throw new UnauthorizedAccessException("The recovery evidence belongs to another canonical Stack project.");
            ValidateRecoverySide(recovery.PreviousManifest, recovery.PreviousRoots, recovery.ProjectId);
            ValidateRecoverySide(recovery.NextManifest, recovery.NextRoots, recovery.ProjectId);
            byte[] manifestBytes = await File.ReadAllBytesAsync(ManifestPath, cancellationToken).ConfigureAwait(false);
            byte[] rootsBytes = await File.ReadAllBytesAsync(RootsPath, cancellationToken).ConfigureAwait(false);
            string manifestSha = Hash(manifestBytes), rootsSha = Hash(rootsBytes);
            // Refuse an unstable actual read even when an external editor ignores storage.lock.
            if (Hash(await File.ReadAllBytesAsync(ManifestPath, cancellationToken).ConfigureAwait(false)) != manifestSha ||
                Hash(await File.ReadAllBytesAsync(RootsPath, cancellationToken).ConfigureAwait(false)) != rootsSha ||
                Hash(await File.ReadAllBytesAsync(PendingSaveRecoveryPath, cancellationToken).ConfigureAwait(false)) != Hash(journalBytes))
                throw new StackFailureException(StackFailureCode.RevisionConflict,
                    "The Stack recovery evidence changed during inspection. No repair was attempted.",
                    PendingSaveRecoveryRelativePath, recoverable: true, retryable: true);
            return new(recovery.ProjectId, Hash(journalBytes), manifestSha, rootsSha,
                manifestSha == recovery.PreviousManifestSha256, manifestSha == recovery.NextManifestSha256,
                rootsSha == recovery.PreviousRootsSha256, rootsSha == recovery.NextRootsSha256);
        }
        finally { _gate.Release(); }
    }

    private static void ValidateRecoverySide(byte[] manifestBytes, byte[] rootsBytes, Guid expectedProjectId)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<StackManifest>(manifestBytes, JsonOptions)
                ?? throw new JsonException("Recovery manifest was empty.");
            if (manifest.SchemaVersion != StackManifest.CurrentSchemaVersion || manifest.ProjectId != expectedProjectId ||
                manifest.Domains is null || manifest.Roots is null ||
                manifest.Domains.Any(static domain => domain is null || domain.BaseTree is null || domain.LocalChanges is null))
                throw new JsonException("Recovery manifest has an invalid canonical identity or schema.");
            ValidateManifest(manifest);
            var roots = JsonSerializer.Deserialize<StackRootsDocument>(rootsBytes, JsonOptions)
                ?? throw new JsonException("Recovery root index was empty.");
            if (roots.SchemaVersion != StackManifest.CurrentSchemaVersion || roots.Roots is null)
                throw new JsonException("Recovery root index has an invalid schema.");
            var domainIds = manifest.Domains.Select(static domain => domain.Id).ToHashSet();
            foreach (var list in new[] { manifest.Roots, roots.Roots })
            {
                var ids = new HashSet<Guid>();
                foreach (var root in list)
                {
                    if (root is null || root.Id == Guid.Empty || !ids.Add(root.Id) || !domainIds.Contains(root.OwnerDomainId) ||
                        root.Paths is null || root.Paths.Any(path => path is null || StackPath.Normalize(path) != path) ||
                        root.Paths.Distinct(StringComparer.Ordinal).Count() != root.Paths.Count)
                        throw new JsonException("Recovery root index has an invalid canonical owner, identity or path.");
                }
            }
            var recorded = roots.Roots.OrderBy(static root => root.Id).ToArray();
            var canonical = manifest.Roots.OrderBy(static root => root.Id).ToArray();
            if (!JsonSerializer.SerializeToUtf8Bytes(recorded, JsonOptions).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(canonical, JsonOptions)))
                throw new JsonException("Recovery root index differs from its canonical manifest.");
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new StackFailureException(StackFailureCode.SourceCorrupt,
                "Stack recovery evidence has invalid canonical project or root metadata. No repair was attempted.",
                PendingSaveRecoveryRelativePath, recoverable: true, innerException: error);
        }
    }

    private void DemandNoPendingSave()
    {
        if (File.Exists(PendingSaveRecoveryPath))
            throw new StackFailureException(StackFailureCode.RecoveryStateUncertain,
                "A previous Stack save has preserved recovery evidence. Source and metadata remain untouched; explicit owner inspection is required before opening or editing this project.",
                PendingSaveRecoveryRelativePath, recoverable: true);
    }


    private void ValidateManagedLayout()
    {
        foreach (string relative in new[] { ".branches", ".branches/domains", ".source", ".roots" })
        {
            string path = Path.GetFullPath(Path.Combine(ProjectDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(ProjectDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new StackFailureException(StackFailureCode.SourceCorrupt, "A managed Stack path resolves outside the project directory.", relative, recoverable: true);
            }

            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new StackFailureException(StackFailureCode.SourceCorrupt, "Managed Stack infrastructure cannot redirect through a symbolic link.", relative, recoverable: true);

            if (!Directory.Exists(path))
            {
                throw new StackFailureException(StackFailureCode.ManagedMetadataMissing,
                    $"Required Stack-managed infrastructure is missing: {relative}.", relative, recoverable: true);
            }
        }

        foreach (var metadataPath in new[] { ManifestPath, RootsPath, PendingSaveRecoveryPath })
            if (new FileInfo(metadataPath).LinkTarget is not null ||
                (File.Exists(metadataPath) && (File.GetAttributes(metadataPath) & FileAttributes.ReparsePoint) != 0))
                throw new StackFailureException(StackFailureCode.SourceCorrupt, "Stack metadata cannot redirect through a symbolic link.", metadataPath, recoverable: true);

        if (!File.Exists(ManifestPath) || !File.Exists(RootsPath))
        {
            string missing = !File.Exists(ManifestPath) ? ManifestRelativePath : RootsRelativePath;
            throw new StackFailureException(StackFailureCode.ManagedMetadataMissing,
                $"Required Stack-managed metadata is missing: {missing}.", missing, recoverable: true);
        }
    }

    private async Task ValidateRootsDocumentAsync(StackManifest manifest, CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(RootsPath, cancellationToken).ConfigureAwait(false);
            StackRootsDocument roots = JsonSerializer.Deserialize<StackRootsDocument>(bytes, JsonOptions)
                ?? throw new JsonException("Root metadata was empty.");
            if (roots.SchemaVersion != StackManifest.CurrentSchemaVersion)
            {
                throw new StackFailureException(StackFailureCode.SchemaVersionUnsupported, "Root metadata uses an unsupported schema version.", RootsRelativePath, recoverable: true);
            }

            if (!roots.Roots.Select(static root => root.Id).Order().SequenceEqual(manifest.Roots.Select(static root => root.Id).Order()))
            {
                throw new StackFailureException(StackFailureCode.ManagedMetadataMissing, "The derived roots.json index does not match the canonical manifest; project recovery is required.", RootsRelativePath, recoverable: true);
            }
        }
        catch (JsonException exception)
        {
            throw new StackFailureException(StackFailureCode.SourceCorrupt, "Root metadata is not valid JSON; project recovery is required.", RootsRelativePath, recoverable: true, innerException: exception);
        }
    }

    private static void ValidateManifest(StackManifest manifest)
    {
        if (manifest.ProjectId == Guid.Empty || manifest.MainDomainId == Guid.Empty || manifest.Domains.Count == 0)
        {
            throw new StackFailureException(StackFailureCode.SourceCorrupt, "The Stack manifest does not contain a valid project and main domain identity.", ManifestRelativePath, recoverable: true);
        }

        StackDomainRecord[] mains = manifest.Domains.Where(static domain => domain.Kind == StackDomainKind.Main).ToArray();
        if (mains.Length != 1 || mains[0].Id != manifest.MainDomainId || mains[0].ParentId is not null)
        {
            throw new StackFailureException(StackFailureCode.SourceCorrupt, "A Stack manifest must contain exactly one parentless Main domain matching MainDomainId.", ManifestRelativePath, recoverable: true);
        }

        var ids = new HashSet<Guid>();
        foreach (StackDomainRecord domain in manifest.Domains)
        {
            if (domain.Id == Guid.Empty || domain.ProjectId != manifest.ProjectId || !ids.Add(domain.Id))
            {
                throw new StackFailureException(StackFailureCode.SourceCorrupt, "The Stack manifest contains an empty or duplicate DomainID.", ManifestRelativePath, recoverable: true);
            }

            foreach (string path in domain.BaseTree.Keys.Concat(domain.LocalChanges.Keys))
            {
                if (!StackPath.Normalize(path).Equals(path, StringComparison.Ordinal))
                {
                    throw new StackFailureException(StackFailureCode.SourceCorrupt, "A Stack manifest contains a non-canonical project path.", path, recoverable: true);
                }
            }
        }

        foreach (StackDomainRecord domain in manifest.Domains.Where(static domain => domain.Kind != StackDomainKind.Main))
        {
            StackDomainRecord? parent = manifest.Domains.FirstOrDefault(candidate => candidate.Id == domain.ParentId);
            if (parent is null || !StackHierarchy.IsAllowed(parent.Kind, domain.Kind))
            {
                throw new StackFailureException(StackFailureCode.SourceCorrupt, "A Stack domain has a missing or invalid parent type.", domain.Id.ToString("D"), recoverable: true);
            }
        }
    }

    private static byte[] Serialize(StackManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);

    private static async Task WriteAtomicallyAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private sealed class StackRootsDocument
    {
        public int SchemaVersion { get; set; }
        public List<StackRoot> Roots { get; set; } = [];
    }
}

public static class StackHierarchy
{
    public static bool IsAllowed(StackDomainKind parent, StackDomainKind child) => (parent, child) switch
    {
        (StackDomainKind.Main, StackDomainKind.Branch) => true,
        (StackDomainKind.Branch, StackDomainKind.Twig) => true,
        (StackDomainKind.Twig, StackDomainKind.Leaf) => true,
        _ => false,
    };

    public static StackDomainKind ChildKind(StackDomainKind parent) => parent switch
    {
        StackDomainKind.Main => StackDomainKind.Branch,
        StackDomainKind.Branch => StackDomainKind.Twig,
        StackDomainKind.Twig => StackDomainKind.Leaf,
        _ => throw new StackFailureException(StackFailureCode.InvalidHierarchy, "Leaf domains cannot have children.", parent.ToString()),
    };
}

/// <summary>Observed actual file hashes only; not an action capability or a recovery plan.</summary>
public sealed record StackPendingSaveInspection(Guid ProjectId, string RecoveryEvidenceSha256,
    string ObservedManifestSha256, string ObservedRootsSha256,
    bool ManifestMatchesPrevious, bool ManifestMatchesProposed,
    bool RootsMatchPrevious, bool RootsMatchProposed);
