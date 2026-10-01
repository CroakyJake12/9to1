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
    /// Cancellation reaches the donor GCancellable during load/frame work.
    /// A cancelled native session is terminal and must reopen the retained source.
    /// </summary>
    public Task<PicturePinnedRasterSource> LoadWithGlycinAsync(PictureArtifactEnvelope artifact, Guid backingFilesRevision,
        PictureGlycinDecoder decoder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        return LoadCoreAsync(artifact, backingFilesRevision, decoder, cancellationToken);
    }

    /// <summary>Preserves the donor animation session for explicit stepping/playback. Source document and revisions remain unchanged.</summary>
    public Task<PicturePinnedRasterSource> LoadAnimationWithGlycinAsync(PictureArtifactEnvelope artifact, Guid backingFilesRevision,
        PictureGlycinDecoder decoder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        return LoadCoreAsync(artifact, backingFilesRevision, decoder, cancellationToken, animation: true);
    }

    private async Task<PicturePinnedRasterSource> LoadCoreAsync(PictureArtifactEnvelope artifact, Guid backingFilesRevision,
        PictureGlycinDecoder? decoder,
        CancellationToken cancellationToken = default, bool animation = false)
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
        PictureGlycinDecoder.FrameSession? frames = null;
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
                if (animation)
                {
                    frames = await Task.Run(() => decoder.OpenFrames(bytes, true, cancellationToken), cancellationToken).ConfigureAwait(false);
                    decoded = await Task.Run(() => frames.NextFrame(cancellationToken), cancellationToken).ConfigureAwait(false);
                }
                else decoded = await Task.Run(() => decoder.DecodeFirstFrame(bytes, cancellationToken), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                // This raster surface is explicitly SDR/sRGB. Preserve linked
                // source and reject CICP/HDR/unconverted ICC instead of silently
                // assigning sRGB to pixels whose colour space differs.
                if (decoded.ColorMode != 1)
                    throw new NotSupportedException("This Picture raster surface requires decoded sRGB; retained HDR/CICP/ICC needs a colour-aware surface.");
            }
            if (await authorization.AuthorizeAsync("picture.file.open", [scope], cancellationToken).ConfigureAwait(false) != actor)
                throw new UnauthorizedAccessException("Picture authority changed while loading the retained source revision.");
            async Task RecheckFrameAuthority(CancellationToken token)
            {
                if (await authorization.AuthorizeAsync("picture.file.open", [scope], token).ConfigureAwait(false) != actor)
                    throw new UnauthorizedAccessException("Picture authority changed during animation playback.");
                var currentSource = await resolveRetainedSource(source, token).ConfigureAwait(false);
                if (!currentSource.IsSuccess || currentSource.Value is null)
                    throw new UnauthorizedAccessException("Current Files authority cannot access the retained animation source.");
                await using var currentLease = currentSource.Value;
                var currentProof = currentLease.Source;
                if (currentProof.AssetId.Value != source.AssetId || currentProof.HostedItemId != source.FileId ||
                    !Guid.TryParse(currentProof.SourceRevisionId, out var currentRevision) || currentRevision != source.RevisionId || !currentProof.SourceUri.IsFile)
                    throw new InvalidDataException("Animation source authority resolved a different canonical asset.");
            }
            // The first frame/static bytes also require fresh raw-source authority after materialization.
            await RecheckFrameAuthority(cancellationToken).ConfigureAwait(false);
            if (decoded is not null)
            {
                if (decoded.DelayMicroseconds <= 0) { frames?.Dispose(); frames = null; }
                var result = new PicturePinnedRasterSource(snapshot, decoded, frames, RecheckFrameAuthority);
                frames = null; // ownership transferred only after successful construction
                pixelsTransferred = true;
                return result;
            }
            bytesTransferred = true;
            return new(snapshot, bytes);
        }
        finally
        {
            frames?.Dispose();
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
    private PictureGlycinDecoder.FrameSession? _frames;
    private readonly Func<CancellationToken, Task>? _recheckFrameAuthority;
    private readonly SemaphoreSlim _advanceGate = new(1, 1);
    private bool _disposed;
    private readonly PictureArtifactEnvelope _snapshot;
    private readonly object _gate = new();
    internal PicturePinnedRasterSource(PictureArtifactEnvelope snapshot, byte[] bytes) { _snapshot = snapshot; _bytes = bytes; }
    internal PicturePinnedRasterSource(PictureArtifactEnvelope snapshot, PictureGlycinFrame decoded,
        PictureGlycinDecoder.FrameSession? frames = null, Func<CancellationToken, Task>? recheckFrameAuthority = null)
    { _snapshot = snapshot; _decoded = decoded; _frames = frames; _recheckFrameAuthority = recheckFrameAuthority; }
    public Guid DocumentId => _snapshot.Document.DocumentId;
    public long Revision => _snapshot.Document.Revision;

    /// <summary>Read-only information from the same authorized native frame; original source metadata is not rewritten.</summary>
    public string InformationSummary
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_decoded is not { } frame) return "Native source information is unavailable.";
                var lines = new List<string> { $"Decoded source: {frame.Width} × {frame.Height} pixels", $"Original file size: {_snapshot.SourceAsset?.SizeBytes} bytes" };
                if (frame.Metadata is { } metadata)
                {
                    lines.Insert(0, "Detected format: " + metadata.MimeType);
                    lines.Add("Source orientation (EXIF value): " + metadata.SourceOrientation);
                    lines.Add("Embedded text fields:");
                    if (metadata.Fields.Count == 0) lines.Add("No embedded text fields available.");
                    foreach (var item in metadata.Fields.Take(32))
                        lines.Add(item.Key + ": " + (item.Value.Length <= 512 ? item.Value : item.Value[..512] + "… [display shortened]"));
                    if (metadata.Fields.Count > 32) lines.Add("Additional fields are retained in the original source; this view shows the first 32.");
                }
                if (frame.MetadataNotice is not null) lines.Add("Embedded information unavailable: " + frame.MetadataNotice);
                lines.Add("The original source is retained.");
                return string.Join(Environment.NewLine, lines);
            }
        }
    }
    public PictureGlycinMetadata? Metadata { get { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); return _decoded?.Metadata; } } }
    public bool CanAdvanceFrames { get { lock (_gate) return !_disposed && _frames is not null; } }
    public long FrameDelayMicroseconds { get { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); return _decoded?.DelayMicroseconds ?? 0; } } }

    /// <summary>Revalidates the current pinned native source without advancing its frame. Hosts must clear their own copied bitmap on failure.</summary>
    public async Task ValidateAccessAsync(CancellationToken cancellationToken = default)
    {
        await _advanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate) ObjectDisposedException.ThrowIf(_disposed, this);
            if (_recheckFrameAuthority is null)
                throw new NotSupportedException("This source has no retained native authority validator.");
            await _recheckFrameAuthority(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidDataException or IOException)
        { Dispose(); throw; }
        finally { _advanceGate.Release(); }
    }

    /// <summary>Decode off the UI thread, then recheck backing and retained raw-source authority before replacing the preview.</summary>
    public async Task AdvanceFrameAsync(CancellationToken cancellationToken = default)
    {
        await _advanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        PictureGlycinFrame? candidate = null;
        try
        {
            PictureGlycinDecoder.FrameSession frames;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                frames = _frames ?? throw new NotSupportedException("This source was not opened for animation playback.");
            }
            await _recheckFrameAuthority!(cancellationToken).ConfigureAwait(false);
            candidate = await Task.Run(() => frames.NextFrame(cancellationToken), cancellationToken).ConfigureAwait(false);
            if (candidate.ColorMode != 1) throw new NotSupportedException("Animation playback requires sRGB frames; the original source remains retained.");
            await _recheckFrameAuthority(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_decoded is not null) PictureFilesSourceRenderer.ClearFrame(_decoded);
                _decoded = candidate;
                candidate = null;
            }
        }
        catch (UnauthorizedAccessException) { Dispose(); throw; }
        finally
        {
            if (candidate is not null) PictureFilesSourceRenderer.ClearFrame(candidate);
            _advanceGate.Release();
        }
    }

    /// <summary>UI-thread adapter for the canonical Home shared renderer. Uses only this already-authorized pinned input.</summary>
    public HavenOS.Home.Core.HomeProductivityRasterFrame RenderSharedFrame()
    {
        var width = _snapshot.Document.CanvasWidth;
        var height = _snapshot.Document.CanvasHeight;
        if (width is < 1 or > 8192 || height is < 1 or > 8192 || (long)width * height * 4 > 64 * 1024 * 1024)
            throw new NotSupportedException("This Picture preview exceeds the bounded shared raster frame; use the owning tiled surface.");
        using var rendered = Render();
        using var converted = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        try
        {
            using (var destination = converted.Lock())
            {
                rendered.CopyPixels(destination); // explicit pixel and alpha format transcode
                for (var row = 0; row < height; row++)
                    Marshal.Copy(IntPtr.Add(destination.Address, checked(row * destination.RowBytes)), pixels, checked(row * stride), stride);
            }
            return new(width, height, stride, pixels);
        }
        finally { Array.Clear(pixels); } // Home's immutable frame owns a detached copy
    }

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
            _frames?.Dispose(); _frames = null;
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
