using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Threading;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace HavenOS.Images;

/// <summary>Immutable preview of a new, flattened first-frame PNG. Created only by the owning renderer.</summary>
public sealed class PicturePngExportIntent
{
    private readonly byte[] _png;
    private readonly JsonElement _arguments;
    internal PicturePngExportIntent(PictureFilesOpenResult source, FilesWorkspaceDirectoryBinding binding,
        FilesRevisionId? folderRevision, FilesRevisionId rawRevision, AuthenticatedResourceActor actor, string name, byte[] png)
    {
        if (source.StoreId == Guid.Empty) throw new ArgumentException("Original store identity is required.", nameof(source));
        StoreId = source.StoreId; SourceFileId = new(source.Artifact.BackingFileId); SourceRevision = source.CasRevisionId;
        DocumentId = source.Artifact.Document.DocumentId; DocumentRevision = source.Artifact.Document.Revision;
        SourceAsset = source.Artifact.SourceAsset!; RawRevision = rawRevision;
        Binding = binding; Actor = actor; DestinationFolderId = binding.FolderId; DestinationRevision = folderRevision;
        FileName = name; OutputFileId = HostedItemId.New(); OutputRevision = new(Guid.NewGuid());
        _png = png.ToArray(); ContentHash = Convert.ToHexString(SHA256.HashData(_png));
        Scopes = Array.AsReadOnly(new[] {
            new ResourceScope("files.item", SourceFileId.ToString(), SourceRevision.ToString(), ResourceAccess.Read),
            new ResourceScope("files.item", DestinationFolderId.ToString(), DestinationRevision?.ToString() ?? "uncommitted", ResourceAccess.Write)
        });
        _arguments = JsonSerializer.SerializeToElement(new {
            storeId = StoreId, sourceFileId = SourceFileId.Value, sourceRevision = SourceRevision.Value, documentId = DocumentId, documentRevision = DocumentRevision,
            sourceAsset = SourceAsset, rawRevision = RawRevision.Value, destinationFolderId = DestinationFolderId.Value,
            destinationRevision = DestinationRevision?.Value, outputFileId = OutputFileId.Value, outputRevision = OutputRevision.Value,
            fileName = FileName, contentHash = ContentHash, sizeBytes = _png.LongLength,
            flatten = "first-frame-srgb-png-without-source-metadata", explicitSnapshotAcknowledgement = true
        });
    }
    public Guid StoreId { get; }
    public const string TargetAppId = "picture";
    public const string ActionId = "picture.file.export";
    public HostedItemId SourceFileId { get; }
    public FilesRevisionId SourceRevision { get; }
    public Guid DocumentId { get; }
    public long DocumentRevision { get; }
    public HostedItemId DestinationFolderId { get; }
    public FilesRevisionId? DestinationRevision { get; }
    public HostedItemId OutputFileId { get; }
    public FilesRevisionId OutputRevision { get; }
    public string FileName { get; }
    public string ContentHash { get; }
    public long SizeBytes => _png.LongLength;
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();
    internal PictureSourceAssetReference SourceAsset { get; }
    internal FilesRevisionId RawRevision { get; }
    internal FilesWorkspaceDirectoryBinding Binding { get; }
    internal AuthenticatedResourceActor Actor { get; }
    internal ReadOnlyMemory<byte> Png => _png;
}

public sealed record PicturePngExportCommit(HostedItemId FileId, FilesRevision Revision);

