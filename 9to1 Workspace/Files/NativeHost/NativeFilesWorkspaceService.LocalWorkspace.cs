using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Files.NativeHost;

/// <summary>What the 9to1 workspace root actually is on this device right now. The distinction between
/// "never set up" and "set up, but the folder is gone" is the whole point: only the second one may show
/// the owner "9to1 directory missing - Re-locate or create new".</summary>
public enum FilesWorkspaceRootState { Configured, NotRegistered, MissingRegistered }

public sealed record FilesWorkspaceRootObservation(FilesWorkspaceRootState State, string? RegisteredRoot,
    string DefaultRoot, string? DefaultRootFallbackReason, IReadOnlyList<string> DiscoveredRoots,
    IReadOnlyList<string> Warnings)
{
    /// <summary>True only when a previously registered root has disappeared, which is the single case
    /// the owner allows a prompt for.</summary>
    public bool RequiresRecoveryChoice => State == FilesWorkspaceRootState.MissingRegistered;
}

/// <summary>Files' use of the shared workspace-root rule. Owner direction, 29 October 2026: there is no
/// "connect" step to perform before a board or document can be saved - the app creates the 9to1 folder
/// under Documents itself, keeps using a folder that is already registered, and follows the folder when
/// the user moves it instead of making a new one in Documents.</summary>
public sealed partial class NativeFilesWorkspaceService
{
    /// <summary>Reports the current root state without changing anything on disk. A configuration record
    /// that cannot be opened (its folder moved away) is observed here rather than rethrown, so Files can
    /// present the recovery choice instead of looking like an app that has never been set up.</summary>
    public async Task<FilesWorkspaceRootObservation> ObserveWorkspaceRootAsync(
        CancellationToken cancellationToken = default)
    {
        var actor = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var planned = ResolvePlannedRoot(createDocuments: false);
        if (actor is null)
            return new(FilesWorkspaceRootState.NotRegistered, null, planned.RootDirectory, planned.FallbackReason,
                Array.Empty<string>(), ["No signed-in local profile is available."]);
        var read = await home.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess)
            return new(FilesWorkspaceRootState.NotRegistered, null, planned.RootDirectory, planned.FallbackReason,
                Array.Empty<string>(), ["Home state could not be read; nothing was changed."]);
        var record = read.State!.Records.SingleOrDefault(item => item.RecordId == RecordId(actor.ProfileId));
        if (record is null)
            return new(FilesWorkspaceRootState.NotRegistered, null, planned.RootDirectory, planned.FallbackReason,
                Array.Empty<string>(), Array.Empty<string>());

        NativeFilesWorkspaceConfiguration configuration;
        try
        {
            configuration = record.Payload.Deserialize<NativeFilesWorkspaceConfiguration>() ??
                throw new JsonException();
        }
        catch (JsonException exception)
        {
            return new(FilesWorkspaceRootState.MissingRegistered, null, planned.RootDirectory, planned.FallbackReason,
                Array.Empty<string>(), ["The Files workspace record could not be read and was preserved: " + exception.Message]);
        }
        if (IsUsableWorkspaceRoot(configuration.RootDirectory))
            return new(FilesWorkspaceRootState.Configured, configuration.RootDirectory, planned.RootDirectory,
                planned.FallbackReason, Array.Empty<string>(), Array.Empty<string>());

