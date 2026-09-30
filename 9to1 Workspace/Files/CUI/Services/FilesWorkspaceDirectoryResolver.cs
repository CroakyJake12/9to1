namespace HavenOS.Files;

public sealed record FilesWorkspaceDirectoryBinding(Guid AccountId, HostedItemId FolderId, FilesLocationId LocationId,
    string DirectoryPath, string OwningAppId, DateTimeOffset RegisteredAt, Guid? ProfileId = null);

/// <summary>
/// Explicit Files-owned materialisation bindings. Hosts register only authorised canonical folders;
/// there is no generated account directory or private app-storage fallback.
/// </summary>
public sealed class FilesWorkspaceDirectoryResolver
{
    public sealed record BindingState(IReadOnlyList<FilesWorkspaceDirectoryBinding> Bindings);
    private readonly VersionedJsonStateStore<BindingState> _store;
    private readonly Func<Guid, IFilesProvider?> _providers;
    private readonly Func<Guid, IFilesProvider?>? _profileProviders;

    public FilesWorkspaceDirectoryResolver(string statePath, Func<Guid, IFilesProvider?> authorisedAccountProvider,
        Func<Guid, IFilesProvider?>? authorisedProfileProvider = null)
    {
        _providers = authorisedAccountProvider ?? throw new ArgumentNullException(nameof(authorisedAccountProvider));
        _profileProviders = authorisedProfileProvider;
        _store = new(statePath, 1, () => new BindingState([]));
    }

    /// <summary>Trusted Home/Files setup operation; never expose this method as an unreviewed raw path web action.</summary>
    public Task<FilesResult<FilesWorkspaceDirectoryBinding>> RegisterAsync(Guid authenticatedAccountId,
        HostedItemId canonicalFolderId, string owningAppId, string existingDirectoryPath,
        CancellationToken cancellationToken = default) =>
        RegisterCoreAsync(authenticatedAccountId, null, canonicalFolderId, owningAppId, existingDirectoryPath, cancellationToken);

    /// <summary>Local Home profile namespace is independent of CAKE AccountID; no fake account is created.</summary>
    public Task<FilesResult<FilesWorkspaceDirectoryBinding>> RegisterProfileAsync(Guid authenticatedProfileId,
        HostedItemId canonicalFolderId, string owningAppId, string existingDirectoryPath, CancellationToken cancellationToken = default) =>
        RegisterCoreAsync(Guid.Empty, authenticatedProfileId, canonicalFolderId, owningAppId, existingDirectoryPath, cancellationToken);

