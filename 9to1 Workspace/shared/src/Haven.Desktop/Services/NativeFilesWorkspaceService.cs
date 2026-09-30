using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

public sealed record NativeFilesWorkspaceConfiguration(string ProfileId, Guid StoreId, FilesLocationId LocationId,
    string RootDirectory, IReadOnlyDictionary<string, HostedItemId> AppFolders);

public sealed record NativeFilesWorkspace(AuthenticatedResourceActor Actor, NativeFilesWorkspaceConfiguration Configuration,
    DurableDriveProvider Provider, FilesWorkspaceDirectoryResolver Directories, FilesMaterializationRegistry Materializations);

/// <summary>Files owns this explicit native configuration. Apps cannot choose another profile or invent a fallback directory.</summary>
public sealed class NativeFilesWorkspaceService(IHomeCoreStateStore home, HomeLocalProfileIdentity profiles) : IHomeLocalStoreEvidenceProvider
{
    private readonly ConcurrentDictionary<(string Profile, long Revision), NativeFilesWorkspace> _cache = new();
    private readonly ConcurrentDictionary<Guid, NativeFilesWorkspace> _creating = new();
    private readonly SemaphoreSlim _setupGate = new(1, 1);
    public string ResourceKind => "files";

    private static string RecordId(string profileId) => "files.native-workspace:" + profileId;
    private static string MetadataDirectory(string root) => Path.Combine(root, ".9to1-files");

