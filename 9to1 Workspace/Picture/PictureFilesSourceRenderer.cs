using System.Security.Cryptography;
using Avalonia.Media.Imaging;
using Haven.Application;
using Haven.Core.Media;

namespace HavenOS.Images;

/// <summary>Obtains exact source bytes through the shared Files-owned media lease, never through a saved machine path.</summary>
public sealed class PictureFilesSourceRenderer(
    Func<PictureSourceAssetReference, CancellationToken, Task<MediaEngineResult<MediaAssetReadLease>>> resolveRetainedSource,
    ResourceAuthorizationService authorization)
{
    public async Task<PicturePinnedRasterSource> LoadAsync(PictureArtifactEnvelope artifact, Guid backingFilesRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var snapshot = PictureArtifactCodec.Deserialize(PictureArtifactCodec.Serialize(artifact));
        if (backingFilesRevision == Guid.Empty) throw new UnauthorizedAccessException("Picture source rendering requires a committed editable Files revision.");
        var scope = new ResourceScope("files.item", snapshot.BackingFileId.ToString(), backingFilesRevision.ToString(), ResourceAccess.Read);
        var actor = await authorization.AuthorizeAsync("picture.file.open", [scope], cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Current Files authority does not allow this Picture artifact.");
        var source = snapshot.SourceAsset ?? throw new NotSupportedException("This raster renderer requires a linked canonical source asset.");
        if (source.SizeBytes > PictureArtifactCodec.MaximumPayloadBytes)
            throw new NotSupportedException("The source asset exceeds the current full-quality raster materialization limit.");
        var resolved = await resolveRetainedSource(source, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
            throw new IOException(resolved.Error?.Message ?? "Files cannot materialize the exact retained Picture source revision.");
        await using var lease = resolved.Value;
        var proof = lease.Source;
        if (proof.AssetId.Value != source.AssetId || proof.HostedItemId != source.FileId ||
            !Guid.TryParse(proof.SourceRevisionId, out var revision) || revision != source.RevisionId || !proof.SourceUri.IsFile)
            throw new InvalidDataException("The Files-owned media lease does not match the retained Picture source identity/revision.");
        byte[] bytes;
        await using (var input = new FileStream(proof.SourceUri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
        {
            if (input.Length != source.SizeBytes) throw new InvalidDataException("Picture source size differs from its canonical retained revision.");
            bytes = new byte[checked((int)input.Length)];
            await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(source.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Picture source bytes differ from the canonical retained revision hash.");
        if (await authorization.AuthorizeAsync("picture.file.open", [scope], cancellationToken).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Picture authority changed while loading the retained source revision.");
        return new(snapshot, bytes);
    }
}

/// <summary>Verified operation input. Render on the host's native UI thread; caller owns each returned raster frame.</summary>
public sealed class PicturePinnedRasterSource : IDisposable
{
    private byte[]? _bytes;
    private readonly PictureArtifactEnvelope _snapshot;
    private readonly object _gate = new();
    internal PicturePinnedRasterSource(PictureArtifactEnvelope snapshot, byte[] bytes) { _snapshot = snapshot; _bytes = bytes; }
    public Guid DocumentId => _snapshot.Document.DocumentId;
    public long Revision => _snapshot.Document.Revision;

    public Bitmap Render()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_bytes is null, this);
            using var input = new MemoryStream(_bytes, writable: false);
            using var bitmap = new Bitmap(input);
            return PictureCropService.Render(bitmap, _snapshot.Document);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_bytes is null) return;
            Array.Clear(_bytes);
            _bytes = null;
        }
    }
}
