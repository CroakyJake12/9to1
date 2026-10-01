using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace HavenOS.Images;

/// <summary>Exact editable-copy proposal. All retained identities are data, never a grant.</summary>
public sealed class PictureSaveCopyIntent
{
    private readonly byte[] _sourceBytes;
    private readonly JsonElement _arguments;
    internal PictureSaveCopyIntent(PictureFilesOpenResult source, AuthenticatedResourceActor originalActor,
        string displayName, PictureCreateCapture capture)
    {
        _sourceBytes = PictureArtifactCodec.Serialize(source.Artifact);
        SourceFileId = new(source.Artifact.BackingFileId); SourceFilesRevision = source.CasRevisionId;
        StoreId = source.StoreId; OriginalActor = originalActor; Capture = capture;
        NewDocumentId = Guid.NewGuid(); DisplayName = displayName;
        var folderRevision = capture.FolderRevision ?? throw new ArgumentException("The captured folder revision is required.", nameof(capture));
        var scopes = new List<ResourceScope>
        {
            new("files.item", SourceFileId.ToString(), SourceFilesRevision.ToString(), ResourceAccess.Read),
            new("files.item", capture.Binding.FolderId.ToString(), folderRevision.ToString(), ResourceAccess.Write)
        };
        if (capture.SourceAsset is { } raw)
        {
            var rawRevision = raw.ExpectedRevision ?? throw new ArgumentException("The captured raw-source revision is required.", nameof(capture));
            scopes.Add(new("files.item", raw.ItemId.ToString(), rawRevision.ToString(), ResourceAccess.Read));
        }
        Scopes = scopes.AsReadOnly();
        _arguments = JsonSerializer.SerializeToElement(new
        {
            operation = "saveEditableCopy", storeId = StoreId, originalActorId = originalActor.ActorId,
            sourceFileId = SourceFileId.Value, sourceFilesRevision = SourceFilesRevision.Value,
            sourceDocumentId = source.Artifact.Document.DocumentId, sourceDocumentRevision = source.Artifact.Document.Revision,
            sourceSnapshotSha256 = Convert.ToHexString(SHA256.HashData(_sourceBytes)),
            newFileId = capture.FileId.Value, newDocumentId = NewDocumentId, displayName,
            destinationFolderId = capture.Binding.FolderId.Value, destinationFolderRevision = folderRevision.Value,
            rawSourceFileId = capture.SourceAsset?.ItemId.Value, rawSourceMetadataRevision = capture.SourceAsset?.ExpectedRevision?.Value
        });
    }
    public const string ActionId = "picture.file.copy";
    public HostedItemId SourceFileId { get; }
    public FilesRevisionId SourceFilesRevision { get; }
    public Guid StoreId { get; }
    public AuthenticatedResourceActor OriginalActor { get; }
    public Guid NewDocumentId { get; }
    public string DisplayName { get; }
    public PictureCreateCapture Capture { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();
    internal PictureArtifactEnvelope Source => PictureArtifactCodec.Deserialize(_sourceBytes);
    internal bool Matches(PictureFilesOpenResult current) => current.StoreId == StoreId && current.CasRevisionId == SourceFilesRevision &&
        PictureArtifactCodec.Serialize(current.Artifact).AsSpan().SequenceEqual(_sourceBytes);
    internal PictureDocument CopyDocument()
    {
        var source = Source.Document;
        return new()
        {
            DocumentId = NewDocumentId, SchemaVersion = source.SchemaVersion, DisplayName = DisplayName,
            FileId = source.FileId, SourceRevision = source.SourceRevision, SourcePath = source.SourcePath,
            CanvasWidth = source.CanvasWidth, CanvasHeight = source.CanvasHeight,
            Operations = source.Operations.ToArray(), Revision = 0
        };
    }
}

/// <summary>Save a Copy creates a new canonical editable raster artifact retaining the original raw asset.
/// It does not flatten, decode, duplicate raw bytes, or replay a consumed Home capability.</summary>
public sealed class PictureHomeSaveCopyOperation(PictureFilesArtifactBridge files, HomeResourceOperationBroker home,
    IAuthenticatedResourceActorSource actors, Func<AuthenticatedResourceActor, DurableDriveProvider?> providers,
    FilesWorkspaceDirectoryResolver directories, ResourceAuthorizationService authorization, Func<bool> hostAllowsWrites)
{
    public async Task<PictureSaveCopyIntent> PrepareAsync(PictureFilesOpenResult captured,
        AuthenticatedResourceActor originalActor, string displayName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captured); ArgumentNullException.ThrowIfNull(originalActor); ArgumentNullException.ThrowIfNull(displayName);
        displayName = displayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 128 || displayName is "." or ".." ||
            displayName.Any(c => char.IsControl(c) || c is '/' or '\\'))
            throw new ArgumentException("Choose a nonempty Picture copy name without path separators.", nameof(displayName));
        if (captured.StoreId == Guid.Empty || captured.CasRevisionId.Value == Guid.Empty)
            throw new ArgumentException("Save a Copy requires an original committed Files store and revision.", nameof(captured));
        // Capture before awaits; mutable caller envelopes never become a later proposal.
        var snapshot = captured with { Artifact = PictureArtifactCodec.Deserialize(PictureArtifactCodec.Serialize(captured.Artifact)) };
        if (!hostAllowsWrites() || await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != originalActor)
            throw new UnauthorizedAccessException("The captured Picture actor or writable host is unavailable.");
        var provider = providers(originalActor) ?? throw new UnauthorizedAccessException("The original Files provider is unavailable.");
        await provider.GetStoreEvidenceAsync(snapshot.StoreId, cancellationToken).ConfigureAwait(false);
        var current = await files.OpenAsync(new(snapshot.Artifact.BackingFileId), snapshot.StoreId, cancellationToken).ConfigureAwait(false);
        if (current.CasRevisionId != snapshot.CasRevisionId ||
            !PictureArtifactCodec.Serialize(current.Artifact).AsSpan().SequenceEqual(PictureArtifactCodec.Serialize(snapshot.Artifact)))
            throw new InvalidOperationException("The Picture source changed before copy preparation.");
        var binding = await BindingAsync(originalActor, cancellationToken).ConfigureAwait(false);
        var folder = await provider.GetAsync(binding.FolderId, cancellationToken).ConfigureAwait(false);
        if (!folder.IsSuccess || folder.Value!.CurrentRevisionId is not { } folderRevision)
            throw new UnauthorizedAccessException("The canonical Pictures destination is unavailable.");
        FilesItemRevisionPrecondition? rawGuard = null;
        if (snapshot.Artifact.SourceAsset is { } raw)
        {
            var item = await provider.GetAsync(new(raw.FileId), cancellationToken).ConfigureAwait(false);
            if (!item.IsSuccess || item.Value!.CurrentRevisionId is not { } rawRevision)
                throw new UnauthorizedAccessException("The retained Picture source is unavailable.");
            rawGuard = new(new(raw.FileId), rawRevision);
        }
        else throw new NotSupportedException("Save a Copy currently supports a canonical retained raster source.");
        var capture = new PictureCreateCapture(HostedItemId.New(), binding, folderRevision, rawGuard,
            new(new(snapshot.Artifact.BackingFileId), snapshot.CasRevisionId));
        var intent = new PictureSaveCopyIntent(snapshot, originalActor, displayName, capture);
        await RequireAsync(intent, provider, cancellationToken).ConfigureAwait(false);
        return intent;
    }

    public async Task<PictureFilesOpenResult> ExecuteAsync(PictureSaveCopyIntent intent,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        var provider = providers(intent.OriginalActor) ?? throw new UnauthorizedAccessException("The original Files provider is unavailable.");
        await RequireAsync(intent, provider, cancellationToken).ConfigureAwait(false);
        var source = await files.OpenAsync(intent.SourceFileId, intent.StoreId, cancellationToken).ConfigureAwait(false);
        if (!intent.Matches(source)) throw new InvalidOperationException("The exact Picture source changed before Save a Copy.");
        var claimed = await home.ClaimExecutionAsync(capability, "picture", PictureSaveCopyIntent.ActionId,
            intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false);
        if (claimed is null || claimed != intent.OriginalActor) throw new UnauthorizedAccessException("Home did not approve this exact editable copy for its original actor.");
        // The guarded owning creation port retains every original source/destination
        // precondition through final same-store Files publication after claim.
        return await files.CreateAsync(intent.CopyDocument(), source.Artifact.SourceAsset,
            intent.StoreId, claimed, intent.Capture, cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireAsync(PictureSaveCopyIntent intent, DurableDriveProvider provider, CancellationToken ct)
    {
        if (!hostAllowsWrites() || await actors.GetCurrentAsync(ct).ConfigureAwait(false) != intent.OriginalActor ||
            !ReferenceEquals(providers(intent.OriginalActor), provider)) throw new UnauthorizedAccessException("Picture copy authority changed.");
        await provider.GetStoreEvidenceAsync(intent.StoreId, ct).ConfigureAwait(false);
        if (await BindingAsync(intent.OriginalActor, ct).ConfigureAwait(false) != intent.Capture.Binding)
            throw new UnauthorizedAccessException("The captured canonical Picture destination changed.");
        if (await authorization.AuthorizeAsync(PictureSaveCopyIntent.ActionId, intent.Scopes, ct).ConfigureAwait(false) != intent.OriginalActor)
            throw new UnauthorizedAccessException("The exact source and destination are no longer authorized.");
    }
    private async Task<FilesWorkspaceDirectoryBinding> BindingAsync(AuthenticatedResourceActor actor, CancellationToken ct)
    {
        var result = actor.AccountId is { } account
            ? await directories.ResolveAsync(account, "picture", ct).ConfigureAwait(false)
            : Guid.TryParse(actor.ProfileId, out var profile)
                ? await directories.ResolveProfileAsync(profile, "picture", ct).ConfigureAwait(false)
                : throw new UnauthorizedAccessException("The original Home profile is invalid.");
        return result.IsSuccess ? result.Value! : throw new UnauthorizedAccessException(result.Error!.Message);
    }
}
