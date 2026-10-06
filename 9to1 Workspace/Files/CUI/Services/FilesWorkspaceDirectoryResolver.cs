namespace HavenOS.Files;

public sealed record FilesWorkspaceDirectoryBinding(Guid AccountId, HostedItemId FolderId, FilesLocationId LocationId,
    string DirectoryPath, string OwningAppId, DateTimeOffset RegisteredAt, Guid? ProfileId = null);

/// <summary>
/// Explicit Files-owned materialisation bindings. Hosts register only authorised canonical folders;
/// there is no generated account directory or private app-storage fallback.
/// </summary>
public sealed partial class FilesWorkspaceDirectoryResolver
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

    public async Task<FilesResult<FilesWorkspaceDirectoryBinding>> ResolveFolderAsync(Guid authenticatedAccountId,
        Guid? authenticatedProfileId, HostedItemId folderId, CancellationToken cancellationToken = default)
    {
        var state = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        var candidates = state.Bindings.Where(item => item.AccountId == authenticatedAccountId &&
            item.ProfileId == authenticatedProfileId && item.FolderId == folderId).ToArray();
        if (candidates.Length != 1)
            return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.DestinationUnavailable,
                "The content anchor has no unique registered Files directory.", authenticatedProfileId ?? authenticatedAccountId);
        var resolved = await ResolveCoreAsync(authenticatedAccountId, authenticatedProfileId,
            candidates[0].OwningAppId, cancellationToken).ConfigureAwait(false);
        return resolved.IsSuccess && resolved.Value!.FolderId != folderId
            ? Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.RevisionConflict, "The registered content anchor changed.", authenticatedProfileId ?? authenticatedAccountId)
            : resolved;
    }

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

    /// <summary>Read an existing mapping using the exact configured provider and original store UUID.
    /// This observation grants no content or write access; the original private resource owner must
    /// authenticate its provider/materializer pairing and retain the returned binding across use.</summary>
    public async Task<FilesResult<FilesWorkspaceDirectoryBinding>> ResolveProfileForOriginalStoreAsync(
        Guid authenticatedProfileId, string owningAppId, HostedItemId originalFolderId,
        DurableDriveProvider originalProvider, Guid expectedStoreId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalProvider); ArgumentException.ThrowIfNullOrWhiteSpace(owningAppId);
        if (authenticatedProfileId == Guid.Empty || originalFolderId.Value == Guid.Empty || expectedStoreId == Guid.Empty ||
            !ReferenceEquals(_profileProviders?.Invoke(authenticatedProfileId), originalProvider))
            throw new UnauthorizedAccessException("The original configured directory provider is unavailable.");
        var state = await _store.ReadExistingAsync(cancellationToken).ConfigureAwait(false);
        var candidates = state.Bindings.Where(item => item.AccountId == Guid.Empty &&
            item.ProfileId == authenticatedProfileId && item.OwningAppId == owningAppId).Take(2).ToArray();
        if (candidates.Length != 1 || candidates[0].FolderId != originalFolderId)
            return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.DestinationUnavailable,
                "The original folder has no unique registered app mapping.", authenticatedProfileId);
        var binding = candidates[0];
        if (!ReferenceEquals(_profileProviders?.Invoke(authenticatedProfileId), originalProvider) ||
            binding.LocationId != originalProvider.Location.Id)
            throw new UnauthorizedAccessException("The original configured directory provider changed.");
        var folder = await originalProvider.GetForOriginalStoreAsync(expectedStoreId, originalFolderId, cancellationToken).ConfigureAwait(false);
        if (!ReferenceEquals(_profileProviders?.Invoke(authenticatedProfileId), originalProvider))
            throw new UnauthorizedAccessException("The original configured directory provider changed.");
        if (!folder.IsSuccess) return FilesResult<FilesWorkspaceDirectoryBinding>.Failure(folder.Error!);
        if (folder.Value!.OwnerPrincipalId != OwnerId(Guid.Empty, authenticatedProfileId) ||
            folder.Value.Kind != HostedItemKind.Folder || folder.Value.LocationId != binding.LocationId)
            return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.PermissionDenied,
                "The original canonical folder ownership or location changed.", authenticatedProfileId);
        if (!FilesPhysicalDirectory.IsDirectDirectory(binding.DirectoryPath))
            return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.DestinationUnavailable,
                "The original registered materialisation is unavailable or redirects elsewhere.", authenticatedProfileId);
        return FilesResult<FilesWorkspaceDirectoryBinding>.Success(binding);
    }

    /// <summary>Observe an existing canonical registration using the privately paired original
    /// provider, with the owner rechecking its original read context after each awaited owner read.
    /// The callback can only refuse observation; neither IDs nor this mapping observation grant access.</summary>
    public async Task<FilesResult<FilesWorkspaceDirectoryBinding>> ResolveOriginalRegisteredFolderAsync(
        Guid originalProfileId, string originalAppId, HostedItemId originalFolderId,
        DurableDriveProvider originalProvider, Guid originalStoreId, bool requireUniqueFolder,
        Func<CancellationToken, ValueTask> requireOriginalRead, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalProvider); ArgumentNullException.ThrowIfNull(requireOriginalRead);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalAppId);
        if (originalProfileId == Guid.Empty || originalStoreId == Guid.Empty || originalFolderId.Value == Guid.Empty)
            throw new UnauthorizedAccessException("Original registered folder identity unavailable.");
        await requireOriginalRead(cancellationToken).ConfigureAwait(false);
        if (!ReferenceEquals(_profileProviders?.Invoke(originalProfileId), originalProvider))
            throw new UnauthorizedAccessException("Original registered folder provider changed.");
        var state = await _store.ReadExistingAsync(cancellationToken).ConfigureAwait(false);
        await requireOriginalRead(cancellationToken).ConfigureAwait(false);
        var candidates = state.Bindings.Where(item => item.AccountId == Guid.Empty && item.ProfileId == originalProfileId &&
            (requireUniqueFolder ? item.FolderId == originalFolderId : item.OwningAppId == originalAppId)).Take(2).ToArray();
        if (candidates.Length != 1 || candidates[0].OwningAppId != originalAppId || candidates[0].FolderId != originalFolderId)
            return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.DestinationUnavailable,
                "Original folder has no unique trusted registration.", originalProfileId);
        var binding = candidates[0];
        if (!ReferenceEquals(_profileProviders?.Invoke(originalProfileId), originalProvider) || binding.LocationId != originalProvider.Location.Id)
            throw new UnauthorizedAccessException("Original registered folder provider or location changed.");
        var folder = await originalProvider.GetForOriginalStoreAsync(originalStoreId, originalFolderId, cancellationToken).ConfigureAwait(false);
        await requireOriginalRead(cancellationToken).ConfigureAwait(false);
        if (!ReferenceEquals(_profileProviders?.Invoke(originalProfileId), originalProvider))
            throw new UnauthorizedAccessException("Original registered folder provider changed.");
        if (!folder.IsSuccess) return FilesResult<FilesWorkspaceDirectoryBinding>.Failure(folder.Error!);
        if (folder.Value!.Kind != HostedItemKind.Folder || folder.Value.OwnerPrincipalId != OwnerId(Guid.Empty, originalProfileId) ||
            folder.Value.LocationId != binding.LocationId || !FilesPhysicalDirectory.IsDirectDirectory(binding.DirectoryPath))
            return Failure<FilesWorkspaceDirectoryBinding>(FilesErrorCode.PermissionDenied,
                "Original registered folder ownership, location or materialization changed.", originalProfileId);
        return FilesResult<FilesWorkspaceDirectoryBinding>.Success(binding);
    }

    /// <summary>Retain an already observed exact mapping through the owner's Files commit.
    /// This metadata lease grants no resource access. The private original resource issuer must
    /// authenticate the resolver/provider pairing before entry. No provider or Home reads occur here.</summary>
    public async Task<FilesOriginalBindingCommitLease?> AcquireOriginalBindingCommitLeaseAsync(
        FilesWorkspaceDirectoryBinding originalBinding, DurableDriveProvider originalProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalBinding); ArgumentNullException.ThrowIfNull(originalProvider);
        if (originalBinding.AccountId != Guid.Empty || originalBinding.ProfileId is not { } profile ||
            profile == Guid.Empty || originalBinding.FolderId.Value == Guid.Empty ||
            originalBinding.LocationId != originalProvider.Location.Id)
            return null;
        VersionedJsonStateStore<BindingState>.ExistingReadLease? lease =
            await _store.AcquireExistingReadLeaseAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var candidates = lease.Snapshot.Bindings.Where(item => item.AccountId == Guid.Empty &&
                item.ProfileId == profile && item.OwningAppId == originalBinding.OwningAppId).Take(2).ToArray();
            if (candidates.Length != 1 || candidates[0] != originalBinding ||
                !FilesPhysicalDirectory.IsDirectDirectory(originalBinding.DirectoryPath))
                return null;
            var retained = new FilesOriginalBindingCommitLease(lease);
            lease = null;
            return retained;
        }
        finally { if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false); }
    }

    public sealed class FilesOriginalBindingCommitLease : IAsyncDisposable
    {
        private VersionedJsonStateStore<BindingState>.ExistingReadLease? _lease;
        internal FilesOriginalBindingCommitLease(VersionedJsonStateStore<BindingState>.ExistingReadLease lease)
        { _lease = lease; }
        public bool IsHeld => Volatile.Read(ref _lease) is not null;
        public async ValueTask DisposeAsync()
        {
            var lease = Interlocked.Exchange(ref _lease, null);
            if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string OwnerId(Guid accountId, Guid? profileId) => profileId is { } local ? "local-profile:" + local.ToString("D") : accountId.ToString("N");

    private static FilesResult<T> Failure<T>(FilesErrorCode code, string message, Guid account) =>
        FilesResult<T>.Failure(new(code, message, "9to1.Files.ResolveWorkspaceDirectory", account.ToString("N"), true, false));
}
