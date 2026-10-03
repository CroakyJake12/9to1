using HavenOS.Home.Core;

namespace HavenOS.Images;

/// <summary>PNG compatibility entry point over the single sandboxed native raster creator.</summary>
public sealed class PictureGlycinPngEncoder
{
    private readonly PictureGlycinRasterEncoder _encoder = new();
    public byte[] EncodeFlattenedFrame(HomeProductivityRasterFrame frame, byte compression = 50,
        CancellationToken cancellationToken = default) =>
        _encoder.EncodeFlattenedFrame(frame, PictureRasterExportFormat.Png, compression, cancellationToken: cancellationToken);
}
