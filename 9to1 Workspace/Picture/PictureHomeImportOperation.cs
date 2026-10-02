using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace HavenOS.Images;

/// <summary>One exact picker snapshot and new canonical identities, bound to a separate Home import approval.</summary>
public sealed class PictureImportIntent : IDisposable
{
    private readonly object _gate = new();
    private byte[]? _sourceBytes;
    private readonly byte[] _artifactBytes;
    private readonly JsonElement _arguments;
    internal PictureImportIntent(PicturePickedImage picked, AuthenticatedResourceActor actor,
        FilesWorkspaceDirectoryBinding binding, FilesRevisionId? folderRevision, Guid expectedStoreId)
    {
        if (expectedStoreId == Guid.Empty) throw new ArgumentException("Original store identity is required.", nameof(expectedStoreId));
        StoreId = expectedStoreId; Actor = actor; Binding = binding; FolderRevision = folderRevision;
        RawFileId = HostedItemId.New(); RawRevision = new(Guid.NewGuid()); BackingFileId = HostedItemId.New();
        Name = picked.Name; MimeType = picked.MimeType; _sourceBytes = picked.CopyBytes(); SourceHash = picked.ContentHash;
        SizeBytes = _sourceBytes.LongLength;
        var displayName = Path.GetFileNameWithoutExtension(Name);
        var document = new PictureDocument { DisplayName = string.IsNullOrWhiteSpace(displayName) ? Name : displayName,
            CanvasWidth = picked.Width, CanvasHeight = picked.Height, FileId = RawFileId.ToString(), SourceRevision = RawRevision.ToString() };
        Artifact = new() { BackingFileId = BackingFileId.Value, Document = document,
            SourceAsset = new(RawFileId.Value, RawRevision.Value, SourceHash, SizeBytes, Guid.NewGuid()) };
        _artifactBytes = PictureArtifactCodec.Serialize(Artifact);
        ArtifactHash = Convert.ToHexString(SHA256.HashData(_artifactBytes));
        ArtifactName = Name + ".picture.json";
        Scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", binding.FolderId.ToString(),
            folderRevision?.ToString() ?? "uncommitted", ResourceAccess.Write) });
        _arguments = JsonSerializer.SerializeToElement(new { storeId = StoreId, name = Name, mimeType = MimeType, rawFileId = RawFileId.Value,
            rawRevision = RawRevision.Value, sourceHash = SourceHash, sizeBytes = SizeBytes,
            backingFileId = BackingFileId.Value, documentId = document.DocumentId, artifactHash = ArtifactHash,
            sourceAsset = Artifact.SourceAsset, artifactName = ArtifactName, destinationFolderId = binding.FolderId.Value,
            destinationRevision = folderRevision?.Value, sourcePreservation = "original-encoded-bytes-all-frames-and-metadata" });
    }
    public Guid StoreId { get; }
    public const string ActionId = "picture.file.import";
    public string Name { get; }
    public string MimeType { get; }
    public string ArtifactName { get; }
    public long SizeBytes { get; }
    public string SourceHash { get; }
    public string ArtifactHash { get; }
    public HostedItemId RawFileId { get; }
    public FilesRevisionId RawRevision { get; }
    public HostedItemId BackingFileId { get; }
    public JsonElement Arguments => _arguments.Clone();
    public IReadOnlyList<ResourceScope> Scopes { get; }
    internal PictureArtifactEnvelope Artifact { get; }
    internal AuthenticatedResourceActor Actor { get; }
    internal FilesWorkspaceDirectoryBinding Binding { get; }
    internal FilesRevisionId? FolderRevision { get; }
    internal byte[] CopySourceBytes()
    {
        lock (_gate) { ObjectDisposedException.ThrowIf(_sourceBytes is null, this); return _sourceBytes!.ToArray(); }
    }
    internal byte[] CopyArtifactBytes() => _artifactBytes.ToArray();
    public void Dispose()
    {
        lock (_gate) { if (_sourceBytes is not null) Array.Clear(_sourceBytes); _sourceBytes = null; }
    }
}

