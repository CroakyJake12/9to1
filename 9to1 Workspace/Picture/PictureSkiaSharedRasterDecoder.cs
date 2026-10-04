using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using HavenOS.Home.Core;
using SkiaSharp;

namespace HavenOS.Images;

/// <summary>
/// A refusal to present an encoded source whose required representation is not
/// established by this provider. Original bytes remain authoritative.
/// </summary>
public sealed class PictureSharedRasterUnavailableException(
    string? detectedMimeType, string message) : NotSupportedException(message)
{
    public string? DetectedMimeType { get; } = detectedMimeType;
    public ImageMetadataAvailability MetadataAvailability => ImageMetadataAvailability.UnsupportedByReader;
}

/// <summary>
/// Uses the maintained Skia GIF codec for authorized Windows presentation bytes.
/// Other formats require their own proven metadata/animation path and are refused.
/// Input/frame admission limits are the existing Picture/shared-raster limits;
/// they do not bound native metadata allocation, decoding time, or interruption.
/// </summary>
public sealed class PictureSkiaSharedRasterDecoder : IPictureSharedRasterDecoder
{
    public IPictureSharedRasterFrameSession OpenFrames(
        ReadOnlySpan<byte> encoded, bool loopAnimation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("This provider requires the maintained Windows Skia package.");
        if (encoded.IsEmpty || encoded.Length > PictureGlycinDecoder.MaximumBufferBytes)
            throw new ArgumentException("Picture encoded image is empty or exceeds the native materialization limit.", nameof(encoded));
        var original = new FrameSession(loopAnimation);
        try
        {
            original.Initialize(encoded, cancellationToken);
            return original;
        }
        catch (Exception error)
        {
            var errors = new List<Exception>();
            Add(errors, error);
            original.RetainFailure(error);
            Attempt(errors, original.Dispose);
            Throw(errors);
            throw;
        }
    }

    private sealed class FrameSession(bool loopAnimation) : IPictureSharedRasterFrameSession
    {
        private readonly object _gate = new();
        private byte[]? _encoded;
        private SKData? _data;
        private SKCodec? _codec;
        private SKColorSpace? _destinationColorSpace;
        private SKImageInfo _destination;
        private int _frameCount, _metadataFrameCount, _nextFrame;
        private bool _ended, _closed, _closeAttempted;
        private Exception? _failure, _closeFailure;

