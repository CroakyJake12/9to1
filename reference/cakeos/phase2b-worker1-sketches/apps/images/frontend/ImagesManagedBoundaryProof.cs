using CakeOS.Images.Interop;

namespace CakeOS.Images.Frontend;

internal static class ImagesManagedBoundaryProof
{
    public static void Run()
    {
        var testPath = Environment.GetEnvironmentVariable("CAKEOS_IMAGES_TEST_FILE");
        if (string.IsNullOrEmpty(testPath) || !File.Exists(testPath))
        {
            Console.WriteLine("CAKEOS_IMAGES_TEST_FILE not set or file not found, skipping proof");
            return;
        }

        var status = LoupeBackendInterop.cake_images_decode(testPath, out var info);
        if (status != LoupeBackendInterop.Status.Ok)
            throw new InvalidOperationException($"Backend decode failed: {status}");

        if (info.Width <= 0 || info.Height <= 0)
            throw new InvalidOperationException("Decoded image has invalid dimensions");

        Console.WriteLine($"Decoded: {info.Width}x{info.Height} alpha={info.HasAlpha} colorspace={LoupeBackendInterop.PtrToStringUtf8(info.ColorSpace)} format={LoupeBackendInterop.PtrToStringUtf8(info.Format)}");

        LoupeBackendInterop.FreeString(info.ColorSpace);
        LoupeBackendInterop.FreeString(info.Format);

        var thumbStatus = LoupeBackendInterop.cake_images_thumbnail(testPath, 128, out var thumb);
        if (thumbStatus != LoupeBackendInterop.Status.Ok)
            throw new InvalidOperationException($"Thumbnail generation failed: {thumbStatus}");

        if (thumb.Data == IntPtr.Zero || thumb.Length == 0)
            throw new InvalidOperationException("Thumbnail data is empty");

        Console.WriteLine($"Thumbnail: {thumb.Width}x{thumb.Height} bytes={thumb.Length}");

        LoupeBackendInterop.cake_images_thumbnail_free(ref thumb);

        var renderStatus = LoupeBackendInterop.cake_images_render(info.Handle, new LoupeBackendInterop.Viewport
        {
            X = 0,
            Y = 0,
            Scale = 1.0,
            TargetWidth = info.Width,
            TargetHeight = info.Height,
        }, out var render);

        if (renderStatus != LoupeBackendInterop.Status.Ok)
            throw new InvalidOperationException($"Render failed: {renderStatus}");

        if (render.CairoSurface == IntPtr.Zero)
            throw new InvalidOperationException("Render returned null surface");

        Console.WriteLine($"Render: {render.Width}x{render.Height} stride={render.Stride}");

        var metaStatus = LoupeBackendInterop.cake_images_metadata_read(info.Handle, out var metadata);
        if (metaStatus == LoupeBackendInterop.Status.Ok)
        {
            Console.WriteLine($"Metadata: exif={LoupeBackendInterop.PtrToStringUtf8(metadata.ExifJson)?.Length ?? 0} chars");
            LoupeBackendInterop.FreeString(metadata.ExifJson);
            LoupeBackendInterop.FreeString(metadata.XmpJson);
            LoupeBackendInterop.FreeString(metadata.IptcJson);
            LoupeBackendInterop.FreeString(metadata.ColorProfile);
        }

        var transformStatus = LoupeBackendInterop.cake_images_transform_apply(info.Handle, LoupeBackendInterop.TransformOp.Rotate90, out var transform);
        if (transformStatus == LoupeBackendInterop.Status.Ok)
        {
            Console.WriteLine($"Transform: new_handle={transform.NewHandle} lossless={transform.Lossless}");
            LoupeBackendInterop.cake_images_free(transform.NewHandle);
        }

        LoupeBackendInterop.cake_images_free(info.Handle);

        Console.WriteLine("IMAGES_MANAGED_BOUNDARY_PROOF_READY");
    }
}