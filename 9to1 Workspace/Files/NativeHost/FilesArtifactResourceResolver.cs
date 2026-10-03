using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

/// <summary>Host binds actual providers to verified Home actors; caller arguments cannot select a profile or Drive.</summary>
public sealed partial class FilesArtifactResourceResolver : IOriginalCanonicalReadResolver, IOriginalCanonicalWriteResolver
{
    private readonly NativeFilesWorkspaceAuthority? _originalAuthority;
    private readonly Func<AuthenticatedResourceActor, CancellationToken, ValueTask<IFilesProvider?>> _providers;
    private readonly Func<AuthenticatedResourceActor, string, CancellationToken, ValueTask<HostedItemId?>>? _appFolders;
    public FilesArtifactResourceResolver(Func<AuthenticatedResourceActor, IFilesProvider?> providers)
        : this((actor, _) => ValueTask.FromResult(providers(actor))) { }
    public FilesArtifactResourceResolver(Func<AuthenticatedResourceActor, CancellationToken, ValueTask<IFilesProvider?>> providers,
        Func<AuthenticatedResourceActor, string, CancellationToken, ValueTask<HostedItemId?>>? appFolders = null)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _appFolders = appFolders;
    }
    public FilesArtifactResourceResolver(NativeFilesWorkspaceAuthority originalAuthority)
        : this(async (actor, token) =>
        {
            var workspace = await originalAuthority.GetCurrentAsync(token).ConfigureAwait(false);
            return workspace?.Actor == actor ? workspace.Provider : null;
        }, async (actor, app, token) =>
        {
            var workspace = await originalAuthority.GetCurrentAsync(token).ConfigureAwait(false);
            return workspace?.Actor == actor && workspace.Configuration.AppFolders.TryGetValue(app, out var folder) ? folder : null;
        }) { _originalAuthority = originalAuthority ?? throw new ArgumentNullException(nameof(originalAuthority)); }

    private sealed class OriginalRead(FilesArtifactResourceResolver issuer, AuthenticatedResourceActor actor,
        ResourceScope scope, string action, Guid storeId, Guid? materializationFolderId, DurableDriveProvider provider, FilesWorkspaceDirectoryResolver directories, Func<CancellationToken, ValueTask<bool>> current) : IOriginalCanonicalReadContext
    {
        public FilesArtifactResourceResolver Issuer { get; } = issuer;
        public AuthenticatedResourceActor Actor { get; } = actor;
        public ResourceScope OriginalScope { get; } = scope;
        public string Action { get; } = action;
        public Guid? OriginalMaterializationFolderId { get; } = materializationFolderId;
        public Guid StoreId { get; } = storeId;
        public DurableDriveProvider Provider { get; } = provider;
        public FilesWorkspaceDirectoryResolver Directories { get; } = directories;
        private int _retired;
        public bool IsRetired => Volatile.Read(ref _retired) != 0;
        public void Retire() => Interlocked.Exchange(ref _retired, 1);
        public async ValueTask<bool> Current(CancellationToken token)
        {
            if (IsRetired) return false;
            var valid = await current(token).ConfigureAwait(false);
            if (!valid) Interlocked.Exchange(ref _retired, 1);
            return valid && !IsRetired;
        }
    }

    public async ValueTask<IOriginalCanonicalReadContext> CaptureOriginalCanvasReadAsync(
        NativeFilesWorkspace original, HostedItemId originalFile, Func<bool> originalLifetime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(originalLifetime);
        if (_originalAuthority is null || originalFile.Value == Guid.Empty)
            throw new UnauthorizedAccessException("The registered original Files read port is unavailable.");
        var current = await _originalAuthority.CaptureOriginalReadCheckAsync(original, originalLifetime, cancellationToken).ConfigureAwait(false);
        if (!await current(cancellationToken).ConfigureAwait(false)) throw new UnauthorizedAccessException("Original Files read retired.");
        var metadata = await original.Provider.GetForOriginalStoreAsync(original.Configuration.StoreId, originalFile, cancellationToken).ConfigureAwait(false);
        if (!await current(cancellationToken).ConfigureAwait(false) || !metadata.IsSuccess)
            throw new UnauthorizedAccessException("Original Files selection changed while capturing its revision.");
        var scope = new ResourceScope(ResourceKind, originalFile.ToString(), metadata.Value!.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Read);
        return new OriginalRead(this, original.Actor, scope, "canvas.file.open", original.Configuration.StoreId, original.Configuration.AppFolders["canvas"].Value, original.Provider, original.Directories, current);
    }

    /// <summary>Capture a read-only canonical folder observation from the actual original workspace.
    /// A folder context does not authenticate a physical mapping, ancestry, project or write.</summary>
    public async ValueTask<IOriginalCanonicalReadContext> CaptureOriginalFolderReadAsync(
        NativeFilesWorkspace original, HostedItemId originalFolder, Func<bool> originalLifetime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(originalLifetime);
        if (_originalAuthority is null || originalFolder.Value == Guid.Empty)
            throw new UnauthorizedAccessException("The registered original Files folder port is unavailable.");
        var current = await _originalAuthority.CaptureOriginalReadCheckAsync(original, originalLifetime, cancellationToken).ConfigureAwait(false);
        if (!await current(cancellationToken).ConfigureAwait(false)) throw new UnauthorizedAccessException("Original Files folder retired.");
        var metadata = await original.Provider.GetForOriginalStoreAsync(original.Configuration.StoreId, originalFolder, cancellationToken).ConfigureAwait(false);
        if (!await current(cancellationToken).ConfigureAwait(false) || !metadata.IsSuccess ||
            metadata.Value is not { Kind: HostedItemKind.Folder, CurrentRevisionId: { } })
            throw new UnauthorizedAccessException("Select an unchanged original canonical folder.");
        var scope = new ResourceScope(ResourceKind, originalFolder.ToString(), metadata.Value.CurrentRevisionId.Value.ToString(), ResourceAccess.Read);
        return new OriginalRead(this, original.Actor, scope, "files.folder.native-root.read", original.Configuration.StoreId, null, original.Provider, original.Directories, current);
    }

    public bool IsIssuedOriginalRead(IOriginalCanonicalReadContext context, AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope) => context is OriginalRead original && ReferenceEquals(original.Issuer, this)
        && !original.IsRetired && original.Actor == actor && actionId == original.Action && scope.Kind == ResourceKind
        && scope.Access == ResourceAccess.Read && scope == original.OriginalScope;

    public bool IsIssuedOriginalReadOwnerBinding(IOriginalCanonicalReadContext context, object originalProvider, object originalDirectories, Guid expectedStoreId) =>
        context is OriginalRead original && ReferenceEquals(original.Issuer, this) && !original.IsRetired
        && expectedStoreId != Guid.Empty && original.StoreId == expectedStoreId
        && ReferenceEquals(original.Provider, originalProvider) && ReferenceEquals(original.Directories, originalDirectories);

    public async ValueTask<ResourceAccessDecision> EvaluateOriginalReadAsync(IOriginalCanonicalReadContext context,
        AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken cancellationToken)
    {
        ResourceAccessDecision Deny() => new(false, "FilesOriginalReadChanged", actor.ActorId, scope.Revision, actor.OrganisationId);
        if (!IsIssuedOriginalRead(context, actor, actionId, scope)) return Deny();
        var original = (OriginalRead)context;
        if (!await original.Current(cancellationToken).ConfigureAwait(false)) return Deny();
        try
        {
            var result = await EvaluateCoreAsync(actor, actionId, scope, cancellationToken, original.Provider, original.Current, original.StoreId).ConfigureAwait(false);
            if (!result.Allowed) original.Retire();
            return await original.Current(cancellationToken).ConfigureAwait(false) ? result : Deny();
        }
        catch (FilesOriginalStoreReadChangedException) { original.Retire(); return Deny(); }
    }

    private sealed class OriginalWrite(FilesArtifactResourceResolver issuer, OriginalRead original) : IOriginalCanonicalWriteContext
    {
        public FilesArtifactResourceResolver Issuer { get; } = issuer;
        public OriginalRead Original { get; } = original;
        public ResourceScope OriginalScope { get; } = original.OriginalScope with { Access = ResourceAccess.Write };
    }

    /// <summary>Derive the exact write observation from this issuer's original Canvas display observation.
    /// No current workspace/provider/configuration is reopened; no Home approval or mutation is granted.</summary>
    public async ValueTask<IOriginalCanonicalWriteContext> CaptureOriginalCanvasWriteAsync(
        IOriginalCanonicalReadContext originalRead, CancellationToken cancellationToken = default)
    {
        if (originalRead is not OriginalRead original || !ReferenceEquals(original.Issuer, this) ||
            original.Action != "canvas.file.open" || original.OriginalMaterializationFolderId is null || original.IsRetired)
            throw new UnauthorizedAccessException("Retain the actual privately issued original Canvas display.");
        var read = await EvaluateOriginalReadAsync(original, original.Actor, original.Action, original.OriginalScope, cancellationToken).ConfigureAwait(false);
        if (!read.Allowed || !await original.Current(cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Canvas read baseline is no longer available.");
        return new OriginalWrite(this, original);
    }

    public bool IsIssuedOriginalWrite(IOriginalCanonicalWriteContext context, AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope) => context is OriginalWrite write && ReferenceEquals(write.Issuer, this) &&
        ReferenceEquals(write.Original.Issuer, this) && !write.Original.IsRetired && write.Original.Actor == actor &&
        actionId == "canvas.file.save" && scope.Access == ResourceAccess.Write && scope == write.OriginalScope;

    public async ValueTask<ResourceAccessDecision> EvaluateOriginalWriteAsync(IOriginalCanonicalWriteContext context,
        AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken cancellationToken)
    {
        ResourceAccessDecision Deny() => new(false, "FilesOriginalWriteChanged", actor.ActorId, scope.Revision, actor.OrganisationId);
        if (!IsIssuedOriginalWrite(context, actor, actionId, scope)) return Deny();
        var original = ((OriginalWrite)context).Original;
        if (!await original.Current(cancellationToken).ConfigureAwait(false)) return Deny();
        try
        {
            var result = await EvaluateCoreAsync(actor, actionId, scope, cancellationToken,
                original.Provider, original.Current, original.StoreId).ConfigureAwait(false);
            if (!result.Allowed) original.Retire();
            return await original.Current(cancellationToken).ConfigureAwait(false) ? result : Deny();
        }
        catch (FilesOriginalStoreReadChangedException) { original.Retire(); return Deny(); }
    }

    public string ResourceKind => "files.item";
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken) => EvaluateCoreAsync(actor, actionId, scope, cancellationToken, null);

    private async ValueTask<ResourceAccessDecision> EvaluateCoreAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken, IFilesProvider? originalProvider, Func<CancellationToken, ValueTask<bool>>? originalCurrent = null, Guid? originalStoreId = null)
    {
        ResourceAccessDecision Deny(string code) => new(false, code, actor.ActorId, scope.Revision, actor.OrganisationId);
        if (scope.Kind != ResourceKind || !Guid.TryParse(scope.Id, out var id) || id == Guid.Empty || actor.OrganisationId is not null)
            return Deny("FilesScopeInvalid");
        var pictureExport = actionId == "picture.file.export";
        var pictureCopy = actionId == "picture.file.copy";
        var pictureImport = actionId == "picture.file.import";
        var sitesWrite = actionId is "sites.project.create" or "sites.project.save";
        var mediaRead = actionId == "media.asset.read" && scope.Access == ResourceAccess.Read;
        var folderRead = actionId == "files.folder.native-root.read" && scope.Access == ResourceAccess.Read;
        var browserRead = actionId == "files.browser.read" && scope.Access == ResourceAccess.Read;
        var originalAgentRename = actionId == "files.agent.rename" && scope.Access == ResourceAccess.Write;
        var packageRead = actionId == "os.compatibility.package.read" && scope.Access == ResourceAccess.Read;
        var mailAttachmentRead = actionId == "mail.attachment.read" && scope.Access == ResourceAccess.Read
            && originalProvider is DurableDriveProvider && originalStoreId is not null;
        var ownerApp = actionId switch
        {
            "write.file.open" or "write.file.save" or "write.file.create" => "write",
            "canvas.file.open" or "canvas.file.save" or "canvas.file.create" => "canvas",
            "games.file.open" or "games.file.save" or "games.file.create" or "games.scene.observe" => "games",
            "picture.file.open" or "picture.file.save" or "picture.file.create" or "picture.file.export" or "picture.file.import" or "picture.file.copy" => "picture",
            _ => null
        };
        if ((sitesWrite || pictureImport) && scope.Access != ResourceAccess.Write) return Deny("FilesActionInvalid");
        if ((pictureExport || pictureCopy) && scope.Access is not (ResourceAccess.Read or ResourceAccess.Write)) return Deny("FilesActionInvalid");
        if (!pictureExport && !pictureCopy && !sitesWrite && !mediaRead && !packageRead && !mailAttachmentRead && !folderRead && !browserRead && !originalAgentRename && (ownerApp is null || scope.Access != ((actionId.EndsWith(".open", StringComparison.Ordinal) || actionId == "games.scene.observe") ? ResourceAccess.Read : ResourceAccess.Write)))
            return Deny("FilesActionInvalid");
        var provider = originalProvider ?? await _providers(actor, cancellationToken).ConfigureAwait(false);
        if (provider is null) return Deny("FilesProviderUnauthorised");
        if (originalStoreId is not null && provider is not DurableDriveProvider) return Deny("FilesOwningAppUnavailable");
        var result = originalStoreId is { } store
            ? await ((DurableDriveProvider)provider).GetForOriginalStoreAsync(store, new(id), cancellationToken).ConfigureAwait(false)
            : await provider.GetAsync(new(id), cancellationToken).ConfigureAwait(false);
        if (originalCurrent is not null && !await originalCurrent(cancellationToken).ConfigureAwait(false)) return Deny("FilesOriginalReadChanged");
        if (!result.IsSuccess) return Deny("FilesItemUnavailable");
        var item = result.Value!;
        var owner = actor.AccountId is { } account ? account.ToString("N") : "local-profile:" + actor.ProfileId;
        if (item.OwnerPrincipalId != owner || item.Scope != "personal") return Deny("FilesOwnerDenied");
        var revision = scope.Revision; // Echo the exact validated scope token; GUID D/N spellings denote the same revision.
        if (item.CurrentRevisionId is { } expectedRevision
            ? !Guid.TryParse(scope.Revision, out var suppliedRevision) || suppliedRevision != expectedRevision.Value
            : scope.Revision != "uncommitted") return Deny("FilesRevisionConflict");
        if (originalAgentRename)
            return new(true, "Allowed", actor.ActorId, revision, null);
        if (sitesWrite || pictureImport || ((pictureExport || pictureCopy) && scope.Access == ResourceAccess.Write))
        {
            // This folder comes from the verified host configuration, never from caller arguments.
            var configured = _appFolders is null ? null : await _appFolders(actor, pictureExport || pictureImport || pictureCopy ? "picture" : "sites", cancellationToken).ConfigureAwait(false);
            if (item.Kind != HostedItemKind.Folder || configured != item.Id)
                return Deny("FilesAppFolderDenied");
            return new(true, "Allowed", actor.ActorId, revision, null);
        }
        // Editable-copy raw dependencies are exact personal Files reads, not writable artifacts.
        // The typed Picture intent binds every original source and destination together separately.
        if (pictureCopy && scope.Access == ResourceAccess.Read && item.Kind == HostedItemKind.File)
            return new(true, "Allowed", actor.ActorId, revision, null);
        if (folderRead) return item.Kind == HostedItemKind.Folder
            ? new(true, "Allowed", actor.ActorId, revision, null) : Deny("FilesNativeFolderInvalid");
        if (browserRead) return new(true, "Allowed", actor.ActorId, revision, null);
        if (mediaRead || packageRead || mailAttachmentRead) return item.Kind == HostedItemKind.File
            ? new(true, "Allowed", actor.ActorId, revision, null) : Deny("FilesMediaSourceInvalid");
        var creating = actionId.EndsWith(".create", StringComparison.Ordinal);
        if (creating && item.Kind != HostedItemKind.Folder) return Deny("FilesDestinationInvalid");
        if (!creating)
        {
            if (provider is not DurableDriveProvider durable) return Deny("FilesOwningAppUnavailable");
            var artifact = originalStoreId is { } originalStore
                ? await durable.GetArtifactForOriginalStoreAsync(originalStore, item.Id, item.CurrentRevisionId, cancellationToken).ConfigureAwait(false)
                : await durable.GetArtifactAsync(item.Id, cancellationToken).ConfigureAwait(false);
            if (originalCurrent is not null && !await originalCurrent(cancellationToken).ConfigureAwait(false)) return Deny("FilesOriginalReadChanged");
            var expectedType = ownerApp switch
            {
                "write" => nameof(FilesArtifactType.WriteDocument),
                "canvas" => nameof(FilesArtifactType.Canvas),
                "picture" => nameof(FilesArtifactType.Picture),
                "games" => nameof(FilesArtifactType.GameProject),
                _ => null
            };
            if (item.Kind != HostedItemKind.Artifact || !artifact.IsSuccess ||
                artifact.Value!.OwnerAppId != ownerApp || artifact.Value.ArtifactType != expectedType)
                return Deny("FilesOwningAppMismatch");
        }
        return new(true, "Allowed", actor.ActorId, revision, null);
    }
}
