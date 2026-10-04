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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileStackProjectStore(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ProjectDirectory = Path.GetFullPath(projectDirectory);
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
                Directory.Move(stagingDirectory, ProjectDirectory);
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
            var roots = new StackRootsDocument
            {
                SchemaVersion = StackManifest.CurrentSchemaVersion,
                Roots = manifest.Roots.ToList(),
            };
            await WriteAtomicallyAsync(RootsPath, JsonSerializer.SerializeToUtf8Bytes(roots, JsonOptions), cancellationToken).ConfigureAwait(false);
            await WriteAtomicallyAsync(ManifestPath, Serialize(manifest), cancellationToken).ConfigureAwait(false);
        }
        catch (StackFailureException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StackFailureException(StackFailureCode.MaterialisationFailed,
                "The Stack manifest could not be saved. The previous manifest remains recoverable.", ManifestRelativePath, recoverable: true, retryable: true, exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string ManifestPath => Path.Combine(ProjectDirectory, ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
    private string RootsPath => Path.Combine(ProjectDirectory, RootsRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private void ValidateManagedLayout()
    {
        foreach (string relative in new[] { ".branches", ".branches/domains", ".source", ".roots" })
        {
            string path = Path.GetFullPath(Path.Combine(ProjectDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(ProjectDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new StackFailureException(StackFailureCode.SourceCorrupt, "A managed Stack path resolves outside the project directory.", relative, recoverable: true);
            }

            if (!Directory.Exists(path))
            {
                throw new StackFailureException(StackFailureCode.ManagedMetadataMissing,
                    $"Required Stack-managed infrastructure is missing: {relative}.", relative, recoverable: true);
            }
        }

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