    internal async Task<NativeFilesWorkspace?> GetConfiguredAsync(CancellationToken cancellationToken)
    {
        var actor = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || actor.AccountId is not null || actor.OrganisationId is not null) return null;
        var read = await home.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess) throw new InvalidDataException("Home Files configuration needs recovery; it was preserved.");
        var record = read.State!.Records.SingleOrDefault(item => item.RecordId == RecordId(actor.ProfileId));
        if (record is null) return null;
        if (record.SchemaVersion != 1 || record.RecordType != "files.native-workspace" ||
            record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical)
            throw new InvalidDataException("Unsupported Files workspace configuration; it was preserved.");
        NativeFilesWorkspaceConfiguration configuration;
        try { configuration = record.Payload.Deserialize<NativeFilesWorkspaceConfiguration>() ?? throw new JsonException(); }
        catch (JsonException exception) { throw new InvalidDataException("Files workspace configuration is corrupt; it was preserved.", exception); }
        if (configuration.ProfileId != actor.ProfileId || configuration.StoreId == Guid.Empty ||
            configuration.LocationId.Value == Guid.Empty || string.IsNullOrWhiteSpace(configuration.RootDirectory) ||
            configuration.RootDirectory.Contains('\0') || !Path.IsPathFullyQualified(configuration.RootDirectory) ||
            configuration.AppFolders is null || configuration.AppFolders.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value.Value == Guid.Empty))
            throw new UnauthorizedAccessException("Files workspace identity does not match the current verified profile.");
        RequireDirectDirectory(configuration.RootDirectory);
        RequireDirectDirectory(MetadataDirectory(configuration.RootDirectory));
        var statePath = Path.Combine(MetadataDirectory(configuration.RootDirectory), "drive.json");
        if (!File.Exists(statePath) || (File.GetAttributes(statePath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The configured Files state is unavailable or redirected.");
        var workspace = _cache.GetOrAdd((actor.ProfileId, record.Revision), _ => Create(actor, configuration));
        var evidence = await workspace.Provider.GetStoreEvidenceAsync(cancellationToken).ConfigureAwait(false);
        if (evidence.StoreId != configuration.StoreId || await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Files workspace or profile identity changed.");
        return workspace with { Actor = actor };
    }

    public async ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(storeId, out var id)) return null;
        var actor = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var workspace = _creating.TryGetValue(id, out var pending) ? pending : await GetConfiguredAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || workspace is null || workspace.Actor != actor) return null;
        var evidence = await workspace.Provider.GetStoreEvidenceAsync(cancellationToken).ConfigureAwait(false);
        return evidence.StoreId == id ? new(ResourceKind, evidence.StoreId.ToString("D"), evidence.Revision,
            evidence.NewlyCreated, evidence.IsEmpty, true) : null;
    }

    /// <summary>Called only by the compiled native Files setup after an explicit folder-picker choice. Existing data is never adopted here.</summary>
    internal async Task<NativeFilesWorkspace> ConfigureNewAsync(string explicitlyChosenEmptyDirectory,
        HomeLocalStoreOwnership ownership, CancellationToken cancellationToken)
    {
        await _setupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ConfigureNewCoreAsync(explicitlyChosenEmptyDirectory, ownership, cancellationToken).ConfigureAwait(false); }
        finally { _setupGate.Release(); }
    }

    private async Task<NativeFilesWorkspace> ConfigureNewCoreAsync(string explicitlyChosenEmptyDirectory,
        HomeLocalStoreOwnership ownership, CancellationToken cancellationToken)
    {
        var actor = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) ?? throw new UnauthorizedAccessException();
        if (actor.AccountId is not null || actor.OrganisationId is not null || !Guid.TryParse(actor.ProfileId, out var profile))
            throw new UnauthorizedAccessException("Local Files setup requires the actual personal operating-system profile.");
        if (await GetConfiguredAsync(cancellationToken).ConfigureAwait(false) is not null)
            throw new InvalidOperationException("Files is already configured for this profile. Existing configuration was preserved.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(explicitlyChosenEmptyDirectory));
        RequireDirectDirectory(root);
        if (Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidOperationException("Choose an empty folder. Existing files require explicit import and were preserved.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var metadata = MetadataDirectory(root);
        Directory.CreateDirectory(metadata);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(metadata, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var provider = new DurableDriveProvider(Path.Combine(metadata, "drive.json"), new(Guid.NewGuid()), actor.ActorId);
        var evidence = await provider.GetStoreEvidenceAsync(cancellationToken).ConfigureAwait(false);
        var folders = new Dictionary<string, HostedItemId>(StringComparer.Ordinal);
        var configuration = new NativeFilesWorkspaceConfiguration(actor.ProfileId, evidence.StoreId, provider.Location.Id, root, folders);
        var workspace = Create(actor, configuration, provider);
        if (!_creating.TryAdd(evidence.StoreId, workspace)) throw new InvalidOperationException("Files setup is already active.");
        try
        {
            await ownership.BindNewEmptyAsync(ResourceKind, evidence.StoreId.ToString("D"), cancellationToken).ConfigureAwait(false);
            foreach (var (appId, name) in new[] { ("write", "Write"), ("present", "Present"), ("canvas", "Canvas"),
                ("picture", "Picture"), ("media", "Media"), ("sites", "Sites"), ("boards", "Boards"), ("games", "Games") })
            {
                if (await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor) throw new UnauthorizedAccessException("Home profile changed during Files setup.");
                RequireDirectDirectory(root);
                var now = DateTimeOffset.UtcNow;
                var id = HostedItemId.New();
                var operation = new FilesOperation(new(Guid.NewGuid()), actor.ActorId, id, null, null, "CreateFolder", null, null,
                    FilesOperationState.Pending, now, now, null, null);
                var created = await provider.MutateAsync(operation, name, cancellationToken).ConfigureAwait(false);
                if (!created.IsSuccess) throw new InvalidOperationException(created.Error!.Message);
                var physical = Path.Combine(root, name);
                Directory.CreateDirectory(physical);
                var registered = await workspace.Directories.RegisterProfileAsync(profile, id, appId, physical, cancellationToken).ConfigureAwait(false);
                if (!registered.IsSuccess) throw new UnauthorizedAccessException(registered.Error!.Message);
                folders.Add(appId, id);
            }
            var write = await home.WriteAsync(new(RecordId(actor.ProfileId), "files.native-workspace", 1, HomeDataScope.DeviceLocal,
                HomeRecordAuthority.LocalCanonical, 1, JsonSerializer.SerializeToElement(configuration)), 0, cancellationToken).ConfigureAwait(false);
            if (!write.IsSuccess) throw new InvalidOperationException("Files setup configuration conflicted; created Files data was preserved for recovery.");
            return workspace;
        }
        finally { _creating.TryRemove(evidence.StoreId, out _); }
    }

    private static NativeFilesWorkspace Create(AuthenticatedResourceActor actor, NativeFilesWorkspaceConfiguration configuration, DurableDriveProvider? provider = null)
    {
        var metadata = MetadataDirectory(configuration.RootDirectory);
        provider ??= new(Path.Combine(metadata, "drive.json"), configuration.LocationId, actor.ActorId);
        var profile = Guid.Parse(actor.ProfileId);
        var directories = new FilesWorkspaceDirectoryResolver(Path.Combine(metadata, "bindings.json"), _ => null,
            requested => requested == profile ? provider : null);
        return new(actor, configuration, provider, directories,
            new FilesMaterializationRegistry(configuration.RootDirectory, Path.Combine(metadata, "materializations.json")));
    }

    private static void RequireDirectDirectory(string directory)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The configured Files directory is unavailable.");
        for (string? current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Files configuration cannot redirect through another directory.");
    }
}

public sealed class NativeFilesWorkspaceAuthority(NativeFilesWorkspaceService workspaces, HomeLocalProfileIdentity profiles,
    IResourceStoreOwnershipAuthority ownership)
{
    public async Task<string?> ResolveAppDirectoryAsync(string appId, CancellationToken cancellationToken = default)
    {
        var workspace = await GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (workspace is null || !workspace.Configuration.AppFolders.TryGetValue(appId, out var folderId) ||
            !Guid.TryParse(workspace.Actor.ProfileId, out var profile)) return null;
        var result = await workspace.Directories.ResolveProfileAsync(profile, appId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess && result.Value!.FolderId == folderId ? result.Value.DirectoryPath : null;
    }

    public async Task<NativeFilesWorkspace?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var actor = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var workspace = await workspaces.GetConfiguredAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || workspace is null || workspace.Actor != actor) return null;
        var binding = await ownership.GetVerifiedAsync("files", workspace.Configuration.StoreId.ToString("D"), cancellationToken).ConfigureAwait(false);
        return binding?.ProfileId == actor.ProfileId && binding.ResourceKind == "files" &&
            binding.StoreId == workspace.Configuration.StoreId.ToString("D") &&
            await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == actor ? workspace : null;
    }
}
