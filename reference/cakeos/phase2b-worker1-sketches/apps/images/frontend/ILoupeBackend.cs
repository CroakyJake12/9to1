using CakeOS.Images.Interop;

namespace CakeOS.Images.Frontend;

public interface ILoupeBackend
{
    LoupeBackendInterop.Status Decode(string path, out LoupeBackendInterop.ImageInfo info);
    void Free(IntPtr handle);
    LoupeBackendInterop.Status Render(IntPtr handle, LoupeBackendInterop.Viewport viewport, out LoupeBackendInterop.RenderResult result);
    LoupeBackendInterop.Status ReadMetadata(IntPtr handle, out LoupeBackendInterop.Metadata metadata);
    LoupeBackendInterop.Status Thumbnail(string path, int maxPx, out LoupeBackendInterop.ThumbnailResult result);
    LoupeBackendInterop.Status Transform(IntPtr handle, LoupeBackendInterop.TransformOp op, out LoupeBackendInterop.TransformResult result);
    LoupeBackendInterop.Status ApplyColorProfile(IntPtr handle, string profilePath, int intent);
}

public sealed class LoupeBackend : ILoupeBackend
{
    public LoupeBackendInterop.Status Decode(string path, out LoupeBackendInterop.ImageInfo info)
    {
        return LoupeBackendInterop.cake_images_decode(path, out info);
    }

    public void Free(IntPtr handle)
    {
        LoupeBackendInterop.cake_images_free(handle);
    }

    public LoupeBackendInterop.Status Render(IntPtr handle, LoupeBackendInterop.Viewport viewport, out LoupeBackendInterop.RenderResult result)
    {
        return LoupeBackendInterop.cake_images_render(handle, viewport, out result);
    }

    public LoupeBackendInterop.Status ReadMetadata(IntPtr handle, out LoupeBackendInterop.Metadata metadata)
    {
        return LoupeBackendInterop.cake_images_metadata_read(handle, out metadata);
    }

    public LoupeBackendInterop.Status Thumbnail(string path, int maxPx, out LoupeBackendInterop.ThumbnailResult result)
    {
        return LoupeBackendInterop.cake_images_thumbnail(path, maxPx, out result);
    }

    public LoupeBackendInterop.Status Transform(IntPtr handle, LoupeBackendInterop.TransformOp op, out LoupeBackendInterop.TransformResult result)
    {
        return LoupeBackendInterop.cake_images_transform_apply(handle, op, out result);
    }

    public LoupeBackendInterop.Status ApplyColorProfile(IntPtr handle, string profilePath, int intent)
    {
        return LoupeBackendInterop.cake_images_color_profile_apply(handle, profilePath, intent);
    }
}