/// <summary>Publishes original picked bytes and their editable Picture envelope atomically through canonical Files.</summary>
public sealed class PictureHomeImportOperation(HomeResourceOperationBroker home,
    IAuthenticatedResourceActorSource actors, Func<AuthenticatedResourceActor, DurableDriveProvider?> providers,
    FilesWorkspaceDirectoryResolver directories, ResourceAuthorizationService authorization, Func<bool> hostAllowsWrites,
    Func<AuthenticatedResourceActor, DurableDriveProvider, CancellationToken, ValueTask<FilesCommitAuthorityGuard>>? captureCommitAuthority = null)
{
    public async Task<PictureImportIntent> PrepareAsync(PicturePickedImage picked, Guid expectedStoreId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(picked);
        var actor = await ActorAsync(cancellationToken).ConfigureAwait(false);
        var provider = providers(actor) ?? throw new UnauthorizedAccessException("No canonical Files provider is available.");
        await provider.GetStoreEvidenceAsync(expectedStoreId, cancellationToken).ConfigureAwait(false);
        var binding = await BindingAsync(actor, cancellationToken).ConfigureAwait(false);
        var folder = await provider.GetAsync(binding.FolderId, cancellationToken).ConfigureAwait(false);
        if (!folder.IsSuccess) throw new UnauthorizedAccessException("The configured Picture folder is unavailable.");
        var intent = new PictureImportIntent(picked, actor, binding, folder.Value!.CurrentRevisionId, expectedStoreId);
        try { await ValidateAsync(intent, cancellationToken).ConfigureAwait(false); return intent; }
        catch { intent.Dispose(); throw; }
    }

    public async Task<PictureFilesOpenResult> ExecuteAsync(PictureImportIntent intent,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        var provider = await ValidateAsync(intent, cancellationToken).ConfigureAwait(false);
        var rawBytes = intent.CopySourceBytes();
        var artifactBytes = intent.CopyArtifactBytes();
        try
        {
            var claimed = await home.ClaimExecutionAsync(capability, "picture", PictureImportIntent.ActionId,
                intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false);
            if (claimed != intent.Actor) throw new UnauthorizedAccessException("Home did not grant this exact import.");
            var rawRelative = Path.Combine(".9to1-picture-imports", intent.RawFileId.Value.ToString("N"), intent.RawRevision.Value.ToString("N") + ".source");
            var artifactRelative = Path.Combine(".9to1-artifacts", intent.BackingFileId.ToString(), Guid.NewGuid().ToString("N") + ".picture.json");
            await WriteCandidateAsync(intent.Binding.DirectoryPath, rawRelative, rawBytes, cancellationToken).ConfigureAwait(false);
            await WriteCandidateAsync(intent.Binding.DirectoryPath, artifactRelative, artifactBytes, cancellationToken).ConfigureAwait(false);
            if (!ReferenceEquals(await ValidateAsync(intent, cancellationToken).ConfigureAwait(false), provider))
                throw new UnauthorizedAccessException("The canonical Files provider changed during import.");
            SafePath(intent.Binding.DirectoryPath, rawRelative); SafePath(intent.Binding.DirectoryPath, artifactRelative);
            var now = DateTimeOffset.UtcNow;
            var document = intent.Artifact.Document;
            var commitAuthority = captureCommitAuthority is null
                ? new FilesCommitAuthorityGuard(intent.Actor.ActorId, async token =>
                    await actors.GetCurrentAsync(token).ConfigureAwait(false) == intent.Actor && hostAllowsWrites())
                : await captureCommitAuthority(intent.Actor, provider, cancellationToken).ConfigureAwait(false);
            var result = await provider.CommitImportedArtifactAsync(
                new(intent.RawFileId, intent.Binding.FolderId, intent.Name, intent.MimeType, intent.RawRevision, null,
                    intent.Actor.ActorId, now, rawBytes.LongLength, intent.SourceHash, rawRelative),
                new("picture", document.DocumentId.ToString("N"), intent.BackingFileId, intent.Binding.FolderId,
                    nameof(FilesArtifactType.Picture), intent.ArtifactName),
                new(intent.BackingFileId, "picture", document.DocumentId.ToString("N") + ":" + document.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    intent.Actor.ActorId, now, artifactBytes.LongLength, intent.ArtifactHash, artifactRelative, null),
                [new(intent.Binding.FolderId, intent.FolderRevision)],
                intent.StoreId, commitAuthority,
                cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Error!.Message + " Unpublished immutable candidates remain recoverable.");
            return new(PictureArtifactCodec.Deserialize(artifactBytes), result.Value!.ArtifactRevision, result.Value.ArtifactRevision.Id) { StoreId = intent.StoreId };
        }
        finally { Array.Clear(rawBytes); Array.Clear(artifactBytes); }
    }

    private async Task<DurableDriveProvider> ValidateAsync(PictureImportIntent intent, CancellationToken ct)
    {
        var provider = providers(intent.Actor) ?? throw new UnauthorizedAccessException("No canonical Files provider is available.");
        await provider.GetStoreEvidenceAsync(intent.StoreId, ct).ConfigureAwait(false);
        if (await ActorAsync(ct).ConfigureAwait(false) != intent.Actor || await BindingAsync(intent.Actor, ct).ConfigureAwait(false) != intent.Binding ||
            await authorization.AuthorizeAsync(PictureImportIntent.ActionId, intent.Scopes, ct).ConfigureAwait(false) != intent.Actor)
            throw new UnauthorizedAccessException("The authenticated actor or import destination changed.");
        return provider;
    }
    private async Task<AuthenticatedResourceActor> ActorAsync(CancellationToken ct)
    {
        if (!hostAllowsWrites()) throw new UnauthorizedAccessException("The Picture host is read-only.");
        return await actors.GetCurrentAsync(ct).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("No authenticated Home actor is active.");
    }
    private async Task<FilesWorkspaceDirectoryBinding> BindingAsync(AuthenticatedResourceActor actor, CancellationToken ct)
    {
        var result = actor.AccountId is { } account
            ? await directories.ResolveAsync(account, "picture", ct).ConfigureAwait(false)
            : Guid.TryParse(actor.ProfileId, out var profile) ? await directories.ResolveProfileAsync(profile, "picture", ct).ConfigureAwait(false)
            : throw new UnauthorizedAccessException("The current Home profile identity is invalid.");
        return result.IsSuccess ? result.Value! : throw new UnauthorizedAccessException(result.Error!.Message);
    }
    private static async Task WriteCandidateAsync(string root, string relative, byte[] bytes, CancellationToken ct)
    {
        var path = SafePath(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); path = SafePath(root, relative);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await output.WriteAsync(bytes, ct).ConfigureAwait(false); await output.FlushAsync(ct).ConfigureAwait(false); output.Flush(true);
    }
    private static string SafePath(string root, string relative)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(fullRoot)) throw new UnauthorizedAccessException("The canonical Picture directory is unavailable.");
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Import escapes its configured directory.");
        for (var current = path; ; current = Path.GetDirectoryName(current)!)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("The import directory redirects outside its authority.");
            if (current == fullRoot) break;
        }
        return path;
    }
}
