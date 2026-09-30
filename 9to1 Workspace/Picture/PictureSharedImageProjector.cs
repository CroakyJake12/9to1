using Avalonia.Threading;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace HavenOS.Images;

/// <summary>A reference to the existing editable Picture and its retained raw asset, not another editable image store.</summary>
public sealed record PictureSharedImageReference(Guid BackingFileId, Guid DocumentId, long DocumentRevision,
    PictureSourceAssetReference SourceAsset);

/// <summary>One explicitly prepared render scope. Hosts invalidate this and their displayed copies on authority change.</summary>
public sealed class PreparedPictureSharedImage : IDisposable
{
    private HomeProductivityRasterFrame? _frame;
    internal PreparedPictureSharedImage(PictureSharedImageReference reference, FilesRevisionId filesRevision, HomeProductivityRasterFrame frame)
    { Reference = reference; FilesRevision = filesRevision; _frame = frame; }
    public PictureSharedImageReference Reference { get; }
    public FilesRevisionId FilesRevision { get; }
    internal HomeProductivityRasterFrame Frame => Volatile.Read(ref _frame) ?? throw new ObjectDisposedException(nameof(PreparedPictureSharedImage));
    public void Dispose() => Interlocked.Exchange(ref _frame, null);
}

/// <summary>Async owning acquisition before the shared synchronous renderer. It never acquires or persists a second image identity.</summary>
public sealed class PictureSharedImageProjector(PictureFilesArtifactBridge files, PictureFilesSourceRenderer renderer, PictureGlycinDecoder decoder)
{
    public async Task<PreparedPictureSharedImage> PrepareAsync(PictureSharedImageReference reference,
        FilesRevisionId expectedFilesRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.BackingFileId == Guid.Empty || reference.DocumentId == Guid.Empty || reference.DocumentRevision < 0 ||
            reference.SourceAsset is null || expectedFilesRevision.Value == Guid.Empty)
            throw new InvalidDataException("An image projection requires the exact committed owning Picture reference.");
        var opened = await files.OpenAsync(new(reference.BackingFileId), cancellationToken).ConfigureAwait(false);
        Validate(opened, reference, expectedFilesRevision);
        using var pinned = await renderer.LoadWithGlycinAsync(opened.Artifact, expectedFilesRevision.Value, decoder, cancellationToken).ConfigureAwait(false);
        var frame = await Dispatcher.UIThread.InvokeAsync(pinned.RenderSharedFrame);
        await pinned.ValidateAccessAsync(cancellationToken).ConfigureAwait(false);
        var current = await files.OpenAsync(new(reference.BackingFileId), cancellationToken).ConfigureAwait(false);
        Validate(current, reference, expectedFilesRevision);
        return new(reference, expectedFilesRevision, frame);
    }
    private static void Validate(PictureFilesOpenResult opened, PictureSharedImageReference reference, FilesRevisionId expected)
    {
        if (opened.CasRevisionId != expected || opened.Artifact.BackingFileId != reference.BackingFileId ||
            opened.Artifact.Document.DocumentId != reference.DocumentId || opened.Artifact.Document.Revision != reference.DocumentRevision ||
            opened.Artifact.SourceAsset != reference.SourceAsset)
            throw new InvalidOperationException("The shared image reference no longer matches the canonical Picture revision.");
    }
}