    private async Task<FilesResult<FilesWorkspaceDirectoryBinding>> RegisterCoreAsync(Guid authenticatedAccountId,
        Guid? profileId, HostedItemId canonicalFolderId, string owningAppId, string existingDirectoryPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owningAppId);
        var principalId = profileId ?? authenticatedAccountId;
        if (principalId == Guid.Empty) return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.PermissionDenied, "A verified account or local profile identity is required.", principalId);
        var provider = profileId is { } local ? _profileProviders?.Invoke(local) : _providers(authenticatedAccountId);
        if (provider is null) return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.PermissionDenied, "This account has no authorised Files provider.", authenticatedAccountId);
        var folder = await provider.GetAsync(canonicalFolderId, cancellationToken).ConfigureAwait(false);
        if (!folder.IsSuccess) return FilesResult<FilesWorkspaceDirectoryBinding>.Failure(folder.Error!);
        if (folder.Value!.Kind != HostedItemKind.Folder || folder.Value.LocationId != provider.Location.Id ||
            folder.Value.OwnerPrincipalId != OwnerId(authenticatedAccountId, profileId))
            return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.PermissionDenied, "The canonical folder is not owned by this account.", authenticatedAccountId);
        if (!Path.IsPathFullyQualified(existingDirectoryPath))
            return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.DestinationUnavailable, "Files setup must supply an absolute existing materialised folder.", authenticatedAccountId);
        var directory = Path.GetFullPath(existingDirectoryPath);
        if (!FilesPhysicalDirectory.IsDirectDirectory(directory))
            return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.DestinationUnavailable, "The materialised directory is unavailable or redirects elsewhere.", authenticatedAccountId);
        var binding = new FilesWorkspaceDirectoryBinding(authenticatedAccountId, canonicalFolderId, provider.Location.Id, directory, owningAppId, DateTimeOffset.UtcNow, profileId);
        var conflict = false;
        await _store.UpdateAsync(state =>
        {
            if (state.Bindings.Any(item => (item.AccountId != authenticatedAccountId || item.ProfileId != profileId) &&
                string.Equals(item.DirectoryPath, directory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            { conflict = true; return state; }
            return new([.. state.Bindings.Where(item => item.AccountId != authenticatedAccountId || item.ProfileId != profileId || item.OwningAppId != owningAppId), binding]);
        }, cancellationToken).ConfigureAwait(false);
        if (conflict) return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.PermissionDenied, "A materialised directory is already bound to another account or local profile authority.", principalId);
        return FilesResult<FilesWorkspaceDirectoryBinding>.Success(binding);
    }

    public Task<FilesResult<FilesWorkspaceDirectoryBinding>> ResolveAsync(Guid authenticatedAccountId,
        string owningAppId, CancellationToken cancellationToken = default) => ResolveCoreAsync(authenticatedAccountId, null, owningAppId, cancellationToken);

    public Task<FilesResult<FilesWorkspaceDirectoryBinding>> ResolveProfileAsync(Guid authenticatedProfileId,
        string owningAppId, CancellationToken cancellationToken = default) => ResolveCoreAsync(Guid.Empty, authenticatedProfileId, owningAppId, cancellationToken);

    private async Task<FilesResult<FilesWorkspaceDirectoryBinding>> ResolveCoreAsync(Guid authenticatedAccountId,
        Guid? profileId, string owningAppId, CancellationToken cancellationToken)
    {
        var principalId = profileId ?? authenticatedAccountId;
        if (principalId == Guid.Empty) return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.PermissionDenied, "A verified account or local profile identity is required.", principalId);
        var state = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        var binding = state.Bindings.SingleOrDefault(item => item.AccountId == authenticatedAccountId && item.ProfileId == profileId && item.OwningAppId == owningAppId);
        if (binding is null) return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.DestinationUnavailable, "Choose and register a canonical Files folder for this app.", authenticatedAccountId);
        var provider = profileId is { } local ? _profileProviders?.Invoke(local) : _providers(authenticatedAccountId);
        if (provider is null || provider.Location.Id != binding.LocationId)
            return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.PermissionDenied, "The registered Files location is not authorised.", authenticatedAccountId);
        var current = await provider.GetAsync(binding.FolderId, cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess) return FilesResult<FilesWorkspaceDirectoryBinding>.Failure(current.Error!);
        if (current.Value!.OwnerPrincipalId != OwnerId(authenticatedAccountId, profileId) || current.Value.Kind != HostedItemKind.Folder)
            return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.PermissionDenied, "Canonical folder ownership changed.", authenticatedAccountId);
        if (!FilesPhysicalDirectory.IsDirectDirectory(binding.DirectoryPath))
            return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.DestinationUnavailable, "The registered Files materialisation is unavailable.", authenticatedAccountId);
        return FilesResult<FilesWorkspaceDirectoryBinding>.Success(binding);
    }

    private static string OwnerId(Guid accountId, Guid? profileId) => profileId is { } local ? "local-profile:" + local.ToString("D") : accountId.ToString("N");

    private static FilesResult<T> Failure<T>(FilesErrorCode code, string message, Guid account) =>
        FilesResult<T>.Failure(new(code, message, "9to1.Files.ResolveWorkspaceDirectory", account.ToString("N"), true, false));
}