        public string MimeType
        {
            get
            {
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_closed, this);
                    if (_failure is not null) ExceptionDispatchInfo.Capture(_failure).Throw();
                    return "image/gif";
                }
            }
        }

        internal void RetainFailure(Exception error) => _failure ??= error;

        internal void Initialize(ReadOnlySpan<byte> encoded, CancellationToken cancellationToken)
        {
            // Capture the caller's source synchronously before any native invocation.
            _encoded = encoded.ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            _data = SKData.CreateCopy(_encoded)
                ?? throw new InvalidDataException("The maintained codec could not capture the encoded source.");
            _codec = SKCodec.Create(_data)
                ?? throw new InvalidDataException("The maintained codec refused the encoded source.");
            cancellationToken.ThrowIfCancellationRequested();
            var format = _codec.EncodedFormat;
            if (format != SKEncodedImageFormat.Gif)
                throw new PictureSharedRasterUnavailableException(DetectedMime(format),
                    "This Windows provider has no established metadata and animation contract for this encoded format.");
            if (_codec.EncodedOrigin != SKEncodedOrigin.TopLeft)
                throw new PictureSharedRasterUnavailableException("image/gif",
                    "The encoded orientation requires a proven orientation-aware presentation path.");

            // The exact maintained native API returns original dimensions at scale >= 1.
            // Avoid obtaining an additional borrowed source-colour-space wrapper.
            var size = _codec.GetScaledDimensions(1.0f);
            if (size.Width is <= 0 or > 8192 || size.Height is <= 0 or > 8192 ||
                (long)size.Width * size.Height * 4 > 64 * 1024 * 1024)
                throw new PictureSharedRasterUnavailableException("image/gif",
                    "The decoded frame exceeds the shared raster bounds.");
            _destinationColorSpace = SKColorSpace.CreateSrgb()
                ?? throw new PictureSharedRasterUnavailableException("image/gif", "The native sRGB conversion surface is unavailable.");
            _destination = new(size.Width, size.Height, SKColorType.Bgra8888,
                SKAlphaType.Premul, _destinationColorSpace);

            // FrameCount may allocate a full native metadata vector. No hard
            // frame-count/allocation/CPU bound is inferred from encoded-byte limits.
            _metadataFrameCount = _codec.FrameCount;
            if (_metadataFrameCount < 0)
                throw new InvalidDataException("The GIF codec returned an invalid frame count.");
            // The maintained API exposes no animation metadata for a still.
            // Such a source still requires one successful native frameIndex 0 decode.
            _frameCount = Math.Max(1, _metadataFrameCount);
            for (var index = 0; index < _metadataFrameCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_codec.GetFrameInfo(index, out var frame) || !frame.FullyRecieved || frame.Duration < 0)
                    throw new InvalidDataException("The GIF source does not contain complete, valid frame metadata.");
                _ = checked((long)frame.Duration * 1000);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        public PictureSharedAnimationFrame? TryNextFrame(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                if (_failure is not null) ExceptionDispatchInfo.Capture(_failure).Throw();
                byte[]? pixels = null;
                var pinned = default(GCHandle);
                PictureSharedAnimationFrame? result = null;
                var errors = new List<Exception>();
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_ended) return null;
                    if (_nextFrame == _frameCount)
                    {
                        if (loopAnimation && _frameCount > 1) _nextFrame = 0;
                        else { _ended = true; return null; }
                    }
                    var codec = _codec ?? throw new ObjectDisposedException(nameof(FrameSession));
                    long delayMicroseconds = 0;
                    if (_metadataFrameCount != 0)
                    {
                        if (!codec.GetFrameInfo(_nextFrame, out var frame) || !frame.FullyRecieved || frame.Duration < 0)
                            throw new InvalidDataException("The GIF frame metadata is unavailable or incomplete.");
                        delayMicroseconds = checked((long)frame.Duration * 1000);
                    }
                    pixels = new byte[checked(_destination.RowBytes * _destination.Height)];
                    pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                    cancellationToken.ThrowIfCancellationRequested();
                    // PriorFrame=-1 delegates prerequisite decoding, blending and
                    // disposal to the original maintained codec; no compositor clone.
                    var decoded = codec.GetPixels(_destination, pinned.AddrOfPinnedObject(),
                        _destination.RowBytes, new SKCodecOptions(_nextFrame, -1));
                    if (decoded != SKCodecResult.Success)
                        throw new InvalidDataException($"The maintained GIF codec refused a complete frame: {decoded}.");
                    // The synchronous native call has now returned. Cancellation
                    // is observed here; it does not certify native interruption.
                    cancellationToken.ThrowIfCancellationRequested();
                    result = new(new HomeProductivityRasterFrame(_destination.Width,
                        _destination.Height, _destination.RowBytes, pixels),
                        delayMicroseconds);
                    cancellationToken.ThrowIfCancellationRequested();
                    _nextFrame++;
                }
                catch (Exception error) { Add(errors, error); }
                finally
                {
                    if (pinned.IsAllocated) Attempt(errors, pinned.Free);
                    if (pixels is not null) Attempt(errors, () => Array.Clear(pixels));
                }
                if (errors.Count != 0)
                {
                    _failure = Combine(errors);
                    ExceptionDispatchInfo.Capture(_failure).Throw();
                }
                return result;
            }
        }

        public void Dispose()
        {
            // The same lock joins any original synchronous codec call before
            // encoded/temporary storage is cleared. Hosts must keep this wait off
            // their UI dispatcher and independently drain their presentation work.
            lock (_gate)
            {
                _closed = true;
                if (_closeAttempted)
                {
                    if (_closeFailure is not null) ExceptionDispatchInfo.Capture(_closeFailure).Throw();
                    return;
                }
                _closeAttempted = true;
                var errors = new List<Exception>();
                var codec = _codec; var data = _data;
                var destinationColorSpace = _destinationColorSpace; var encoded = _encoded;
                if (codec is not null) Attempt(errors, codec.Dispose);
                if (data is not null)
                {
                    Attempt(errors, () => data.Span.Clear());
                    Attempt(errors, data.Dispose);
                }
                if (destinationColorSpace is not null) Attempt(errors, destinationColorSpace.Dispose);
                if (encoded is not null) Attempt(errors, () => Array.Clear(encoded));
                if (errors.Count == 0)
                {
                    _codec = null; _data = null; _destinationColorSpace = null; _encoded = null;
                    return;
                }
                // Keep failed close ownership and the exact cleanup causes observable.
                // Frame/init causes remain in _failure and their original caller task;
                // a resource-only close does not replay an already-reported cancellation.
                _closeFailure = new OriginalCloseFailure(errors, this);
                ExceptionDispatchInfo.Capture(_closeFailure).Throw();
            }
        }
    }

    // A failed close keeps the exact owning session/resources strongly retained
    // with every original cleanup cause; repetition returns this same fault.
    private sealed class OriginalCloseFailure(
        IEnumerable<Exception> errors, FrameSession original) : AggregateException(errors)
    {
        public FrameSession RetainedOriginal { get; } = original;
    }

    private static string? DetectedMime(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Gif => "image/gif",
        SKEncodedImageFormat.Png => "image/png",
        SKEncodedImageFormat.Jpeg => "image/jpeg",
        SKEncodedImageFormat.Webp => "image/webp",
        SKEncodedImageFormat.Bmp => "image/bmp",
        _ => null
    };

    private static void Attempt(List<Exception> errors, Action action)
    {
        try { action(); } catch (Exception error) { Add(errors, error); }
    }
    private static void Add(List<Exception> errors, Exception error)
    {
        if (!errors.Any(existing => ReferenceEquals(existing, error))) errors.Add(error);
    }
    private static Exception Combine(List<Exception> errors) =>
        errors.Count == 1 ? errors[0] : new AggregateException(errors);
    private static void Throw(List<Exception> errors)
    {
        if (errors.Count != 0) ExceptionDispatchInfo.Capture(Combine(errors)).Throw();
    }
}