        // Registered before, gone now: look for the folder by the identity it carries in its own
        // metadata, and hand every match to the owner instead of picking one.
        var matches = LocalWorkspaceFolders.DiscoverWorkspaceRoots(
                LocalWorkspaceFolders.CandidateScanParents(configuration.RootDirectory, planned.RootDirectory,
                    HomeDirectoryOverride))
            .Where(root => RootStoreId(root) == configuration.StoreId)
            .ToArray();
        return new(FilesWorkspaceRootState.MissingRegistered, configuration.RootDirectory, planned.RootDirectory,
            planned.FallbackReason, matches, matches.Length == 1
                ? ["The 9to1 workspace folder was found at " + matches[0] + "; re-register it to keep the existing files."]
                : ["The 9to1 workspace folder registered at " + configuration.RootDirectory + " is unavailable."]);
    }

    /// <summary>First run for a profile that has never had a Files workspace: provision the default 9to1
    /// folder and configure it, with no folder picker and no account. An existing 9to1 folder is adopted
    /// non-destructively - its contents are inspected and only the missing app folders are added.</summary>
    public async Task<NativeFilesWorkspace?> ConfigureDefaultWorkspaceAsync(HomeLocalStoreOwnership ownership,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        var observation = await ObserveWorkspaceRootAsync(cancellationToken).ConfigureAwait(false);
        // Only a profile that has never been set up gets a brand-new root. A root that was registered and
        // then disappeared must be re-located or explicitly replaced by the owner, never quietly recreated.
        if (observation.State != FilesWorkspaceRootState.NotRegistered) return null;
        var planned = LocalWorkspaceFolders.Provision(observation.DefaultRoot);
        return await ConfigureAdoptedRootAsync(planned.RootDirectory, planned.AppFoldersDirectory, ownership,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Points the profile's existing Files workspace at the folder the owner chose, or at the one
    /// the app found by itself. The store identity in the folder's own metadata must match the recorded
    /// store, so a different or unrelated folder can never be adopted as if it were the user's data.</summary>
    public async Task<NativeFilesWorkspace> ReLocateWorkspaceRootAsync(string relocatedRoot, HomeLocalStoreOwnership ownership,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentException.ThrowIfNullOrWhiteSpace(relocatedRoot);
        await _setupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var actor = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) ??
                throw new UnauthorizedAccessException("A verified local profile is required to move the Files workspace.");
            var existing = await ReadConfigurationRecordAsync(actor.ProfileId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
                throw new InvalidOperationException("This profile has no Files workspace to re-locate; a new one is created automatically instead.");
            var configuration = existing.Configuration;
            var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(relocatedRoot));
            var foundId = RootStoreId(target);
            if (foundId is null)
                throw new InvalidOperationException("That folder is not a 9to1 workspace folder, so it cannot replace the missing one. " +
                    "Choose the folder that contains your .9to1-files data, or create a new 9to1 folder.");
            if (foundId != configuration.StoreId)
                throw new InvalidOperationException("That folder belongs to a different 9to1 workspace, so it was not adopted. Your existing record was preserved.");
            var planned = LocalWorkspaceFolders.Provision(target);
            await RewriteConfigurationRecordAsync(actor, existing.Record, configuration with { RootDirectory = planned.RootDirectory },
                cancellationToken).ConfigureAwait(false);
            await RebindAppFoldersAsync(planned, configuration, configuration.RootDirectory, cancellationToken).ConfigureAwait(false);
            var workspace = await GetConfiguredAsync(cancellationToken).ConfigureAwait(false) ??
                throw new InvalidOperationException("The re-located Files workspace could not be opened; the previous record was preserved.");
            return workspace;
        }
        finally { _setupGate.Release(); }
    }

    /// <summary>Used by Files setup when the owner deliberately points the workspace at a folder they
    /// chose. Unlike the historic empty-folder setup, an adopted folder is inspected and completed; it is
    /// never required to be empty, because requiring that is what forced a "connect" step onto first use.</summary>
    public Task<NativeFilesWorkspace> ConfigureAdoptedRootAsync(string rootDirectory, string appFoldersDirectory,
        HomeLocalStoreOwnership ownership, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        return ConfigureAdoptedCoreAsync(rootDirectory, appFoldersDirectory, ownership, cancellationToken);
    }

    private async Task<NativeFilesWorkspace> ConfigureAdoptedCoreAsync(string rootDirectory, string appFoldersDirectory,
        HomeLocalStoreOwnership ownership, CancellationToken cancellationToken)
    {
        await _setupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var actor = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) ??
                throw new UnauthorizedAccessException();
            if (actor.AccountId is not null || actor.OrganisationId is not null || !Guid.TryParse(actor.ProfileId, out var profile))
                throw new UnauthorizedAccessException("The 9to1 workspace requires the actual personal operating-system profile.");
            if (await GetConfiguredAsync(cancellationToken).ConfigureAwait(false) is not null)
                throw new InvalidOperationException("Files is already configured for this profile. Existing configuration was preserved.");
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
            var apps = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                string.IsNullOrWhiteSpace(appFoldersDirectory) ? root : appFoldersDirectory));
            RequireDirectDirectory(root);
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
                foreach (var (appId, name) in LocalWorkspaceFolders.KnownAppFolders)
                {
                    if (await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor)
                        throw new UnauthorizedAccessException("Home profile changed during Files setup.");
                    RequireDirectDirectory(root);
                    var physical = Path.Combine(apps, name);
                    // The folder may already hold the user's work. It is created only when missing and
                    // never cleared, so adopting Documents/9to1 cannot destroy anything.
                    if (!Directory.Exists(physical))
                    {
                        if (File.Exists(physical))
                            throw new InvalidOperationException($"{physical} is a file, not a folder. Choose another location or " +
                                "move that file; nothing was changed.");
                        Directory.CreateDirectory(physical);
                    }
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(physical, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    var now = DateTimeOffset.UtcNow;
                    var id = HostedItemId.New();
                    var operation = new FilesOperation(new(Guid.NewGuid()), actor.ActorId, id, null, null, "CreateFolder", null, null,
                        FilesOperationState.Pending, now, now, null, null);
                    var created = await provider.MutateAsync(operation, name, cancellationToken).ConfigureAwait(false);
                    if (!created.IsSuccess) throw new InvalidOperationException(created.Error!.Message);
                    var registered = await workspace.Directories.RegisterProfileAsync(profile, id, appId, physical, cancellationToken).ConfigureAwait(false);
                    if (!registered.IsSuccess) throw new UnauthorizedAccessException(registered.Error!.Message);
                    folders.Add(appId, id);
                }
                await WriteConfigurationRecordAsync(actor, configuration, expectedRevision: 0, cancellationToken).ConfigureAwait(false);
                return workspace;
            }
            finally { _creating.TryRemove(evidence.StoreId, out _); }
        }
        finally { _setupGate.Release(); }
    }

    /// <summary>After the root moves, every app folder binding has to point at the same place under the
    /// new root. Each binding is re-based by its path relative to the root it was registered under, so a
    /// workspace that keeps its own Apps subfolder keeps that shape too. Missing folders are created;
    /// content that travelled with the moved folder is left exactly where the user put it.</summary>
    private async Task RebindAppFoldersAsync(LocalWorkspacePlacement planned,
        NativeFilesWorkspaceConfiguration previousConfiguration, string previousRoot,
        CancellationToken cancellationToken)
    {
        var workspace = await GetConfiguredAsync(cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException("The re-located Files workspace is unavailable.");
        if (!Guid.TryParse(workspace.Actor.ProfileId, out var profile))
            throw new UnauthorizedAccessException("The local profile identity is unavailable.");
        var failures = new List<string>();
        foreach (var (appId, folderName) in LocalWorkspaceFolders.KnownAppFolders)
        {
            if (!previousConfiguration.AppFolders.TryGetValue(appId, out var folderId)) continue;
            var previous = await workspace.Directories.ResolveProfileAsync(profile, appId, cancellationToken)
                .ConfigureAwait(false);
            var target = previous.IsSuccess && IsUnder(previous.Value!.DirectoryPath, previousRoot)
                ? Path.Combine(planned.RootDirectory, Path.GetRelativePath(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(previousRoot)), previous.Value.DirectoryPath))
                : Path.Combine(planned.AppFoldersDirectory, folderName);
            if (!Directory.Exists(target))
            {
                if (File.Exists(target))
                {
                    failures.Add($"{appId}: {target} is a file, not a folder");
                    continue;
                }
                Directory.CreateDirectory(target);
            }
            var registered = await workspace.Directories.RegisterProfileAsync(profile, folderId, appId, target,
                cancellationToken).ConfigureAwait(false);
            if (!registered.IsSuccess) failures.Add($"{appId}: {registered.Error!.Message}");
        }
        if (failures.Count > 0)
            throw new InvalidOperationException("The workspace folder was re-registered, but these app folders could not be " +
                "pointed at it: " + string.Join("; ", failures));
    }

    private static bool IsUnder(string path, string parent) =>
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) +
            Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Reads the recorded workspace location without requiring the folder to still exist. The
    /// recovery surface needs the old path to display it, and GetConfigurationAsync cannot answer when
    /// its directory has moved away. Nothing here grants access to that folder.</summary>
    public async Task<NativeFilesWorkspaceConfiguration?> ReadRecordedConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        var actor = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null) return null;
        return (await ReadConfigurationRecordAsync(actor.ProfileId, cancellationToken).ConfigureAwait(false))?.Configuration;
    }

    private sealed record ConfigurationRecord(HomeCoreStateRecord Record, NativeFilesWorkspaceConfiguration Configuration);

    private async Task<ConfigurationRecord?> ReadConfigurationRecordAsync(string profileId, CancellationToken cancellationToken)
    {
        var read = await home.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess) throw new InvalidDataException("Home Files configuration needs recovery; it was preserved.");
        var record = read.State!.Records.SingleOrDefault(item => item.RecordId == RecordId(profileId));
        if (record is null) return null;
        if (record.SchemaVersion != 1 || record.RecordType != "files.native-workspace" ||
            record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical)
            throw new InvalidDataException("Unsupported Files workspace configuration; it was preserved.");
        try
        {
            return new(record, record.Payload.Deserialize<NativeFilesWorkspaceConfiguration>() ?? throw new JsonException());
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Files workspace configuration is corrupt; it was preserved.", exception);
        }
    }

    private async Task RewriteConfigurationRecordAsync(AuthenticatedResourceActor actor, HomeCoreStateRecord record,
        NativeFilesWorkspaceConfiguration configuration, CancellationToken cancellationToken)
    {
        await WriteConfigurationRecordAsync(actor, configuration, record.Revision, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteConfigurationRecordAsync(AuthenticatedResourceActor actor,
        NativeFilesWorkspaceConfiguration configuration, long expectedRevision, CancellationToken cancellationToken)
    {
        var write = await home.WriteGuardedAsync(new(RecordId(actor.ProfileId), "files.native-workspace", 1, HomeDataScope.DeviceLocal,
            HomeRecordAuthority.LocalCanonical, expectedRevision + 1, JsonSerializer.SerializeToElement(configuration)),
            expectedRevision, actor, profiles, cancellationToken).ConfigureAwait(false);
        if (write.IsSuccess) return;
        var failure = write.Failure ?? throw new InvalidOperationException("Home returned an unsuccessful Files configuration write without failure details; created Files data was preserved for recovery.");
        var message = failure.Code == HomeCoreErrorCode.HomeStateConflict
            ? "The Files workspace location conflicted with another change. " + failure.Message
            : failure.Message;
        message += " Existing Files data was preserved.";
        if (!string.IsNullOrWhiteSpace(failure.RecoveryAction)) message += " " + failure.RecoveryAction;
        Exception exception = failure.Code switch
        {
            HomeCoreErrorCode.HomeStateConflict => new InvalidOperationException(message),
            HomeCoreErrorCode.PermissionDenied => new UnauthorizedAccessException(message),
            _ => new HomeCoreStateUnavailableException(failure.Code, message),
        };
        exception.Data["HomeCoreFailure"] = failure;
        throw exception;
    }

    /// <summary>True when this directory is a usable 9to1 workspace root: the folder exists, is not a
    /// link, and still carries its own store metadata.</summary>
    private static bool IsUsableWorkspaceRoot(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) ||
            !LocalWorkspaceFolders.IsWorkspaceRoot(directory)) return false;
        try
        {
            for (string? current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)); current is not null;
                 current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The store identity a folder carries with it, read from the workspace's own metadata file.
    /// This is what lets a moved folder be recognised instead of guessed at from its name.</summary>
    internal static Guid? RootStoreId(string directory)
    {
        try
        {
            var path = Path.Combine(directory, LocalWorkspaceFolders.MetadataFolderName, "drive.json");
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (!document.RootElement.TryGetProperty("state", out var state) ||
                !state.TryGetProperty("storeId", out var storeId)) return null;
            return Guid.TryParse(storeId.GetString(), out var parsed) ? parsed : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { return null; }
    }

    /// <summary>Where the workspace goes when Documents cannot hold it. Haven composition wires this to
    /// the app's own data directory; left unset, the platform's user data location is used, so the
    /// fallback is never a caller guessing a path.</summary>
    public string? ApplicationDataFallbackDirectory { get; set; }

    /// <summary>The user profile whose Documents folder holds the workspace. Home sets this when it runs
    /// with a redirected profile so a development copy cannot write into the signed-in user's real
    /// Documents folder; unset, the running account's home is used.</summary>
    public string? HomeDirectoryOverride { get; set; }

    private DefaultWorkspaceRoot ResolvePlannedRoot(bool createDocuments) =>
        LocalWorkspaceFolders.ResolveDefaultRoot(FallbackWorkspaceParent(), HomeDirectoryOverride,
            createDocuments: createDocuments);

    private string FallbackWorkspaceParent()
    {
        if (!string.IsNullOrWhiteSpace(ApplicationDataFallbackDirectory))
            return Path.GetFullPath(ApplicationDataFallbackDirectory!);
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(data))
            data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(data, "9to1");
    }
}
