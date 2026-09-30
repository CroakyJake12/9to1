using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Haven.Application;
using Haven.Core.Media;

namespace HavenOS.Images;

/// <summary>Obtains exact source bytes through the shared Files-owned media lease, never through a saved machine path.</summary>
public sealed class PictureFilesSourceRenderer(
    Func<PictureSourceAssetReference, CancellationToken, Task<MediaEngineResult<MediaAssetReadLease>>> resolveRetainedSource,
    ResourceAuthorizationService authorization)
{
    public Task<PicturePinnedRasterSource> LoadAsync(PictureArtifactEnvelope artifact, Guid backingFilesRevision,
        CancellationToken cancellationToken = default) => LoadCoreAsync(artifact, backingFilesRevision, null, cancellationToken);

    /// <summary>
    /// Explicit controlled-donor pipeline. Decode the exact Files lease bytes
    /// under mandatory native BWRAP, then fresh-check authority before handing
    /// an owned pixel input to the native UI. Native failures never fall back.
    /// Cancellation is observed before/after synchronous donor decoding; it
    /// does not currently interrupt a running native decoder operation.
    /// </summary>
    public Task<PicturePinnedRasterSource> LoadWithGlycinAsync(PictureArtifactEnvelope artifact, Guid backingFilesRevision,
        PictureGlycinDecoder decoder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        return LoadCoreAsync(artifact, backingFilesRevision, decoder, cancellationToken);
    }

    private async Task<PicturePinnedRasterSource> LoadCoreAsync(PictureArtifactEnvelope artifact, Guid backingFilesRevision,
        PictureGlycinDecoder? decoder,
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
            try { await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false); }
            catch { Array.Clear(bytes); throw; }
        }
        PictureGlycinFrame? decoded = null;
        var bytesTransferred = false;
        var pixelsTransferred = false;
        try
        {
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(source.ContentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Picture source bytes differ from the canonical retained revision hash.");
            if (decoder is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await authorization.AuthorizeAsync("picture.file.open", [scope], cancellationToken).ConfigureAwait(false) != actor)
                    throw new UnauthorizedAccessException("Picture authority changed before native source decoding.");
                decoded = await Task.Run(() => decoder.DecodeFirstFrame(bytes), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                // This raster surface is explicitly SDR/sRGB. Preserve linked
                // source and reject CICP/HDR/unconverted ICC instead of silently
                // assigning sRGB to pixels whose colour space differs.
                if (decoded.ColorMode != 1)
                    throw new NotSupportedException("This Picture raster surface requires decoded sRGB; retained HDR/CICP/ICC needs a colour-aware surface.");
            }
            if (await authorization.AuthorizeAsync("picture.file.open", [scope], cancellationToken).ConfigureAwait(false) != actor)
                throw new UnauthorizedAccessException("Picture authority changed while loading the retained source revision.");
            if (decoded is not null)
            {
                var result = new PicturePinnedRasterSource(snapshot, decoded);
                pixelsTransferred = true;
                return result;
            }
            bytesTransferred = true;
            return new(snapshot, bytes);
        }
        finally
        {
            if (!bytesTransferred) Array.Clear(bytes);
            if (decoded is not null && !pixelsTransferred) ClearFrame(decoded);
        }
    }

    internal static void ClearFrame(PictureGlycinFrame frame)
    {
        Array.Clear(frame.BgraPremultipliedPixels);
        if (frame.IccProfile is not null) Array.Clear(frame.IccProfile);
    }
}

/// <summary>Verified operation input. Render on the host's native UI thread; caller owns each returned raster frame.</summary>
public sealed class PicturePinnedRasterSource : IDisposable
{
    private byte[]? _bytes;
    private PictureGlycinFrame? _decoded;
    private bool _disposed;
    private readonly PictureArtifactEnvelope _snapshot;
    private readonly object _gate = new();
    internal PicturePinnedRasterSource(PictureArtifactEnvelope snapshot, byte[] bytes) { _snapshot = snapshot; _bytes = bytes; }
    internal PicturePinnedRasterSource(PictureArtifactEnvelope snapshot, PictureGlycinFrame decoded) { _snapshot = snapshot; _decoded = decoded; }
    public Guid DocumentId => _snapshot.Document.DocumentId;
    public long Revision => _snapshot.Document.Revision;

    public Bitmap Render()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_decoded is not null) return RenderDecoded(_decoded, _snapshot.Document);
            using var input = new MemoryStream(_bytes!, writable: false);
            using var bitmap = new Bitmap(input);
            return PictureCropService.Render(bitmap, _snapshot.Document);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_bytes is not null) Array.Clear(_bytes);
            _bytes = null;
            if (_decoded is not null) PictureFilesSourceRenderer.ClearFrame(_decoded);
            _decoded = null;
        }
    }

    private static Bitmap RenderDecoded(PictureGlycinFrame frame, PictureDocument document)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(checked((int)frame.Width), checked((int)frame.Height)),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var destination = bitmap.Lock())
        {
            var rowBytes = checked((int)frame.Width * 4);
            if (destination.RowBytes < rowBytes) throw new InvalidDataException("The native Picture raster has insufficient row storage.");
            for (var row = 0; row < frame.Height; row++)
                Marshal.Copy(frame.BgraPremultipliedPixels, checked((int)((ulong)row * frame.Stride)),
                    IntPtr.Add(destination.Address, checked((int)row * destination.RowBytes)), rowBytes);
        }
        return PictureCropService.Render(bitmap, document);
    }
}
