namespace HavenOS.Images;

/// <summary>
/// Decodes an owning service's already-authorized encoded source into detached
/// presentation frames. This grants no asset authority or continuing access.
/// The host retains the original operation and rechecks source identity and
/// revision before decoding and before presentation.
/// </summary>
public interface IPictureSharedRasterDecoder
{
    IPictureSharedRasterFrameSession OpenFrames(
        ReadOnlySpan<byte> encoded,
        bool loopAnimation,
        CancellationToken cancellationToken);
}

/// <summary>
/// An owning decoder session. Returned frames own independent CPU copies;
/// disposal retires only this session's resources and temporary buffers.
/// Cancellation around a synchronous native call does not certify interruption.
/// </summary>
public interface IPictureSharedRasterFrameSession : IDisposable
{
    string MimeType { get; }
    PictureSharedAnimationFrame? TryNextFrame(CancellationToken cancellationToken);
}
