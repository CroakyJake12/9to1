namespace HavenOS.Images;

/// <summary>Uses the unchanged sandboxed glycin adapter for an owner's authorized source.</summary>
public sealed class PictureGlycinSharedRasterProvider : IPictureSharedRasterDecoder
{
    private readonly PictureGlycinSharedRasterDecoder _donor = new();

    public IPictureSharedRasterFrameSession OpenFrames(
        ReadOnlySpan<byte> encoded,
        bool loopAnimation,
        CancellationToken cancellationToken) =>
        new Session(_donor.OpenFrames(encoded, loopAnimation, cancellationToken));

    private sealed class Session(
        PictureGlycinSharedRasterDecoder.FrameSession original) : IPictureSharedRasterFrameSession
    {
        public string MimeType => original.MimeType;
        public PictureSharedAnimationFrame? TryNextFrame(CancellationToken cancellationToken) =>
            original.TryNextFrame(cancellationToken);
        public void Dispose() => original.Dispose();
    }
}
