namespace HavenOS.Images;

/// <summary>Explicit platform composition; no decoding failure selects another provider.</summary>
public static class PictureSharedRasterProviders
{
    public static IPictureSharedRasterDecoder ForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return new PictureSkiaSharedRasterDecoder();
        if (OperatingSystem.IsLinux()) return new PictureGlycinSharedRasterProvider();
        throw new PlatformNotSupportedException("No maintained shared raster provider is selected for this platform.");
    }
}
