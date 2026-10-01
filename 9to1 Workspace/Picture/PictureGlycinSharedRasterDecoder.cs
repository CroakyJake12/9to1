using HavenOS.Home.Core;

namespace HavenOS.Images;

public sealed record PictureSharedAnimationFrame(HomeProductivityRasterFrame Raster, long DelayMicroseconds);

/// <summary>
/// Pure byte adapter for an owning service's already-authorized encoded source.
/// This grants no asset authority; callers must bind and recheck their source identity and revision.
/// </summary>
public sealed class PictureGlycinSharedRasterDecoder
{
    public HomeProductivityRasterFrame DecodeFirstFrame(ReadOnlySpan<byte> encoded, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ConvertOwnedFrame(new PictureGlycinDecoder().DecodeFirstFrame(encoded, cancellationToken), cancellationToken);
    }

    public FrameSession OpenFrames(ReadOnlySpan<byte> encoded, bool loopAnimation = true) => new(new PictureGlycinDecoder().OpenFrames(encoded, loopAnimation));

    public FrameSession OpenFrames(ReadOnlySpan<byte> encoded, bool loopAnimation, CancellationToken cancellationToken) =>
        new(new PictureGlycinDecoder().OpenFrames(encoded, loopAnimation, cancellationToken));

    /// <summary>Bounded donor animation, not an ongoing resource grant. The host revalidates access before decoding and before presentation.</summary>
    public sealed class FrameSession : IDisposable
    {
        private readonly object _gate = new();
        private readonly PictureGlycinDecoder.FrameSession _native;
        private bool _disposed;
        internal FrameSession(PictureGlycinDecoder.FrameSession native) => _native = native;
        public string MimeType => _native.MimeType;
        public PictureSharedAnimationFrame NextFrame(CancellationToken cancellationToken = default) =>
            TryNextFrame(cancellationToken) ?? throw new EndOfStreamException("The non-looping image has no more frames.");
        public PictureSharedAnimationFrame? TryNextFrame(CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var decoded = _native.TryNextFrame(cancellationToken);
                if (decoded is null) return null;
                var delay = decoded.DelayMicroseconds;
                return new(ConvertOwnedFrame(decoded, cancellationToken), delay);
            }
        }
        public void Dispose()
        {
            // Interrupt the donor before waiting for this adapter's frame conversion lock.
            _native.Dispose();
            lock (_gate) { _disposed = true; }
        }
    }

    private static HomeProductivityRasterFrame ConvertOwnedFrame(PictureGlycinFrame decoded, CancellationToken cancellationToken)
    {
        byte[]? compact = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (decoded.ColorMode != 1)
                throw new NotSupportedException("The shared raster surface requires sRGB; HDR/CICP or unconverted ICC requires a colour-aware surface.");
            if (decoded.Width is 0 or > 8192 || decoded.Height is 0 or > 8192 || (ulong)decoded.Width * decoded.Height * 4 > 64 * 1024 * 1024)
                throw new NotSupportedException("The decoded frame exceeds the shared raster bounds.");
            var width = checked((int)decoded.Width); var height = checked((int)decoded.Height);
            var stride = checked(width * 4);
            compact = new byte[checked(stride * height)];
            for (var y = 0; y < height; y++)
                decoded.BgraPremultipliedPixels.AsSpan(checked(y * (int)decoded.Stride), stride).CopyTo(compact.AsSpan(y * stride, stride));
            cancellationToken.ThrowIfCancellationRequested();
            return new(width, height, stride, compact);
        }
        finally
        {
            if (compact is not null) Array.Clear(compact);
            PictureFilesSourceRenderer.ClearFrame(decoded);
        }
    }
}