/// <summary>Renders the exact owning snapshot, then claims a distinct Home approval and atomically publishes a new Files item.</summary>
public sealed class PictureHomePngExportOperation(PictureFilesArtifactBridge files, PictureFilesSourceRenderer renderer,
    PictureGlycinDecoder decoder, PictureGlycinPngEncoder encoder, HomeResourceOperationBroker home,
    IAuthenticatedResourceActorSource actors, Func<AuthenticatedResourceActor, DurableDriveProvider?> providers,
    FilesWorkspaceDirectoryResolver directories, ResourceAuthorizationService authorization, Func<bool> hostAllowsWrites,
    Func<AuthenticatedResourceActor, DurableDriveProvider, CancellationToken, ValueTask<FilesCommitAuthorityGuard>>? captureCommitAuthority = null)
{
    public async Task<PicturePngExportIntent> PrepareAsync(HostedItemId sourceFileId, FilesRevisionId expectedRevision,
        Guid documentId, long documentRevision, string fileName, bool acknowledgeFlattenedFirstFrame, Guid expectedStoreId,
        CancellationToken cancellationToken = default)
    {
        if (!acknowledgeFlattenedFirstFrame)
            throw new InvalidOperationException("Explicit acknowledgement is required: export flattens the first frame to sRGB PNG without source metadata.");
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255 || fileName.IndexOfAny(['/', '\\', '\0']) >= 0 ||
            fileName != Path.GetFileName(fileName) || !fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A simple PNG file name is required.", nameof(fileName));
        var actor = await ActorAsync(cancellationToken).ConfigureAwait(false);
        var provider = providers(actor) ?? throw new UnauthorizedAccessException("No canonical Files provider is available.");
        await provider.GetStoreEvidenceAsync(expectedStoreId, cancellationToken).ConfigureAwait(false);
        var source = await files.OpenAsync(sourceFileId, expectedStoreId, cancellationToken).ConfigureAwait(false);
        RequireSource(source, expectedRevision, documentId, documentRevision);
        var raw = source.Artifact.SourceAsset ?? throw new NotSupportedException("PNG export requires a canonical retained raster source.");
        var rawMetadata = await provider.GetAsync(new(raw.FileId), cancellationToken).ConfigureAwait(false);
        if (!rawMetadata.IsSuccess || rawMetadata.Value!.CurrentRevisionId is not { } rawRevision)
            throw new UnauthorizedAccessException("The raw source has no current canonical revision.");
        var binding = await BindingAsync(actor, cancellationToken).ConfigureAwait(false);
        var folder = await provider.GetAsync(binding.FolderId, cancellationToken).ConfigureAwait(false);
        if (!folder.IsSuccess) throw new UnauthorizedAccessException("The configured Picture destination is unavailable.");
        using var pinned = await renderer.LoadWithGlycinAsync(source.Artifact, source.CasRevisionId.Value, decoder, cancellationToken).ConfigureAwait(false);
        var frame = await Dispatcher.UIThread.InvokeAsync(pinned.RenderSharedFrame);
        var png = await Task.Run(() => encoder.EncodeFlattenedFrame(frame, cancellationToken: cancellationToken), cancellationToken).ConfigureAwait(false);
        try
        {
            var intent = new PicturePngExportIntent(source, binding, folder.Value!.CurrentRevisionId, rawRevision, actor, fileName, png);
            await ValidateAsync(intent, cancellationToken).ConfigureAwait(false);
            return intent;
        }
        finally { Array.Clear(png); }
    }

    public async Task<PicturePngExportCommit> ExecuteAsync(PicturePngExportIntent intent,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        var provider = await ValidateAsync(intent, cancellationToken).ConfigureAwait(false);
        var claimed = await home.ClaimExecutionAsync(capability, PicturePngExportIntent.TargetAppId, PicturePngExportIntent.ActionId,
            intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false);
        if (claimed != intent.Actor) throw new UnauthorizedAccessException("Home did not grant this exact PNG export.");
        var relative = Path.Combine(".9to1-picture-exports", intent.OutputFileId.Value.ToString("N"), intent.OutputRevision.Value.ToString("N") + ".png");
        var path = SafePath(intent.Binding.DirectoryPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        path = SafePath(intent.Binding.DirectoryPath, relative);
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await output.WriteAsync(intent.Png, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }
        // A failed publication leaves an unreferenced immutable candidate for recovery, never a success acknowledgement.
        SafePath(intent.Binding.DirectoryPath, relative);
        var currentProvider = await ValidateAsync(intent, cancellationToken).ConfigureAwait(false);
        if (!ReferenceEquals(currentProvider, provider)) throw new UnauthorizedAccessException("The Files provider changed during export.");
        var commitAuthority = captureCommitAuthority is null
            ? new FilesCommitAuthorityGuard(intent.Actor.ActorId, async token =>
                await actors.GetCurrentAsync(token).ConfigureAwait(false) == intent.Actor && hostAllowsWrites())
            : await captureCommitAuthority(intent.Actor, provider, cancellationToken).ConfigureAwait(false);
        var committed = await provider.CommitUploadedContentAsync(new(intent.OutputFileId, intent.DestinationFolderId, intent.FileName,
            "image/png", intent.OutputRevision, null, intent.Actor.ActorId, DateTimeOffset.UtcNow, intent.SizeBytes, intent.ContentHash, relative),
            [new(intent.SourceFileId, intent.SourceRevision), new(intent.DestinationFolderId, intent.DestinationRevision),
             new(new(intent.SourceAsset.FileId), intent.RawRevision)],
            intent.StoreId, commitAuthority,
            cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess) throw new InvalidOperationException(committed.Error!.Message);
        return new(intent.OutputFileId, committed.Value!);
    }

    private async Task<DurableDriveProvider> ValidateAsync(PicturePngExportIntent intent, CancellationToken cancellationToken)
    {
        var provider = providers(intent.Actor) ?? throw new UnauthorizedAccessException("The canonical Files provider is unavailable.");
        await provider.GetStoreEvidenceAsync(intent.StoreId, cancellationToken).ConfigureAwait(false);
        if (await ActorAsync(cancellationToken).ConfigureAwait(false) != intent.Actor ||
            await BindingAsync(intent.Actor, cancellationToken).ConfigureAwait(false) != intent.Binding)
            throw new UnauthorizedAccessException("The authenticated actor or configured export destination changed.");
        var source = await files.OpenAsync(intent.SourceFileId, intent.StoreId, cancellationToken).ConfigureAwait(false);
        RequireSource(source, intent.SourceRevision, intent.DocumentId, intent.DocumentRevision);
        if (source.Artifact.SourceAsset != intent.SourceAsset)
            throw new InvalidDataException("The retained Picture source changed.");
        if (await authorization.AuthorizeAsync(PicturePngExportIntent.ActionId, intent.Scopes, cancellationToken).ConfigureAwait(false) != intent.Actor ||
            await authorization.AuthorizeAsync("media.asset.read", [new ResourceScope("files.item", intent.SourceAsset.FileId.ToString(),
                intent.RawRevision.ToString(), ResourceAccess.Read)], cancellationToken).ConfigureAwait(false) != intent.Actor)
            throw new UnauthorizedAccessException("Current Files authority does not allow this exact export.");
        return provider;
    }
    private async Task<AuthenticatedResourceActor> ActorAsync(CancellationToken cancellationToken)
    {
        if (!hostAllowsWrites()) throw new UnauthorizedAccessException("The Picture host is read-only.");
        return await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No authenticated Home actor is active.");
    }
    private async Task<FilesWorkspaceDirectoryBinding> BindingAsync(AuthenticatedResourceActor actor, CancellationToken cancellationToken)
    {
        var result = actor.AccountId is { } account
            ? await directories.ResolveAsync(account, "picture", cancellationToken).ConfigureAwait(false)
            : Guid.TryParse(actor.ProfileId, out var profile)
                ? await directories.ResolveProfileAsync(profile, "picture", cancellationToken).ConfigureAwait(false)
                : throw new UnauthorizedAccessException("The current Home profile identity is invalid.");
        if (!result.IsSuccess) throw new UnauthorizedAccessException(result.Error!.Message);
        return result.Value!;
    }
    private static void RequireSource(PictureFilesOpenResult source, FilesRevisionId revision, Guid documentId, long documentRevision)
    {
        if (source.CasRevisionId != revision || source.Artifact.Document.DocumentId != documentId || source.Artifact.Document.Revision != documentRevision)
            throw new InvalidOperationException("The canonical Picture snapshot changed before export.");
    }
    private static string SafePath(string root, string relative)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(fullRoot)) throw new UnauthorizedAccessException("The canonical Picture directory is unavailable.");
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Export escapes the canonical Picture directory.");
        for (var current = path; ; current = Path.GetDirectoryName(current)!)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("The export directory redirects outside its authority.");
            if (current == fullRoot) break;
        }
        return path;
    }
}
